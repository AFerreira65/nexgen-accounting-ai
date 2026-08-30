using System.Data.Common;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AccountingAI.Core.Configuration;
using AccountingAI.Core.Models;

namespace AccountingAI.Core.Phc;

public sealed class PhcPurchaseAuditService
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-PT");

    public PurchaseAuditResult CreatePending(
        RecordTable? purchases,
        RecordTable? lines,
        PurchaseAuditOptions? options = null)
    {
        var efatura = ReadEfaturaDocuments(purchases, lines);
        var (searchStart, searchEnd) = SuggestSearchRange(efatura);
        return BuildResult(efatura, [], searchStart, searchEnd, options ?? PurchaseAuditOptions.Default);
    }

    public async Task<PurchaseAuditResult> AuditAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        RecordTable? purchases,
        RecordTable? lines,
        DateOnly searchStart,
        DateOnly searchEnd,
        PurchaseAuditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var efatura = ReadEfaturaDocuments(purchases, lines);
        if (efatura.Count == 0)
        {
            return BuildResult([], [], searchStart, searchEnd, options ?? PurchaseAuditOptions.FromCompany(company));
        }

        if (searchEnd < searchStart)
        {
            throw new ArgumentException("A data final da auditoria não pode ser anterior à data inicial.");
        }

        var phc = await ReadPhcDocumentsAsync(
            company,
            credential,
            searchStart,
            searchEnd,
            cancellationToken).ConfigureAwait(false);
        return BuildResult(
            efatura,
            phc,
            searchStart,
            searchEnd,
            options ?? PurchaseAuditOptions.FromCompany(company));
    }

    public static PurchaseAuditResult Compare(
        IReadOnlyList<PurchaseAuditDocument> efatura,
        IReadOnlyList<PurchaseAuditDocument> phc,
        DateOnly? searchStart = null,
        DateOnly? searchEnd = null,
        PurchaseAuditOptions? options = null,
        IReadOnlyDictionary<string, string>? manualMatches = null) =>
        BuildResult(
            efatura,
            phc,
            searchStart,
            searchEnd,
            options ?? PurchaseAuditOptions.Default,
            manualMatches);

    public static (DateOnly? Start, DateOnly? End) SuggestSearchRange(
        IReadOnlyList<PurchaseAuditDocument> efatura)
    {
        var dates = efatura.Where(document => document.InvoiceDate.HasValue)
            .Select(document => document.InvoiceDate!.Value)
            .ToArray();
        if (dates.Length == 0)
        {
            return (null, null);
        }

        var firstMonth = new DateOnly(dates.Min().Year, dates.Min().Month, 1);
        var lastMonth = new DateOnly(dates.Max().Year, dates.Max().Month, 1);
        return (firstMonth.AddMonths(-1), lastMonth.AddMonths(2).AddDays(-1));
    }

    private static async Task<IReadOnlyList<PurchaseAuditDocument>> ReadPhcDocumentsAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        DateOnly searchStart,
        DateOnly searchEnd,
        CancellationToken cancellationToken)
    {
        await using var connection = new OdbcConnection(PhcOdbcDataSource.BuildConnectionString(company, credential));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var documents = new Dictionary<string, MutablePhcDocument>(StringComparer.OrdinalIgnoreCase);
        var endExclusive = searchEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT fostamp, adoc, docnome, data, docdata, no, ncont, nome,
                       etotal, ettiliq, ettiva, total, ttiliq, ttiva, moeda
                FROM fo
                WHERE (data >= ? AND data < ?) OR (docdata >= ? AND docdata < ?)
                ORDER BY data, adoc, fostamp
                """;
            AddDateRange(command, searchStart.ToDateTime(TimeOnly.MinValue), endExclusive);
            command.CommandTimeout = 90;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var stamp = ReadString(reader, 0);
                if (stamp.Length == 0)
                {
                    continue;
                }

                var euroGross = ReadDecimal(reader, 8);
                var euroNet = ReadDecimal(reader, 9);
                var euroVat = ReadDecimal(reader, 10);
                var useEuro = euroGross != 0m || euroNet != 0m || euroVat != 0m;
                documents[stamp] = new MutablePhcDocument(
                    stamp,
                    ReadString(reader, 1),
                    ReadString(reader, 2),
                    ReadDate(reader, 4),
                    ReadDate(reader, 3),
                    ReadString(reader, 6),
                    ReadString(reader, 7),
                    useEuro ? euroNet : ReadDecimal(reader, 12),
                    useEuro ? euroVat : ReadDecimal(reader, 13),
                    useEuro ? euroGross : ReadDecimal(reader, 11));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT fot.fostamp, fot.codigo, fot.taxa,
                       fot.ebaseinc, fot.evalor, fot.baseinc, fot.valor
                FROM fot
                INNER JOIN fo ON fo.fostamp = fot.fostamp
                WHERE (fo.data >= ? AND fo.data < ?) OR (fo.docdata >= ? AND fo.docdata < ?)
                ORDER BY fot.fostamp, fot.codigo, fot.taxa
                """;
            AddDateRange(command, searchStart.ToDateTime(TimeOnly.MinValue), endExclusive);
            command.CommandTimeout = 90;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var stamp = ReadString(reader, 0);
                if (!documents.TryGetValue(stamp, out var document))
                {
                    continue;
                }

                var euroBase = ReadDecimal(reader, 3);
                var euroVat = ReadDecimal(reader, 4);
                var useEuro = euroBase != 0m || euroVat != 0m;
                document.Taxes.Add(new PurchaseAuditTaxTotal(
                    ReadDecimal(reader, 2),
                    useEuro ? euroBase : ReadDecimal(reader, 5),
                    useEuro ? euroVat : ReadDecimal(reader, 6)));
            }
        }

        return documents.Values
            .Select(document => new PurchaseAuditDocument(
                document.Stamp,
                "PHC",
                document.SupplierTaxId,
                document.SupplierName,
                document.InvoiceNumber,
                document.DocumentType,
                document.InvoiceDate,
                document.PostingDate,
                document.NetTotal,
                document.VatTotal,
                document.GrossTotal,
                AggregateTaxes(document.Taxes),
                document.Stamp))
            .ToList();
    }

    private static void AddDateRange(OdbcCommand command, DateTime start, DateTime endExclusive)
    {
        foreach (var value in new[] { start, endExclusive, start, endExclusive })
        {
            command.Parameters.Add("date", OdbcType.DateTime).Value = value;
        }
    }

    private static List<PurchaseAuditDocument> ReadEfaturaDocuments(RecordTable? purchases, RecordTable? lines)
    {
        if (purchases is null)
        {
            return [];
        }

        var linesByDocument = (lines?.Rows ?? [])
            .GroupBy(LineIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var documents = new List<PurchaseAuditDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in purchases.Rows)
        {
            var identity = DocumentIdentity(record);
            if (!seen.Add(identity))
            {
                continue;
            }

            linesByDocument.TryGetValue(identity, out var fiscalLines);
            fiscalLines ??= [];
            var taxes = fiscalLines
                .Select(line => new
                {
                    HasRate = TryReadRate(First(line, "Tax/TaxPercentage", "TaxPercentage"), out var rate),
                    Rate = rate,
                    Base = ReadAmount(First(line, "NetAmount", "TaxBase", "TotalTaxBase")),
                    Vat = ReadAmount(line["TaxAmount"])
                })
                .Where(item => item.HasRate)
                .Select(item => new PurchaseAuditTaxTotal(item.Rate, item.Base, item.Vat))
                .ToList();
            var lineNet = fiscalLines.Sum(line => ReadAmount(First(line, "NetAmount", "TaxBase", "TotalTaxBase")));
            var lineVat = fiscalLines.Sum(line => ReadAmount(line["TaxAmount"]));
            var lineGross = fiscalLines.Sum(line => ReadAmount(First(line, "GrossAmount", "GrossTotal")));
            var netText = record["NetTotal"];
            var vatText = record["TaxPayable"];
            var grossText = record["GrossTotal"];
            documents.Add(new PurchaseAuditDocument(
                identity,
                record.SourceFile,
                record["TaxRegistrationNumber"].Trim(),
                record["SupplierName"].Trim(),
                record["InvoiceNo"].Trim(),
                record["InvoiceType"].Trim(),
                ReadDate(record["InvoiceDate"]),
                null,
                netText.Length > 0 ? ReadAmount(netText) : lineNet,
                vatText.Length > 0 ? ReadAmount(vatText) : lineVat,
                grossText.Length > 0 ? ReadAmount(grossText) : lineGross,
                AggregateTaxes(taxes)));
        }

        return documents;
    }

    private static PurchaseAuditResult BuildResult(
        IReadOnlyList<PurchaseAuditDocument> efatura,
        IReadOnlyList<PurchaseAuditDocument> phc,
        DateOnly? searchStart = null,
        DateOnly? searchEnd = null,
        PurchaseAuditOptions? options = null,
        IReadOnlyDictionary<string, string>? manualMatches = null)
    {
        options ??= PurchaseAuditOptions.Default;
        var acceptedManualMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var efaturaKeys = efatura.Select(document => document.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var phcKeys = phc.Select(document => document.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validManualMatches = (manualMatches ?? new Dictionary<string, string>())
            .Where(pair => efaturaKeys.Contains(pair.Key) && phcKeys.Contains(pair.Value))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        var reservedManualPhcKeys = validManualMatches.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = new RecordTable("purchase-audit-efatura", "Auditoria e-Fatura");
        var right = new RecordTable("purchase-audit-phc", "Auditoria PHC");
        var availablePhc = new HashSet<int>(Enumerable.Range(0, phc.Count));
        var pairs = new List<AuditPair>();

        foreach (var source in efatura.OrderBy(document => document.InvoiceDate).ThenBy(document => document.InvoiceNumber, StringComparer.CurrentCultureIgnoreCase))
        {
            if (validManualMatches.TryGetValue(source.Key, out var manualPhcKey))
            {
                var manualIndex = availablePhc
                    .Where(index => phc[index].Key.Equals(manualPhcKey, StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(-1)
                    .First();
                if (manualIndex >= 0)
                {
                    availablePhc.Remove(manualIndex);
                    acceptedManualMatches[source.Key] = phc[manualIndex].Key;
                    pairs.Add(new AuditPair(source, phc[manualIndex], MatchKind.Manual));
                    continue;
                }
            }

            var candidates = availablePhc.Where(index =>
                    !reservedManualPhcKeys.Contains(phc[index].Key) &&
                    SameSupplier(source, phc[index]) &&
                    DocumentNumberMatch(source.InvoiceNumber, phc[index].InvoiceNumber, options.MinimumDocumentSuffixLength) != DocumentNumberMatchKind.None &&
                    DateDistance(source, phc[index]) <= options.DateToleranceDays &&
                    AmountDifference(source.GrossTotal, phc[index].GrossTotal) <= options.TotalTolerance &&
                    AmountDifference(source.VatTotal, phc[index].VatTotal) <= options.VatTolerance)
                .ToList();
            if (candidates.Count == 0)
            {
                pairs.Add(new AuditPair(source, null, MatchKind.None));
                continue;
            }

            var selected = candidates
                .OrderBy(index => DocumentNumberMatch(source.InvoiceNumber, phc[index].InvoiceNumber, options.MinimumDocumentSuffixLength))
                .ThenBy(index => DateDistance(source, phc[index]))
                .ThenBy(index => AmountDifference(source.GrossTotal, phc[index].GrossTotal))
                .ThenBy(index => AmountDifference(source.VatTotal, phc[index].VatTotal))
                .First();
            availablePhc.Remove(selected);
            pairs.Add(new AuditPair(source, phc[selected], candidates.Count > 1
                ? MatchKind.Ambiguous
                : DocumentNumberMatch(source.InvoiceNumber, phc[selected].InvoiceNumber, options.MinimumDocumentSuffixLength) == DocumentNumberMatchKind.Exact
                    ? MatchKind.DocumentNumber
                    : MatchKind.ApproximateDocumentNumber));
        }

        pairs.AddRange(availablePhc
            .Select(index => phc[index])
            .OrderBy(document => document.PostingDate)
            .ThenBy(document => document.InvoiceNumber, StringComparer.CurrentCultureIgnoreCase)
            .Select(document => new AuditPair(null, document, MatchKind.None)));

        var conforming = 0;
        var different = 0;
        var partial = 0;
        var missingInPhc = 0;
        var missingInEfatura = 0;
        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            var comparison = ComparePair(pair, options);
            switch (comparison.Category)
            {
                case AuditCategory.Conforming: conforming++; break;
                case AuditCategory.Different: different++; break;
                case AuditCategory.Partial: partial++; break;
                case AuditCategory.MissingInPhc: missingInPhc++; break;
                case AuditCategory.MissingInEfatura: missingInEfatura++; break;
            }

            var auditKey = (index + 1).ToString(CultureInfo.InvariantCulture);
            left.Add(BuildEfaturaRow(auditKey, pair.Efatura, pair.Phc, comparison));
            right.Add(BuildPhcRow(auditKey, pair.Efatura, pair.Phc, comparison));
        }

        return new PurchaseAuditResult(
            left,
            right,
            searchStart,
            searchEnd,
            conforming,
            different,
            partial,
            missingInPhc,
            missingInEfatura,
            efatura,
            phc,
            options,
            acceptedManualMatches);
    }

    private static AuditComparison ComparePair(AuditPair pair, PurchaseAuditOptions options)
    {
        if (pair.Efatura is null)
        {
            return new AuditComparison(AuditCategory.MissingInEfatura, "Apenas no PHC", "Documento não encontrado nos dados e-Fatura carregados.");
        }

        if (pair.Phc is null)
        {
            return new AuditComparison(AuditCategory.MissingInPhc, "Sem correspondência no PHC", "Documento não encontrado no intervalo PHC pesquisado.");
        }

        var differences = new List<string>();
        AddDifference(differences, "total", pair.Efatura.GrossTotal, pair.Phc.GrossTotal, options.TotalTolerance);
        AddDifference(differences, "líquido", pair.Efatura.NetTotal, pair.Phc.NetTotal, options.TotalTolerance);
        AddDifference(differences, "IVA", pair.Efatura.VatTotal, pair.Phc.VatTotal, options.VatTolerance);
        if (pair.Efatura.TaxTotals.Count > 0)
        {
            foreach (var rate in pair.Efatura.TaxTotals.Select(tax => tax.Rate)
                         .Union(pair.Phc.TaxTotals.Select(tax => tax.Rate)).Distinct().OrderBy(rate => rate))
            {
                var efaturaTax = pair.Efatura.TaxTotals.FirstOrDefault(tax => tax.Rate == rate) ?? new PurchaseAuditTaxTotal(rate, 0m, 0m);
                var phcTax = pair.Phc.TaxTotals.FirstOrDefault(tax => tax.Rate == rate) ?? new PurchaseAuditTaxTotal(rate, 0m, 0m);
                AddDifference(differences, $"base a {FormatRate(rate)}", efaturaTax.TaxBase, phcTax.TaxBase, options.TotalTolerance);
                AddDifference(differences, $"IVA a {FormatRate(rate)}", efaturaTax.TaxAmount, phcTax.TaxAmount, options.VatTolerance);
            }
        }

        if (differences.Count > 0)
        {
            var manual = pair.MatchKind == MatchKind.Manual ? " (associação manual)" : string.Empty;
            return new AuditComparison(AuditCategory.Different, "Diferenças" + manual, string.Join("; ", differences));
        }

        if (pair.MatchKind == MatchKind.Ambiguous)
        {
            return new AuditComparison(AuditCategory.Partial, "Confirmar correspondência", "Existe mais do que um documento PHC candidato.");
        }

        if (pair.Efatura.TaxTotals.Count == 0)
        {
            return new AuditComparison(AuditCategory.Partial, "Total conforme; taxas indisponíveis", "O CSV e-Fatura não discrimina as taxas de IVA.");
        }

        var matchNote = pair.MatchKind switch
        {
            MatchKind.Manual => "Correspondência definida manualmente pelo utilizador.",
            MatchKind.ApproximateDocumentNumber => "Correspondência por NIF, sufixo do documento, data, total e IVA.",
            _ => "Totais e taxas de IVA conformes."
        };
        var status = pair.MatchKind == MatchKind.Manual ? "Conforme (associação manual)" : "Conforme";
        return new AuditComparison(AuditCategory.Conforming, status, matchNote);
    }

    private static SaftRecord BuildEfaturaRow(
        string auditKey,
        PurchaseAuditDocument? efatura,
        PurchaseAuditDocument? phc,
        AuditComparison comparison) => new(efatura?.SourceFile ?? "Auditoria", "Auditoria de compra")
        {
            ["Chave de auditoria"] = auditKey,
            ["Chave e-Fatura"] = efatura?.Key ?? string.Empty,
            ["Chave PHC"] = phc?.Key ?? string.Empty,
            ["Estado da auditoria"] = comparison.Status,
            ["NIF fornecedor"] = efatura?.SupplierTaxId ?? phc?.SupplierTaxId ?? string.Empty,
            ["Fornecedor e-Fatura"] = efatura?.SupplierName ?? string.Empty,
            ["Documento e-Fatura"] = efatura?.InvoiceNumber ?? string.Empty,
            ["Data e-Fatura"] = FormatDate(efatura?.InvoiceDate),
            ["Total líquido e-Fatura"] = FormatAmount(efatura?.NetTotal),
            ["IVA e-Fatura"] = FormatAmount(efatura?.VatTotal),
            ["Total e-Fatura"] = FormatAmount(efatura?.GrossTotal),
            ["Taxas e-Fatura"] = FormatTaxes(efatura?.TaxTotals),
            ["Documento PHC correspondente"] = phc?.InvoiceNumber ?? string.Empty,
            ["Diferenças / observações"] = comparison.Details
        };

    private static SaftRecord BuildPhcRow(
        string auditKey,
        PurchaseAuditDocument? efatura,
        PurchaseAuditDocument? phc,
        AuditComparison comparison) => new("PHC", "Auditoria de compra")
        {
            ["Chave de auditoria"] = auditKey,
            ["Chave e-Fatura"] = efatura?.Key ?? string.Empty,
            ["Chave PHC"] = phc?.Key ?? string.Empty,
            ["Estado da auditoria"] = comparison.Status,
            ["NIF fornecedor"] = phc?.SupplierTaxId ?? efatura?.SupplierTaxId ?? string.Empty,
            ["Fornecedor PHC"] = phc?.SupplierName ?? string.Empty,
            ["Documento PHC"] = phc?.InvoiceNumber ?? string.Empty,
            ["Tipo de documento PHC"] = phc?.DocumentType ?? string.Empty,
            ["Data original PHC"] = FormatDate(phc?.InvoiceDate),
            ["Data de lançamento PHC"] = FormatDate(phc?.PostingDate),
            ["Diferença de datas"] = FormatDateDifference(efatura?.InvoiceDate, phc?.PostingDate),
            ["Total líquido PHC"] = FormatAmount(phc?.NetTotal),
            ["IVA PHC"] = FormatAmount(phc?.VatTotal),
            ["Total PHC"] = FormatAmount(phc?.GrossTotal),
            ["Taxas PHC"] = FormatTaxes(phc?.TaxTotals),
            ["Documento e-Fatura correspondente"] = efatura?.InvoiceNumber ?? string.Empty,
            ["Diferenças / observações"] = comparison.Details
        };

    private static void AddDifference(
        List<string> differences,
        string label,
        decimal expected,
        decimal actual,
        decimal tolerance)
    {
        if (AmountDifference(expected, actual) > tolerance)
        {
            differences.Add($"{label}: e-Fatura {FormatAmount(expected)}, PHC {FormatAmount(actual)}");
        }
    }

    private static bool SameSupplier(PurchaseAuditDocument left, PurchaseAuditDocument right) =>
        left.SupplierTaxId.Length == 0 || right.SupplierTaxId.Length == 0 ||
        DigitsOnly(left.SupplierTaxId).Equals(DigitsOnly(right.SupplierTaxId), StringComparison.Ordinal);

    private static int DateDistance(PurchaseAuditDocument left, PurchaseAuditDocument right)
    {
        if (!left.InvoiceDate.HasValue)
        {
            return int.MaxValue / 2;
        }

        var candidates = new[] { right.InvoiceDate, right.PostingDate }
            .Where(date => date.HasValue)
            .Select(date => Math.Abs(date!.Value.DayNumber - left.InvoiceDate.Value.DayNumber))
            .ToArray();
        return candidates.Length == 0 ? int.MaxValue / 2 : candidates.Min();
    }

    private static decimal AmountDifference(decimal left, decimal right) =>
        Math.Abs(Math.Abs(left) - Math.Abs(right));

    private static DocumentNumberMatchKind DocumentNumberMatch(string left, string right, int minimumSuffixLength)
    {
        var normalizedLeft = NormalizeDocumentNumber(left);
        var normalizedRight = NormalizeDocumentNumber(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return DocumentNumberMatchKind.None;
        }

        if (normalizedLeft.Equals(normalizedRight, StringComparison.Ordinal))
        {
            return DocumentNumberMatchKind.Exact;
        }

        var shorterLength = Math.Min(normalizedLeft.Length, normalizedRight.Length);
        if (shorterLength >= minimumSuffixLength &&
            (normalizedLeft.EndsWith(normalizedRight, StringComparison.Ordinal) ||
             normalizedRight.EndsWith(normalizedLeft, StringComparison.Ordinal)))
        {
            return DocumentNumberMatchKind.Suffix;
        }

        var commonSuffix = 0;
        while (commonSuffix < shorterLength &&
               normalizedLeft[^(commonSuffix + 1)] == normalizedRight[^(commonSuffix + 1)])
        {
            commonSuffix++;
        }

        return commonSuffix >= minimumSuffixLength
            ? DocumentNumberMatchKind.Suffix
            : DocumentNumberMatchKind.None;
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static string DocumentIdentity(SaftRecord record)
    {
        var key = record["DocumentKey"].Trim();
        return key.Length > 0 ? key : string.Join("|", record["TaxRegistrationNumber"], record["InvoiceNo"], record["InvoiceDate"], record["InvoiceType"]);
    }

    private static string LineIdentity(SaftRecord record)
    {
        var key = record["DocumentKey"].Trim();
        return key.Length > 0 ? key : string.Join("|", record["TaxRegistrationNumber"], record["InvoiceNo"], record["InvoiceDate"]);
    }

    private static string First(SaftRecord record, params string[] fields) =>
        fields.Select(field => record[field]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyList<PurchaseAuditTaxTotal> AggregateTaxes(IEnumerable<PurchaseAuditTaxTotal> taxes) =>
        taxes.GroupBy(tax => tax.Rate)
            .Select(group => new PurchaseAuditTaxTotal(group.Key, group.Sum(tax => tax.TaxBase), group.Sum(tax => tax.TaxAmount)))
            .OrderBy(tax => tax.Rate)
            .ToList();

    private static bool TryReadRate(string value, out decimal rate)
    {
        var cleaned = value.Trim().Replace("%", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out rate) && rate is >= 0m and <= 100m;
    }

    private static decimal ReadAmount(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, "[^0-9,.-]", string.Empty);
        if (cleaned.Length == 0)
        {
            return 0m;
        }

        var comma = cleaned.LastIndexOf(',');
        var dot = cleaned.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            cleaned = comma > dot
                ? cleaned.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : cleaned.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (comma >= 0)
        {
            cleaned = cleaned.Replace(',', '.');
        }

        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    private static DateOnly? ReadDate(string value)
    {
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy" };
        if (DateOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateOnly.TryParse(value, Portuguese, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static string NormalizeDocumentNumber(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string FormatTaxes(IReadOnlyList<PurchaseAuditTaxTotal>? taxes) =>
        taxes is null || taxes.Count == 0
            ? "Sem discriminação por taxa"
            : string.Join("; ", taxes.Select(tax =>
                $"{FormatRate(tax.Rate)}: base {FormatAmount(tax.TaxBase)}, IVA {FormatAmount(tax.TaxAmount)}"));

    private static string FormatRate(decimal rate) => $"{rate.ToString("0.##", Portuguese)} %";
    private static string FormatAmount(decimal? amount) => amount.HasValue ? $"{amount.Value.ToString("N2", Portuguese)} €" : string.Empty;
    private static string FormatDate(DateOnly? date) => date?.ToString("dd/MM/yyyy", Portuguese) ?? string.Empty;
    private static string FormatDateDifference(DateOnly? efaturaDate, DateOnly? phcDate)
    {
        if (!efaturaDate.HasValue || !phcDate.HasValue)
        {
            return string.Empty;
        }

        var days = phcDate.Value.DayNumber - efaturaDate.Value.DayNumber;
        return days == 0 ? "Mesma data" : $"{days:+#;-#;0} dia(s)";
    }

    private static string ReadString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static decimal ReadDecimal(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static DateOnly? ReadDate(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : ReadDate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private sealed class MutablePhcDocument(
        string stamp,
        string invoiceNumber,
        string documentType,
        DateOnly? invoiceDate,
        DateOnly? postingDate,
        string supplierTaxId,
        string supplierName,
        decimal netTotal,
        decimal vatTotal,
        decimal grossTotal)
    {
        public string Stamp { get; } = stamp;
        public string InvoiceNumber { get; } = invoiceNumber;
        public string DocumentType { get; } = documentType;
        public DateOnly? InvoiceDate { get; } = invoiceDate;
        public DateOnly? PostingDate { get; } = postingDate;
        public string SupplierTaxId { get; } = supplierTaxId;
        public string SupplierName { get; } = supplierName;
        public decimal NetTotal { get; } = netTotal;
        public decimal VatTotal { get; } = vatTotal;
        public decimal GrossTotal { get; } = grossTotal;
        public List<PurchaseAuditTaxTotal> Taxes { get; } = [];
    }

    private sealed record AuditPair(PurchaseAuditDocument? Efatura, PurchaseAuditDocument? Phc, MatchKind MatchKind);
    private sealed record AuditComparison(AuditCategory Category, string Status, string Details);
    private enum MatchKind { None, DocumentNumber, ApproximateDocumentNumber, Manual, Ambiguous }
    private enum DocumentNumberMatchKind { Exact = 0, Suffix = 1, None = 2 }
    private enum AuditCategory { Conforming, Different, Partial, MissingInPhc, MissingInEfatura }
}
