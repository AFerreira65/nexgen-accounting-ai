using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AccountingAI.Core.Models;

namespace AccountingAI.Core.Parsing;

public sealed class SaftParser
{
    private static readonly IReadOnlyDictionary<string, (string Key, string Title)> SimpleRecords =
        new Dictionary<string, (string Key, string Title)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Account"] = (SaftTableKeys.Accounts, "Plano de contas"),
            ["Customer"] = (SaftTableKeys.Customers, "Clientes"),
            ["Supplier"] = (SaftTableKeys.Suppliers, "Fornecedores"),
            ["Product"] = (SaftTableKeys.Products, "Produtos e serviços"),
            ["TaxTableEntry"] = (SaftTableKeys.Taxes, "Tabela de impostos")
        };

    public SaftDataSet ParseFiles(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var result = new SaftDataSet();

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ParseFile(path, result, cancellationToken);
            }
            catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
            {
                result.ValidationIssues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    Path.GetFileName(path),
                    "Leitura",
                    exception.Message));
            }
        }

        return result;
    }

    private static void ParseFile(string path, SaftDataSet result, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("O ficheiro indicado não existe.", path);
        }

        var sourceFile = file.Name;
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var detectedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var countsBefore = SnapshotCounts(result);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
            MaxCharactersFromEntities = 0
        };

        using var reader = XmlReader.Create(file.FullName, settings);
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            var localName = reader.LocalName;
            if (localName.Equals("Header", StringComparison.OrdinalIgnoreCase))
            {
                var element = ReadCurrentElement(reader);
                var record = XmlRecordFlattener.Flatten(element, sourceFile, "Header");
                result.GetOrCreateTable(SaftTableKeys.Header, "Cabeçalho").Add(record);
                foreach (var pair in record.Values.Where(pair => !pair.Key.Equals("Ficheiro", StringComparison.OrdinalIgnoreCase)))
                {
                    header[pair.Key] = pair.Value;
                }

                reader.Skip();
                continue;
            }

            if (SimpleRecords.TryGetValue(localName, out var tableDefinition))
            {
                var element = ReadCurrentElement(reader);
                result.GetOrCreateTable(tableDefinition.Key, tableDefinition.Title)
                    .Add(XmlRecordFlattener.Flatten(element, sourceFile, localName));
                detectedSections.Add(tableDefinition.Key);
                reader.Skip();
                continue;
            }

            switch (localName)
            {
                case "Journal":
                    ReadJournal(reader, sourceFile, result);
                    detectedSections.Add(SaftTableKeys.Journals);
                    reader.Skip();
                    continue;
                case "Invoice":
                    ReadCommercialDocument(
                        reader,
                        sourceFile,
                        result,
                        SaftTableKeys.Invoices,
                        "Documentos de faturação",
                        SaftTableKeys.InvoiceLines,
                        "Linhas de faturação",
                        "InvoiceNo");
                    detectedSections.Add(SaftTableKeys.Invoices);
                    reader.Skip();
                    continue;
                case "StockMovement":
                    ReadCommercialDocument(
                        reader,
                        sourceFile,
                        result,
                        SaftTableKeys.StockMovements,
                        "Documentos de transporte",
                        SaftTableKeys.StockMovementLines,
                        "Linhas de transporte",
                        "DocumentNumber");
                    detectedSections.Add(SaftTableKeys.StockMovements);
                    reader.Skip();
                    continue;
                case "WorkDocument":
                    ReadCommercialDocument(
                        reader,
                        sourceFile,
                        result,
                        SaftTableKeys.WorkDocuments,
                        "Documentos de conferência",
                        SaftTableKeys.WorkDocumentLines,
                        "Linhas de conferência",
                        "DocumentNumber");
                    detectedSections.Add(SaftTableKeys.WorkDocuments);
                    reader.Skip();
                    continue;
                case "Payment":
                    ReadCommercialDocument(
                        reader,
                        sourceFile,
                        result,
                        SaftTableKeys.Payments,
                        "Recibos",
                        SaftTableKeys.PaymentLines,
                        "Linhas de recibos",
                        "PaymentRefNo");
                    detectedSections.Add(SaftTableKeys.Payments);
                    reader.Skip();
                    continue;
                default:
                    reader.Read();
                    break;
            }
        }

        var basis = Value(header, "TaxAccountingBasis");
        var detectedContent = DescribeDetectedContent(result, countsBefore);
        result.SourceFiles.Add(new SaftSourceFile(
            sourceFile,
            file.FullName,
            file.Length,
            Value(header, "AuditFileVersion"),
            basis,
            Value(header, "CompanyID"),
            Value(header, "CompanyName"),
            Value(header, "StartDate"),
            Value(header, "EndDate"),
            detectedContent));

        ValidateHeader(result, sourceFile, header, basis, detectedSections);
    }

    private static void ReadJournal(XmlReader outerReader, string sourceFile, SaftDataSet result)
    {
        using var reader = outerReader.ReadSubtree();
        reader.MoveToContent();
        var journalId = string.Empty;
        var description = string.Empty;
        var transactionCount = 0;
        decimal totalDebit = 0;
        decimal totalCredit = 0;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Depth == 1 && reader.LocalName.Equals("JournalID", StringComparison.OrdinalIgnoreCase))
            {
                journalId = reader.ReadElementContentAsString();
                continue;
            }

            if (reader.Depth == 1 && reader.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase))
            {
                description = reader.ReadElementContentAsString();
                continue;
            }

            if (!reader.LocalName.Equals("Transaction", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var transaction = ReadCurrentElement(reader);
            transactionCount++;
            var transactionRecord = XmlRecordFlattener.Flatten(transaction, sourceFile, "Transaction", "Lines");
            transactionRecord["JournalID"] = journalId;
            transactionRecord["JournalDescription"] = description;
            result.GetOrCreateTable(SaftTableKeys.Transactions, "Documentos contabilísticos").Add(transactionRecord);

            foreach (var line in transaction.Descendants().Where(element =>
                         element.Name.LocalName is "DebitLine" or "CreditLine"))
            {
                var lineRecord = XmlRecordFlattener.Flatten(line, sourceFile, line.Name.LocalName);
                lineRecord["JournalID"] = journalId;
                lineRecord["TransactionID"] = transactionRecord["TransactionID"];
                lineRecord["Natureza"] = line.Name.LocalName == "DebitLine" ? "Débito" : "Crédito";
                result.GetOrCreateTable(SaftTableKeys.AccountingLines, "Lançamentos contabilísticos").Add(lineRecord);
                totalDebit += ParseDecimal(lineRecord["DebitAmount"]);
                totalCredit += ParseDecimal(lineRecord["CreditAmount"]);
            }

            reader.Skip();
        }

        var journalRecord = new SaftRecord(sourceFile, "Journal")
        {
            ["JournalID"] = journalId,
            ["Description"] = description,
            ["Número de documentos"] = transactionCount.ToString(CultureInfo.InvariantCulture),
            ["Total débito"] = totalDebit.ToString("0.00", CultureInfo.InvariantCulture),
            ["Total crédito"] = totalCredit.ToString("0.00", CultureInfo.InvariantCulture)
        };
        result.GetOrCreateTable(SaftTableKeys.Journals, "Diários").Add(journalRecord);
    }

    private static void ReadCommercialDocument(
        XmlReader reader,
        string sourceFile,
        SaftDataSet result,
        string documentsKey,
        string documentsTitle,
        string linesKey,
        string linesTitle,
        string documentNumberField)
    {
        var element = ReadCurrentElement(reader);
        var documentRecord = XmlRecordFlattener.Flatten(element, sourceFile, element.Name.LocalName, "Line");
        result.GetOrCreateTable(documentsKey, documentsTitle).Add(documentRecord);

        var documentNumber = documentRecord[documentNumberField];
        foreach (var line in element.Elements().Where(child => child.Name.LocalName.Equals("Line", StringComparison.OrdinalIgnoreCase)))
        {
            var lineRecord = XmlRecordFlattener.Flatten(line, sourceFile, "Line");
            lineRecord[documentNumberField] = documentNumber;
            result.GetOrCreateTable(linesKey, linesTitle).Add(lineRecord);
        }
    }

    private static XElement ReadCurrentElement(XmlReader reader)
    {
        using var subtree = reader.ReadSubtree();
        subtree.MoveToContent();
        return XElement.Load(subtree, LoadOptions.None);
    }

    private static void ValidateHeader(
        SaftDataSet result,
        string sourceFile,
        IReadOnlyDictionary<string, string> header,
        string basis,
        IReadOnlySet<string> detectedSections)
    {
        if (header.Count == 0)
        {
            result.ValidationIssues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                sourceFile,
                "Cabeçalho",
                "Não foi encontrado o elemento Header."));
            return;
        }

        var version = Value(header, "AuditFileVersion");
        if (version is not ("1.03_01" or "1.04_01"))
        {
            result.ValidationIssues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                sourceFile,
                "Cabeçalho",
                $"Versão SAF-T não reconhecida pelo monitor: '{version}'. Os dados serão lidos pelos nomes dos elementos."));
        }

        var knownBases = new HashSet<string>(["C", "E", "F", "I", "P", "R", "S", "T"], StringComparer.OrdinalIgnoreCase);
        if (!knownBases.Contains(basis))
        {
            result.ValidationIssues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                sourceFile,
                "Cabeçalho",
                $"TaxAccountingBasis inválido ou ausente: '{basis}'."));
        }

        if ((basis.Equals("C", StringComparison.OrdinalIgnoreCase) || basis.Equals("I", StringComparison.OrdinalIgnoreCase)) &&
            !detectedSections.Contains(SaftTableKeys.Journals))
        {
            result.ValidationIssues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                sourceFile,
                "Contabilidade",
                "O cabeçalho indica contabilidade, mas não foram encontrados diários contabilísticos."));
        }

        result.ValidationIssues.Add(new ValidationIssue(
            ValidationSeverity.Information,
            sourceFile,
            "Leitura",
            "Estrutura XML lida com sucesso. Esta verificação não substitui a validação oficial XSD/SVAT."));
    }

    private static Dictionary<string, int> SnapshotCounts(SaftDataSet result) =>
        typeof(SaftTableKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToDictionary(key => key, result.Count, StringComparer.OrdinalIgnoreCase);

    private static string DescribeDetectedContent(SaftDataSet result, IReadOnlyDictionary<string, int> before)
    {
        var parts = new List<string>();
        AddIfNew(SaftTableKeys.Invoices, "Faturação");
        AddIfNew(SaftTableKeys.StockMovements, "Transporte");
        AddIfNew(SaftTableKeys.WorkDocuments, "Conferência");
        AddIfNew(SaftTableKeys.Payments, "Recibos");
        AddIfNew(SaftTableKeys.Transactions, "Contabilidade");
        return parts.Count == 0 ? "Tabelas mestre" : string.Join(", ", parts);

        void AddIfNew(string key, string description)
        {
            if (result.Count(key) > before.GetValueOrDefault(key))
            {
                parts.Add(description);
            }
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value : string.Empty;

    private static decimal ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
