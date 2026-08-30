using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text.RegularExpressions;
using AccountingAI.Core.Configuration;

namespace AccountingAI.Core.Phc;

public sealed partial class PhcAggregatedInvoiceService
{
    private const decimal EscudoFactor = 200.482m;
    private int _stampCounter;

    public async Task<PhcAggregatedInvoiceTarget> PrepareAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        PhcAggregatedInvoiceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(draft);
        if (company.InvoiceImportMode != PhcInvoiceImportMode.OneDocumentForGenericCustomer)
        {
            throw new InvalidOperationException(
                "A empresa não está configurada para 'Agrupa 1 documento no Cliente genérico'.");
        }

        if (company.GenericCustomerNumber is null)
        {
            throw new InvalidOperationException("Selecione primeiro o Cliente genérico PHC na configuração da empresa.");
        }

        var invoiceMappings = company.InvoiceSeriesMappings
            .Where(mapping => mapping.PhcDocumentNumber is not null &&
                              mapping.PhcTableName.Equals("TD", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var preferredMappings = invoiceMappings
            .Where(mapping => mapping.SaftDocumentType.Equals("FT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var documentNumbers = (preferredMappings.Count > 0 ? preferredMappings : invoiceMappings)
            .Select(mapping => mapping.PhcDocumentNumber!.Value)
            .Distinct()
            .ToList();
        if (documentNumbers.Count != 1)
        {
            throw new InvalidOperationException(
                "Para o lançamento agregado, as séries FT devem apontar para um único documento PHC da tabela TD.");
        }

        await using var connection = new OdbcConnection(PhcOdbcDataSource.BuildConnectionString(company, credential));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureNotDuplicateAsync(connection, null, draft.ImportReference, documentNumbers[0], cancellationToken)
            .ConfigureAwait(false);

        var client = await ReadSingleRowAsync(
            connection,
            null,
            "SELECT TOP (1) * FROM cl WHERE no = ? ORDER BY estab",
            [company.GenericCustomerNumber.Value],
            "Cliente genérico",
            cancellationToken).ConfigureAwait(false);
        var document = await ReadSingleRowAsync(
            connection,
            null,
            "SELECT TOP (1) * FROM td WHERE ndoc = ?",
            [documentNumbers[0]],
            "Documento de faturação TD",
            cancellationToken).ConfigureAwait(false);
        var configuredMappings = company.VatArticleMappings
            .Where(mapping => mapping.PhcArticleReference.Length > 0)
            .GroupBy(mapping => mapping.VatRate)
            .ToDictionary(group => group.Key, group => group.First());
        var targetLines = new List<PhcAggregatedTargetLine>();
        foreach (var sourceLine in draft.Lines)
        {
            if (!configuredMappings.TryGetValue(sourceLine.VatRate, out var mapping))
            {
                throw new InvalidOperationException(
                    $"A taxa {sourceLine.VatRate:N2}% não tem artigo PHC associado na configuração da empresa.");
            }

            var article = await ReadSingleRowAsync(
                connection,
                null,
                "SELECT TOP (1) * FROM st WHERE LTRIM(RTRIM(ref)) = ?",
                [mapping.PhcArticleReference],
                $"Artigo ST {mapping.PhcArticleReference}",
                cancellationToken).ConfigureAwait(false);
            var vatTable = ReadInt(article, "TABIVA");
            if (vatTable is < 1 or > 9)
            {
                throw new InvalidDataException(
                    $"O artigo {mapping.PhcArticleReference} tem a tabela de IVA {vatTable}; são aceites as tabelas 1 a 9.");
            }

            var vat = await ReadSingleRowAsync(
                connection,
                null,
                "SELECT TOP (1) * FROM taxasiva WHERE codigo = ?",
                [vatTable],
                $"Taxa TAXASIVA código {vatTable}",
                cancellationToken).ConfigureAwait(false);
            var phcVatRate = ReadDecimal(vat, "TAXA");
            if (Math.Abs(phcVatRate - sourceLine.VatRate) > 0.005m)
            {
                throw new InvalidDataException(
                    $"O artigo {mapping.PhcArticleReference} usa a tabela {vatTable} com IVA {phcVatRate:N2}%, " +
                    $"mas foi associado à taxa SAF-T {sourceLine.VatRate:N2}%.");
            }

            var movesStock = PhcStockPolicy.MovesStock(company.InvoiceImportMode, ReadInt(article, "STNS"));
            targetLines.Add(new PhcAggregatedTargetLine(
                sourceLine.VatRate,
                sourceLine.NetAmount,
                sourceLine.VatAmount,
                sourceLine.TaxExemptionCode,
                sourceLine.TaxExemptionReason,
                ReadString(article, "REF"),
                ReadString(article, "DESIGN"),
                ReadString(article, "UNIDADE", "UN"),
                ReadInt(article, "CPOC"),
                vatTable,
                phcVatRate,
                ReadBoolean(article, "SERVICO"),
                movesStock));
        }

        if (targetLines
            .GroupBy(line => line.VatTable)
            .Any(group => group.Select(line => line.PhcVatRate).Distinct().Count() > 1))
        {
            throw new InvalidDataException(
                "Duas taxas SAF-T diferentes ficaram associadas à mesma tabela de IVA PHC. Corrija os artigos configurados.");
        }

        ValidateDocumentConfiguration(document, targetLines);

        return new PhcAggregatedInvoiceTarget(
            company,
            draft,
            ReadLong(client, "NO"),
            ReadInt(client, "ESTAB"),
            ReadString(client, "NOME"),
            ReadString(client, "NCONT"),
            ReadString(client, "MORADA"),
            ReadString(client, "LOCAL"),
            ReadString(client, "CODPOST"),
            ReadString(client, "TELEFONE"),
            ReadLong(document, "NDOC"),
            ReadString(document, "NMDOC"),
            ReadInt(document, "TIPODOC"),
            ReadInt(document, "ARMAZEM"),
            ReadString(document, "TIPOSAFT", "FT"),
            targetLines);
    }

    public async Task<PhcAggregatedInvoiceResult> CreateAsync(
        PhcAggregatedInvoiceTarget target,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default) =>
        (await CreateManyAsync([target], credential, cancellationToken).ConfigureAwait(false)).Single();

    public async Task<IReadOnlyList<PhcAggregatedInvoiceResult>> CreateManyAsync(
        IReadOnlyList<PhcAggregatedInvoiceTarget> targets,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(credential);
        if (targets.Count == 0)
        {
            throw new ArgumentException("Não existem documentos PHC preparados para lançar.", nameof(targets));
        }

        var company = targets[0].Company;
        if (targets.Any(target =>
                !target.Company.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase) ||
                !target.Company.DatabaseName.Equals(company.DatabaseName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Todos os documentos do lote devem pertencer à mesma empresa e base de dados PHC.");
        }

        _stampCounter = 0;
        await using var connection = new OdbcConnection(
            PhcOdbcDataSource.BuildConnectionString(company, credential));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var results = new List<PhcAggregatedInvoiceResult>(targets.Count);
            foreach (var target in targets)
            {
                results.Add(await CreateWithinTransactionAsync(
                    target,
                    credential,
                    connection,
                    transaction,
                    cancellationToken).ConfigureAwait(false));
            }

            transaction.Commit();
            return results;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<PhcAggregatedInvoiceResult> CreateWithinTransactionAsync(
        PhcAggregatedInvoiceTarget target,
        PhcOdbcCredential credential,
        OdbcConnection connection,
        OdbcTransaction transaction,
        CancellationToken cancellationToken)
    {
        var ftStamp = NewStamp();
        var documentDate = target.Draft.DocumentDate.ToDateTime(TimeOnly.MinValue);
        var audit = AuditFields(credential.Username);
        await EnsureNotDuplicateAsync(
            connection,
            transaction,
            target.Draft.ImportReference,
            target.DocumentNumber,
            cancellationToken).ConfigureAwait(false);
        var sequentialNumber = await NextNumberAsync(
            connection,
            transaction,
            "SELECT COALESCE(MAX(fno), 0) + 1 FROM ft WITH (UPDLOCK, HOLDLOCK) WHERE ndoc = ? AND ftano = ?",
            [target.DocumentNumber, documentDate.Year],
            cancellationToken).ConfigureAwait(false);
        var header = Audit(new Dictionary<string, object?>
        {
            ["ftstamp"] = ftStamp,
            ["fno"] = sequentialNumber,
            ["ftano"] = documentDate.Year,
            ["ndoc"] = target.DocumentNumber,
            ["nmdoc"] = target.DocumentName,
            ["tipodoc"] = target.InternalDocumentType,
            ["fdata"] = documentDate,
            ["pdata"] = documentDate,
            ["cdata"] = documentDate,
            ["no"] = target.CustomerNumber,
            ["estab"] = target.CustomerEstablishment,
            ["nome"] = target.CustomerName,
            ["nome2"] = Limit(target.CustomerName, 55),
            ["ncont"] = target.CustomerTaxId,
            ["morada"] = target.CustomerAddress,
            ["local"] = target.CustomerLocation,
            ["codpost"] = target.CustomerPostalCode,
            ["telefone"] = target.CustomerPhone,
            ["moeda"] = IsEuro(target.Draft.CurrencyCode) ? "Euro" : "PTE ou EURO",
            ["memissao"] = "EURO",
            ["cambio"] = 1m,
            ["ettiliq"] = target.Draft.NetTotal,
            ["ttiliq"] = Esc(target.Draft.NetTotal),
            ["ettiva"] = target.Draft.VatTotal,
            ["ttiva"] = Esc(target.Draft.VatTotal),
            ["etotal"] = target.Draft.GrossTotal,
            ["total"] = Esc(target.Draft.GrossTotal),
            ["etot1"] = target.Draft.NetTotal,
            ["tot1"] = Esc(target.Draft.NetTotal),
            ["totqtt"] = target.Lines.Count,
            ["qtt1"] = target.Lines.Count,
            ["fref"] = target.Draft.ImportReference,
            ["final"] = Limit($"Accounting AI: {target.Draft.DocumentCount:N0} docs origem — {target.Draft.GroupLabel}", 75),
            ["anulado"] = false,
            ["aprovado"] = false,
            ["facturada"] = false,
            ["impresso"] = false,
            ["plano"] = false,
            ["nprotri"] = false
        }, audit);
        foreach (var vatGroup in target.Lines.GroupBy(line => line.VatTable))
        {
            var slot = vatGroup.Key;
            var net = vatGroup.Sum(line => line.TaxableBase);
            var vat = vatGroup.Sum(line => line.VatAmount);
            header[$"eivain{slot}"] = net;
            header[$"ivain{slot}"] = Esc(net);
            header[$"ivatx{slot}"] = vatGroup.First().PhcVatRate;
            header[$"eivav{slot}"] = vat;
            header[$"ivav{slot}"] = Esc(vat);
        }
        await InsertAsync(connection, transaction, "ft", header, ["ftstamp", "fno", "ndoc", "no", "fdata", "etotal"], cancellationToken)
            .ConfigureAwait(false);

        var exemptions = target.Lines
            .Where(line => line.TaxExemptionCode.Length > 0 || line.TaxExemptionReason.Length > 0)
            .Select(line => (line.TaxExemptionCode, line.TaxExemptionReason))
            .Distinct()
            .ToList();
        var documentExemption = exemptions.Count == 1 ? exemptions[0] : default;
        await InsertAsync(connection, transaction, "ft2", Audit(new Dictionary<string, object?>
        {
            ["ft2stamp"] = ftStamp,
            ["tiposaft"] = target.SaftDocumentType,
            ["reexgiva"] = false,
            ["operext"] = false,
            ["ndocmanual"] = target.Draft.ImportReference,
            ["codmotiseimp"] = Limit(documentExemption.TaxExemptionCode ?? string.Empty, 3),
            ["motiseimp"] = Limit(documentExemption.TaxExemptionReason ?? string.Empty, 120)
        }, audit), ["ft2stamp"], cancellationToken).ConfigureAwait(false);

        await InsertAsync(connection, transaction, "ft3", Audit(new Dictionary<string, object?>
        {
            ["ft3stamp"] = ftStamp,
            ["invoiceyear"] = documentDate.Year,
            ["invoicenoORI"] = target.Draft.ImportReference,
            ["taxpointdt"] = documentDate,
            ["oridata"] = documentDate,
            ["oriinis"] = Limit(credential.Username, 30)
        }, audit), ["ft3stamp"], cancellationToken).ConfigureAwait(false);

        var fttGroups = target.Lines
            .GroupBy(line => (line.VatTable, line.PhcVatRate))
            .Select(group => new
            {
                VatTable = group.Key.VatTable,
                VatRate = group.Key.PhcVatRate,
                NetAmount = group.Sum(line => line.TaxableBase),
                VatAmount = group.Sum(line => line.VatAmount)
            })
            .OrderBy(group => group.VatTable)
            .ToList();
        foreach (var taxTotal in fttGroups)
        {
            var fttStamp = NewStamp();
            await InsertAsync(connection, transaction, "ftt", Audit(new Dictionary<string, object?>
            {
                ["fttstamp"] = fttStamp,
                ["ftstamp"] = ftStamp,
                ["codigo"] = taxTotal.VatTable,
                ["taxa"] = taxTotal.VatRate,
                ["ebaseinc"] = taxTotal.NetAmount,
                ["baseinc"] = Esc(taxTotal.NetAmount),
                ["evalor"] = taxTotal.VatAmount,
                ["valor"] = Esc(taxTotal.VatAmount),
                ["baseincm"] = 0m,
                ["valorm"] = 0m,
                ["marcada"] = false
            }, audit), ["fttstamp", "ftstamp", "codigo", "taxa", "ebaseinc", "evalor"], cancellationToken).ConfigureAwait(false);
        }

        var lineOrder = 0;
        foreach (var line in target.Lines)
        {
            lineOrder += 1000;
            var fiStamp = NewStamp();
            var grossAmount = line.GrossAmount;
            var taxableBase = grossAmount - line.VatAmount;
            await InsertAsync(connection, transaction, "fi", Audit(new Dictionary<string, object?>
            {
                ["fistamp"] = fiStamp,
                ["ftstamp"] = ftStamp,
                ["ref"] = line.ArticleReference,
                ["design"] = line.ArticleDesignation,
                ["unidade"] = line.Unit,
                ["cpoc"] = line.Cpoc,
                ["qtt"] = 1m,
                ["armazem"] = 1,
                // Nos documentos PHC NDOC=1 desta empresa, as linhas são lançadas
                // com preço IVA incluído: EPV/ETILIQUIDO guardam o total com IVA e
                // ESLVU/ESLTT guardam a base tributável.
                ["epv"] = grossAmount,
                ["pv"] = Esc(grossAmount),
                ["epvori"] = 0m,
                ["pvori"] = 0m,
                ["etiliquido"] = grossAmount,
                ["tiliquido"] = decimal.Round(Esc(grossAmount), 0, MidpointRounding.AwayFromZero),
                ["tliquido"] = grossAmount,
                ["eslvu"] = taxableBase,
                ["slvu"] = Esc(taxableBase),
                ["esltt"] = taxableBase,
                ["sltt"] = Esc(taxableBase),
                ["evbase"] = 0m,
                ["vbase"] = 0m,
                ["evvenda"] = 0m,
                ["vvenda"] = 0m,
                ["tabiva"] = line.VatTable,
                ["iva"] = line.PhcVatRate,
                ["ivaincl"] = true,
                ["codmotiseimp"] = Limit(line.TaxExemptionCode, 3),
                ["motiseimp"] = Limit(line.TaxExemptionReason, 120),
                ["stns"] = line.IsService || !line.MovesStock,
                ["nmdoc"] = target.DocumentName,
                ["ndoc"] = target.DocumentNumber,
                ["fno"] = sequentialNumber,
                ["tipodoc"] = target.InternalDocumentType,
                ["taxpointdt"] = documentDate,
                ["lordem"] = lineOrder,
                ["lrecno"] = string.Empty,
                ["nprotri"] = false
            }, audit), ["fistamp", "ftstamp", "ref", "cpoc", "tabiva", "iva"], cancellationToken)
                .ConfigureAwait(false);

            await InsertAsync(connection, transaction, "fi2", Audit(new Dictionary<string, object?>
            {
                ["fi2stamp"] = fiStamp,
                ["ftstamp"] = ftStamp,
                ["origem"] = "Accounting AI",
                ["originatingon"] = target.Draft.ImportReference,
                ["orderdate"] = documentDate,
                ["prestSrv"] = line.IsService
            }, audit), ["fi2stamp", "ftstamp"], cancellationToken).ConfigureAwait(false);
        }

        var insertedLines = await ScalarLongAsync(
            connection,
            transaction,
            "SELECT COUNT(*) FROM fi WHERE ftstamp = ?",
            [ftStamp],
            cancellationToken).ConfigureAwait(false);
        if (insertedLines != target.Lines.Count)
        {
            throw new InvalidOperationException(
                $"A verificação transacional encontrou {insertedLines} linhas FI; eram esperadas {target.Lines.Count}.");
        }

        await ValidateStoredTotalsAsync(connection, transaction, ftStamp, target.Draft, cancellationToken)
            .ConfigureAwait(false);
        await ValidateStoredFttAsync(connection, transaction, ftStamp, fttGroups.Count, target.Draft, cancellationToken)
            .ConfigureAwait(false);

        return new PhcAggregatedInvoiceResult(
            ftStamp,
            sequentialNumber,
            target.Lines.Count,
            fttGroups.Count,
            target.Draft.GrossTotal);
    }

    private static void ValidateDocumentConfiguration(
        IReadOnlyDictionary<string, object?> document,
        IReadOnlyCollection<PhcAggregatedTargetLine> lines)
    {
        var name = ReadString(document, "NMDOC", "Documento PHC");
        if (ReadBoolean(document, "FECHADA"))
        {
            throw new InvalidOperationException($"A série PHC '{name}' está fechada.");
        }

        if (!ReadBoolean(document, "DOCSIMPORT") && !ReadBoolean(document, "DOCSEXT"))
        {
            throw new InvalidOperationException(
                $"O documento PHC '{name}' não está configurado como série para documentos importados ou externos.");
        }

        if (ReadBoolean(document, "LANCASL") && lines.Any(line => line.MovesStock))
        {
            throw new InvalidOperationException(
                $"O documento PHC '{name}' movimenta stocks e contém linhas configuradas para movimentação. " +
                "O lançamento só é permitido quando todas as linhas estão marcadas como não movimentando stock.");
        }

        if (ReadBoolean(document, "AUTOML"))
        {
            throw new InvalidOperationException(
                $"O documento PHC '{name}' está configurado para integração contabilística imediata (TD.AUTOML ativo), ainda fora desta fase.");
        }
    }

    private static async Task EnsureNotDuplicateAsync(
        OdbcConnection connection,
        OdbcTransaction? transaction,
        string importReference,
        long documentNumber,
        CancellationToken cancellationToken)
    {
        var existing = await ScalarStringAsync(
            connection,
            transaction,
            "SELECT TOP (1) LTRIM(RTRIM(ftstamp)) FROM ft WHERE fref = ? AND ndoc = ?",
            [importReference, documentNumber],
            cancellationToken).ConfigureAwait(false);
        if (existing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Este conjunto SAF-T já foi lançado no PHC com o stamp {existing}. Não foi criado um duplicado.");
        }
    }

    private static async Task ValidateStoredTotalsAsync(
        OdbcConnection connection,
        OdbcTransaction transaction,
        string ftStamp,
        PhcAggregatedInvoiceDraft draft,
        CancellationToken cancellationToken)
    {
        var stored = await ReadSingleRowAsync(
            connection,
            transaction,
            "SELECT ettiliq, ettiva, etotal, etot1, ttiliq, ttiva, total, tot1 FROM ft WHERE ftstamp = ?",
            [ftStamp],
            "Totais do documento FT acabado de criar",
            cancellationToken).ConfigureAwait(false);
        var comparisons = new (string Field, decimal Expected)[]
        {
            ("ETTILIQ", draft.NetTotal),
            ("ETTIVA", draft.VatTotal),
            ("ETOTAL", draft.GrossTotal),
            ("ETOT1", draft.NetTotal),
            ("TTILIQ", Esc(draft.NetTotal)),
            ("TTIVA", Esc(draft.VatTotal)),
            ("TOTAL", Esc(draft.GrossTotal)),
            ("TOT1", Esc(draft.NetTotal))
        };
        var invalid = comparisons
            .Where(item => Math.Abs(ReadDecimal(stored, item.Field) - item.Expected) > 0.01m)
            .Select(item => $"{item.Field}: esperado {item.Expected:N2}, gravado {ReadDecimal(stored, item.Field):N2}")
            .ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                "Os totais da FT não ficaram gravados corretamente: " + string.Join("; ", invalid));
        }
    }

    private static async Task ValidateStoredFttAsync(
        OdbcConnection connection,
        OdbcTransaction transaction,
        string ftStamp,
        int expectedRows,
        PhcAggregatedInvoiceDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(
            connection,
            transaction,
            "SELECT COUNT(*), COUNT(DISTINCT fttstamp), COUNT(DISTINCT codigo), " +
            "COALESCE(SUM(ebaseinc), 0), COALESCE(SUM(evalor), 0) FROM ftt WHERE ftstamp = ?",
            [ftStamp]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Não foi possível validar os totais FTT do documento criado.");
        }

        var storedRows = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        var storedStamps = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
        var storedCodes = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
        var storedNet = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
        var storedVat = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture);
        if (storedRows != expectedRows ||
            storedStamps != expectedRows ||
            storedCodes != expectedRows ||
            Math.Abs(storedNet - draft.NetTotal) > 0.01m ||
            Math.Abs(storedVat - draft.VatTotal) > 0.01m)
        {
            throw new InvalidOperationException(
                $"A FTT não ficou reconciliada: registos {storedRows}/{expectedRows}, " +
                $"stamps {storedStamps}/{expectedRows}, códigos {storedCodes}/{expectedRows}, " +
                $"base {storedNet:N2}/{draft.NetTotal:N2}, IVA {storedVat:N2}/{draft.VatTotal:N2}.");
        }
    }

    private static async Task<Dictionary<string, object?>> ReadSingleRowAsync(
        OdbcConnection connection,
        OdbcTransaction? transaction,
        string sql,
        IReadOnlyList<object?> values,
        string description,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(connection, transaction, sql, values);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException($"Não foi encontrado no PHC: {description}.");
        }

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }
        return row;
    }

    private static async Task<long> NextNumberAsync(
        OdbcConnection connection,
        OdbcTransaction transaction,
        string sql,
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, sql, values, cancellationToken).ConfigureAwait(false);

    private static async Task<long> ScalarLongAsync(
        OdbcConnection connection,
        OdbcTransaction? transaction,
        string sql,
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(connection, transaction, sql, values);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull
            ? 0L
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(
        OdbcConnection connection,
        OdbcTransaction? transaction,
        string sql,
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(connection, transaction, sql, values);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? string.Empty : Convert.ToString(result)?.Trim() ?? string.Empty;
    }

    private static OdbcCommand BuildCommand(
        OdbcConnection connection,
        OdbcTransaction? transaction,
        string sql,
        IReadOnlyList<object?> values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 60;
        foreach (var value in values)
        {
            AddParameter(command, value);
        }
        return command;
    }

    private static async Task InsertAsync(
        OdbcConnection connection,
        OdbcTransaction transaction,
        string table,
        IReadOnlyDictionary<string, object?> requestedValues,
        IReadOnlyCollection<string> requiredColumns,
        CancellationToken cancellationToken)
    {
        if (!SqlIdentifierRegex().IsMatch(table))
        {
            throw new InvalidOperationException($"Nome de tabela SQL inválido: {table}.");
        }

        var availableColumns = await GetColumnsAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false);
        var identityColumns = await GetIdentityColumnsAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false);
        var missing = requiredColumns.Where(column => !availableColumns.Contains(column)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"A tabela {table.ToUpperInvariant()} não contém os campos obrigatórios: {string.Join(", ", missing)}.");
        }

        var values = requestedValues
            .Where(pair => availableColumns.Contains(pair.Key) && !identityColumns.Contains(pair.Key))
            .ToList();
        var columns = string.Join(", ", values.Select(pair => $"[{pair.Key}]"));
        var placeholders = string.Join(", ", values.Select(_ => "?"));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO dbo.[{table}] ({columns}) VALUES ({placeholders})";
        command.CommandTimeout = 60;
        foreach (var pair in values)
        {
            AddParameter(command, pair.Value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OdbcException exception)
        {
            throw new InvalidOperationException(
                $"Erro ao inserir na tabela {table.ToUpperInvariant()}: {exception.Message}",
                exception);
        }
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        OdbcConnection connection,
        OdbcTransaction transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT TOP (0) * FROM dbo.[{table}]";
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
        return Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<string>> GetIdentityColumnsAsync(
        OdbcConnection connection,
        OdbcTransaction transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(
            connection,
            transaction,
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(?) AND is_identity = 1",
            [$"dbo.{table}"]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static void AddParameter(OdbcCommand command, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.Value = value ?? DBNull.Value;
        if (value is decimal)
        {
            parameter.OdbcType = OdbcType.Decimal;
            parameter.Precision = 18;
            parameter.Scale = 6;
        }
        command.Parameters.Add(parameter);
    }

    private Dictionary<string, object?> AuditFields(string username)
    {
        var user = Limit(username.Trim(), 30);
        return new Dictionary<string, object?>
        {
            ["ousrinis"] = user,
            ["usrinis"] = user,
            ["ousrdata"] = DateTime.Today,
            ["usrdata"] = DateTime.Today,
            ["ousrhora"] = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["usrhora"] = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
        };
    }

    private static Dictionary<string, object?> Audit(
        Dictionary<string, object?> values,
        IReadOnlyDictionary<string, object?> audit)
    {
        foreach (var pair in audit)
        {
            values[pair.Key] = pair.Value;
        }
        return values;
    }

    private string NewStamp() => $"SAF{DateTime.Now:yyyyMMddHHmmssfff}{++_stampCounter:000}";

    private static decimal Esc(decimal value) =>
        decimal.Round(value * EscudoFactor, 5, MidpointRounding.AwayFromZero);

    private static bool IsEuro(string currencyCode) =>
        currencyCode.Equals("EUR", StringComparison.OrdinalIgnoreCase) ||
        currencyCode.Equals("EURO", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(
        IReadOnlyDictionary<string, object?> row,
        string field,
        string defaultValue = "") =>
        row.TryGetValue(field, out var value) && value is not null and not DBNull
            ? Convert.ToString(value)?.Trim() ?? defaultValue
            : defaultValue;

    private static long ReadLong(IReadOnlyDictionary<string, object?> row, string field) =>
        row.TryGetValue(field, out var value) && value is not null and not DBNull
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : 0L;

    private static int ReadInt(IReadOnlyDictionary<string, object?> row, string field) =>
        row.TryGetValue(field, out var value) && value is not null and not DBNull
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : 0;

    private static decimal ReadDecimal(IReadOnlyDictionary<string, object?> row, string field) =>
        row.TryGetValue(field, out var value) && value is not null and not DBNull
            ? Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            : 0m;

    private static bool ReadBoolean(IReadOnlyDictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value is null or DBNull)
        {
            return false;
        }

        return value is bool boolean
            ? boolean
            : Convert.ToString(value)?.Trim().ToUpperInvariant() is "1" or "T" or "Y" or "S" or "TRUE";
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlIdentifierRegex();
}
