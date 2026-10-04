namespace NexGenAccountingAI.Application.Saft;

/// <summary>A base e o imposto de um documento de venda a uma taxa de IVA.</summary>
public sealed record SaftSalesDocumentTax(decimal Percentage, decimal TaxBase, decimal TaxAmount);

/// <summary>
/// Um documento do bloco <c>SourceDocuments/SalesInvoices</c> de um SAF-T (PT) de
/// faturação, com o cliente resolvido pelos <c>MasterFiles</c> e a base e o
/// imposto repartidos por taxa. Os valores são sempre positivos; numa nota de
/// crédito o tipo diz o sentido.
/// </summary>
public sealed record SaftSalesDocument(
    string InvoiceNo,
    string? Atcud,
    string InvoiceType,
    DateOnly InvoiceDate,
    bool IsVoided,
    string CustomerId,
    string CustomerTaxId,
    string CustomerName,
    string? CustomerCountry,
    decimal NetTotal,
    decimal TaxPayable,
    decimal GrossTotal,
    IReadOnlyList<SaftSalesDocumentTax> Taxes);

public sealed record SaftSalesDocumentsResult(
    string? CompanyTaxId,
    IReadOnlyList<SaftSalesDocument> Documents,
    IReadOnlyList<string> Warnings);

/// <summary>Lê os documentos de venda de um SAF-T (PT) de faturação, um de cada vez.</summary>
public interface ISaftSalesDocumentsReader
{
    Task<SaftSalesDocumentsResult> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}
