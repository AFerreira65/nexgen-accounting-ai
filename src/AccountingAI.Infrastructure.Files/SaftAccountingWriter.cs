using System.Globalization;
using System.Text;
using System.Xml;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files;

/// <summary>
/// Escreve um SAF-T (PT) de contabilidade (<c>TaxAccountingBasis = C</c>) na
/// versão 1.04_01 da Portaria n.º 302/2016, em Windows-1252 como o formato
/// pede. Escreve em streaming, pela ordem do esquema: cabeçalho, plano de
/// contas, clientes, fornecedores e movimentos por diário. As moradas que a
/// aplicação não conhece saem como «Desconhecido», como o formato admite.
/// </summary>
public sealed class SaftAccountingWriter : ISaftAccountingWriter
{
    public const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private const string Unknown = "Desconhecido";

    static SaftAccountingWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task WriteAsync(SaftExportModel model, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(output);

        var settings = new XmlWriterSettings
        {
            Async = true,
            CloseOutput = false,
            Encoding = Encoding.GetEncoding(1252),
            Indent = true,
        };

        await using var writer = XmlWriter.Create(output, settings);
        await writer.WriteStartDocumentAsync().ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "AuditFile", Namespace).ConfigureAwait(false);

        await WriteHeaderAsync(writer, model.Header).ConfigureAwait(false);

        await writer.WriteStartElementAsync(null, "MasterFiles", Namespace).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "GeneralLedgerAccounts", Namespace).ConfigureAwait(false);
        await ElementAsync(writer, "TaxonomyReference", model.TaxonomyReference).ConfigureAwait(false);
        foreach (var account in model.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteStartElementAsync(null, "Account", Namespace).ConfigureAwait(false);
            await ElementAsync(writer, "AccountID", account.AccountId).ConfigureAwait(false);
            await ElementAsync(writer, "AccountDescription", Text(account.Description, 100)).ConfigureAwait(false);
            await ElementAsync(writer, "OpeningDebitBalance", Amount(account.OpeningDebit)).ConfigureAwait(false);
            await ElementAsync(writer, "OpeningCreditBalance", Amount(account.OpeningCredit)).ConfigureAwait(false);
            await ElementAsync(writer, "ClosingDebitBalance", Amount(account.ClosingDebit)).ConfigureAwait(false);
            await ElementAsync(writer, "ClosingCreditBalance", Amount(account.ClosingCredit)).ConfigureAwait(false);
            await ElementAsync(writer, "GroupingCategory", account.GroupingCategory).ConfigureAwait(false);
            if (account.GroupingCode is not null)
            {
                await ElementAsync(writer, "GroupingCode", account.GroupingCode).ConfigureAwait(false);
            }

            if (account.TaxonomyCode is { } taxonomyCode)
            {
                await ElementAsync(writer, "TaxonomyCode", taxonomyCode.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);

        // O esquema pede todos os clientes antes dos fornecedores.
        foreach (var party in model.Parties.Where(item => item.IsCustomer).Concat(model.Parties.Where(item => !item.IsCustomer)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = party.IsCustomer ? "Customer" : "Supplier";
            await writer.WriteStartElementAsync(null, prefix, Namespace).ConfigureAwait(false);
            await ElementAsync(writer, prefix + "ID", Text(party.PartyId, 30)).ConfigureAwait(false);
            await ElementAsync(writer, "AccountID", party.AccountId).ConfigureAwait(false);
            await ElementAsync(writer, prefix + "TaxID", Text(party.TaxId, 30)).ConfigureAwait(false);
            await ElementAsync(writer, "CompanyName", Text(party.Name, 100)).ConfigureAwait(false);
            await writer.WriteStartElementAsync(null, "BillingAddress", Namespace).ConfigureAwait(false);
            await ElementAsync(writer, "AddressDetail", Text(party.AddressDetail, 210)).ConfigureAwait(false);
            await ElementAsync(writer, "City", Text(party.City, 50)).ConfigureAwait(false);
            await ElementAsync(writer, "PostalCode", Text(party.PostalCode, 20)).ConfigureAwait(false);
            await ElementAsync(writer, "Country", party.Country).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await ElementAsync(writer, "SelfBillingIndicator", "0").ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);

        await writer.WriteStartElementAsync(null, "GeneralLedgerEntries", Namespace).ConfigureAwait(false);
        await ElementAsync(writer, "NumberOfEntries", model.NumberOfEntries.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await ElementAsync(writer, "TotalDebit", Amount(model.TotalDebit)).ConfigureAwait(false);
        await ElementAsync(writer, "TotalCredit", Amount(model.TotalCredit)).ConfigureAwait(false);
        foreach (var journal in model.Journals)
        {
            await writer.WriteStartElementAsync(null, "Journal", Namespace).ConfigureAwait(false);
            await ElementAsync(writer, "JournalID", Text(journal.JournalId, 30)).ConfigureAwait(false);
            await ElementAsync(writer, "Description", Text(journal.Description, 255)).ConfigureAwait(false);
            foreach (var transaction in journal.Transactions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteTransactionAsync(writer, transaction).ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);

        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await writer.WriteEndDocumentAsync().ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WriteHeaderAsync(XmlWriter writer, SaftExportHeader header)
    {
        await writer.WriteStartElementAsync(null, "Header", Namespace).ConfigureAwait(false);
        await ElementAsync(writer, "AuditFileVersion", "1.04_01").ConfigureAwait(false);
        await ElementAsync(writer, "CompanyID", header.TaxRegistrationNumber).ConfigureAwait(false);
        await ElementAsync(writer, "TaxRegistrationNumber", header.TaxRegistrationNumber).ConfigureAwait(false);
        await ElementAsync(writer, "TaxAccountingBasis", "C").ConfigureAwait(false);
        await ElementAsync(writer, "CompanyName", Text(header.CompanyName, 100)).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "CompanyAddress", Namespace).ConfigureAwait(false);
        await ElementAsync(writer, "AddressDetail", Text(header.AddressDetail, 210)).ConfigureAwait(false);
        await ElementAsync(writer, "City", Text(header.City, 50)).ConfigureAwait(false);
        await ElementAsync(writer, "PostalCode", Text(header.PostalCode, 20)).ConfigureAwait(false);
        await ElementAsync(writer, "Country", header.Country).ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await ElementAsync(writer, "FiscalYear", header.FiscalYear.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await ElementAsync(writer, "StartDate", Date(header.StartDate)).ConfigureAwait(false);
        await ElementAsync(writer, "EndDate", Date(header.EndDate)).ConfigureAwait(false);
        await ElementAsync(writer, "CurrencyCode", "EUR").ConfigureAwait(false);
        await ElementAsync(writer, "DateCreated", Date(header.DateCreated)).ConfigureAwait(false);
        await ElementAsync(writer, "TaxEntity", "Sede").ConfigureAwait(false);
        await ElementAsync(writer, "ProductCompanyTaxID", SaftProducer.CompanyTaxId).ConfigureAwait(false);
        await ElementAsync(writer, "SoftwareCertificateNumber", SaftProducer.SoftwareCertificateNumber.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await ElementAsync(writer, "ProductID", SaftProducer.ProductId).ConfigureAwait(false);
        await ElementAsync(writer, "ProductVersion", Text(header.ProductVersion, 30)).ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteTransactionAsync(XmlWriter writer, SaftExportTransaction transaction)
    {
        await writer.WriteStartElementAsync(null, "Transaction", Namespace).ConfigureAwait(false);
        await ElementAsync(writer, "TransactionID", transaction.TransactionId).ConfigureAwait(false);
        await ElementAsync(writer, "Period", transaction.Period.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await ElementAsync(writer, "TransactionDate", Date(transaction.TransactionDate)).ConfigureAwait(false);
        await ElementAsync(writer, "SourceID", Text(transaction.SourceId, 30)).ConfigureAwait(false);
        await ElementAsync(writer, "Description", Text(transaction.Description, 255)).ConfigureAwait(false);
        await ElementAsync(writer, "DocArchivalNumber", transaction.DocArchivalNumber).ConfigureAwait(false);
        await ElementAsync(writer, "TransactionType", transaction.TransactionType).ConfigureAwait(false);
        await ElementAsync(writer, "GLPostingDate", Date(transaction.GlPostingDate)).ConfigureAwait(false);

        // O esquema admite um cliente ou um fornecedor, não os dois.
        if (transaction.CustomerId is not null)
        {
            await ElementAsync(writer, "CustomerID", Text(transaction.CustomerId, 30)).ConfigureAwait(false);
        }
        else if (transaction.SupplierId is not null)
        {
            await ElementAsync(writer, "SupplierID", Text(transaction.SupplierId, 30)).ConfigureAwait(false);
        }

        await writer.WriteStartElementAsync(null, "Lines", Namespace).ConfigureAwait(false);
        foreach (var line in transaction.Lines)
        {
            var isDebit = line.Debit != 0;
            await writer.WriteStartElementAsync(null, isDebit ? "DebitLine" : "CreditLine", Namespace).ConfigureAwait(false);
            await ElementAsync(writer, "RecordID", Text(line.RecordId, 30)).ConfigureAwait(false);
            await ElementAsync(writer, "AccountID", line.AccountId).ConfigureAwait(false);
            await ElementAsync(writer, "SystemEntryDate", line.SystemEntryDate.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await ElementAsync(writer, "Description", Text(line.Description, 255)).ConfigureAwait(false);
            await ElementAsync(writer, isDebit ? "DebitAmount" : "CreditAmount", Amount(isDebit ? line.Debit : line.Credit)).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static Task ElementAsync(XmlWriter writer, string name, string value) =>
        writer.WriteElementStringAsync(null, name, Namespace, value);

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Texto obrigatório com limite: nunca vazio, cortado ao máximo do esquema.</summary>
    private static string Text(string? value, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
        return text.Length > maxLength ? text[..maxLength] : text;
    }
}
