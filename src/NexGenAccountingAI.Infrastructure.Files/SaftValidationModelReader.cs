using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Constrói o <see cref="SaftValidationModel"/> numa passagem única em
/// streaming (<see cref="XmlReader"/>, DTD proibido, sem resolução externa,
/// Windows-1252 suportado): campos do cabeçalho, o conjunto de `AccountID` do
/// plano, e as contagens/totais do bloco de movimentos, com as transações
/// desequilibradas, com contas por resolver ou fora do período em listas
/// limitadas. Não guarda as transações nem as linhas.
/// </summary>
public sealed class SaftValidationModelReader : ISaftValidationModelReader
{
    private const int FlaggedLimit = 50;

    static SaftValidationModelReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SaftValidationModel> ReadAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = new XmlReaderSettings
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
            XmlResolver = null
        };

        string? version = null, taxNumber = null, basis = null, productId = null, productCompanyTaxId = null, softwareCertificate = null;
        int? fiscalYear = null;
        DateOnly? startDate = null, endDate = null;
        var accountIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var journalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int? declaredEntryCount = null;
        decimal? totalDebit = null, totalCredit = null;
        var transactionCount = 0;
        decimal lineDebitTotal = 0m, lineCreditTotal = 0m;
        var unbalanced = new List<SaftFlaggedTransaction>();
        var unresolved = new List<SaftUnresolvedAccountRef>();
        var outOfPeriod = new List<SaftFlaggedTransaction>();
        var truncated = false;

        var inHeader = false;
        var inAccounts = false;
        var inEntries = false;
        var inTransaction = false;
        var sawEntries = false;

        void Flag<T>(List<T> list, T item)
        {
            if (list.Count >= FlaggedLimit)
            {
                truncated = true;
                return;
            }

            list.Add(item);
        }

        try
        {
            using var reader = XmlReader.Create(content, settings);
            var moved = await reader.ReadAsync().ConfigureAwait(false);
            while (moved)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    switch (reader.LocalName)
                    {
                        case "Header":
                            inHeader = false;
                            break;
                        case "GeneralLedgerAccounts":
                            inAccounts = false;
                            break;
                        case "GeneralLedgerEntries":
                            inEntries = false;
                            moved = false; // nada depois interessa para a validação analítica
                            continue;
                    }

                    moved = await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    moved = await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                var consumed = false;
                switch (reader.LocalName)
                {
                    case "Header":
                        inHeader = true;
                        break;
                    case "GeneralLedgerAccounts":
                        inAccounts = true;
                        break;
                    case "GeneralLedgerEntries":
                        inEntries = true;
                        sawEntries = true;
                        break;

                    case "AuditFileVersion" when inHeader:
                        version = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TaxRegistrationNumber" when inHeader && taxNumber is null:
                        taxNumber = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TaxAccountingBasis" when inHeader:
                        basis = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "FiscalYear" when inHeader:
                        fiscalYear = ParseInt(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "StartDate" when inHeader:
                        startDate = ParseDate(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "EndDate" when inHeader:
                        endDate = ParseDate(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "ProductID" when inHeader:
                        productId = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "ProductCompanyTaxID" when inHeader:
                        productCompanyTaxId = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "SoftwareCertificateNumber" when inHeader:
                        softwareCertificate = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;

                    case "AccountID" when inAccounts:
                        var accountId = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        if (accountId is not null)
                        {
                            accountIds.Add(accountId);
                        }

                        consumed = true;
                        break;

                    case "NumberOfEntries" when inEntries && !inTransaction:
                        declaredEntryCount = ParseInt(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TotalDebit" when inEntries && !inTransaction:
                        totalDebit = ParseDecimal(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TotalCredit" when inEntries && !inTransaction:
                        totalCredit = ParseDecimal(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "JournalID" when inEntries && !inTransaction:
                        var journalId = Trimmed(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        if (journalId is not null)
                        {
                            journalIds.Add(journalId);
                        }

                        consumed = true;
                        break;

                    case "Transaction" when inEntries:
                        inTransaction = true;
                        transactionCount++;
                        XElement element;
                        using (var subtree = reader.ReadSubtree())
                        {
                            element = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                        }

                        inTransaction = false;
                        var (id, date, debit, credit, refs) = Summarize(element);
                        lineDebitTotal += debit;
                        lineCreditTotal += credit;

                        if (Math.Round(debit - credit, 2) != 0m)
                        {
                            Flag(unbalanced, new SaftFlaggedTransaction(id, date, debit, credit));
                        }

                        if (date is { } value && startDate is { } from && endDate is { } to && (value < from || value > to))
                        {
                            Flag(outOfPeriod, new SaftFlaggedTransaction(id, date, debit, credit));
                        }

                        foreach (var accountRef in refs)
                        {
                            if (!accountIds.Contains(accountRef))
                            {
                                Flag(unresolved, new SaftUnresolvedAccountRef(id, accountRef));
                            }
                        }

                        // O leitor exterior ficou no fim de <Transaction>.
                        break;
                }

                moved = consumed
                    ? reader.ReadState == ReadState.Interactive
                    : await reader.ReadAsync().ConfigureAwait(false);
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }

        return new SaftValidationModel
        {
            AuditFileVersion = version,
            TaxRegistrationNumber = taxNumber,
            TaxAccountingBasis = basis,
            FiscalYear = fiscalYear,
            StartDate = startDate,
            EndDate = endDate,
            ProductId = productId,
            ProductCompanyTaxId = productCompanyTaxId,
            SoftwareCertificateNumber = softwareCertificate,
            AccountIds = accountIds,
            JournalCount = journalIds.Count,
            HasGeneralLedgerEntries = sawEntries,
            DeclaredNumberOfEntries = declaredEntryCount,
            DeclaredTotalDebit = totalDebit,
            DeclaredTotalCredit = totalCredit,
            TransactionCount = transactionCount,
            LineDebitTotal = Math.Round(lineDebitTotal, 2),
            LineCreditTotal = Math.Round(lineCreditTotal, 2),
            UnbalancedTransactions = unbalanced,
            UnresolvedAccountRefs = unresolved,
            OutOfPeriodTransactions = outOfPeriod,
            FlaggedListsTruncated = truncated
        };
    }

    private static (string Id, DateOnly? Date, decimal Debit, decimal Credit, IReadOnlyList<string> AccountRefs) Summarize(XElement transaction)
    {
        var id = (ChildValue(transaction, "TransactionID")
            ?? transaction.Attribute("TransactionID")?.Value
            ?? string.Empty).Trim();
        var date = ParseDate(ChildValue(transaction, "TransactionDate") ?? string.Empty);

        decimal debit = 0m, credit = 0m;
        var refs = new List<string>();
        var lines = transaction.Elements().FirstOrDefault(element => element.Name.LocalName == "Lines");
        if (lines is not null)
        {
            foreach (var line in lines.Elements())
            {
                var isDebit = line.Name.LocalName.Equals("DebitLine", StringComparison.OrdinalIgnoreCase);
                var isCredit = line.Name.LocalName.Equals("CreditLine", StringComparison.OrdinalIgnoreCase);
                if (!isDebit && !isCredit)
                {
                    continue;
                }

                var accountRef = (ChildValue(line, "AccountID") ?? string.Empty).Trim();
                if (accountRef.Length > 0)
                {
                    refs.Add(accountRef);
                }

                var amount = ParseAmount(line.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == (isDebit ? "DebitAmount" : "CreditAmount")));
                if (isDebit)
                {
                    debit += amount;
                }
                else
                {
                    credit += amount;
                }
            }
        }

        return (id, date, Math.Round(debit, 2), Math.Round(credit, 2), refs);
    }

    private static string? ChildValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;

    private static decimal ParseAmount(XElement? amountElement)
    {
        if (amountElement is null)
        {
            return 0m;
        }

        var inner = amountElement.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Amount", StringComparison.OrdinalIgnoreCase));
        return ParseDecimal((inner?.Value ?? amountElement.Value).Trim()) ?? 0m;
    }

    private static string? Trimmed(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;
}
