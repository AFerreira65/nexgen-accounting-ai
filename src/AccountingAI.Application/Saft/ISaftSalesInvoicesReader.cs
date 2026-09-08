namespace AccountingAI.Application.Saft;

/// <summary>
/// Lê um resumo do bloco <c>SourceDocuments/SalesInvoices</c> de um SAF-T (PT)
/// de faturação, em streaming e sem materializar as linhas.
/// </summary>
public interface ISaftSalesInvoicesReader
{
    Task<SaftSalesInvoicesSummary> ReadSummaryAsync(Stream content, CancellationToken cancellationToken = default);
}
