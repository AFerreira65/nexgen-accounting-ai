using AccountingAI.Core.Models;

namespace AccountingAI.Core.Phc;

public sealed record PurchaseAuditTaxTotal(decimal Rate, decimal TaxBase, decimal TaxAmount);

public sealed record PurchaseAuditOptions(
    decimal TotalTolerance,
    decimal VatTolerance,
    int DateToleranceDays,
    int MinimumDocumentSuffixLength)
{
    public static PurchaseAuditOptions Default { get; } = new(0.50m, 0.50m, 45, 6);

    public static PurchaseAuditOptions FromCompany(AccountingAI.Core.Configuration.PhcCompanyConfiguration company) => new(
        Math.Max(0m, company.PurchaseAuditTotalTolerance),
        Math.Max(0m, company.PurchaseAuditVatTolerance),
        Math.Max(0, company.PurchaseAuditDateToleranceDays),
        Math.Clamp(company.PurchaseAuditDocumentSuffixLength, 3, 20));
}

public sealed record PurchaseAuditDocument(
    string Key,
    string SourceFile,
    string SupplierTaxId,
    string SupplierName,
    string InvoiceNumber,
    string DocumentType,
    DateOnly? InvoiceDate,
    DateOnly? PostingDate,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    IReadOnlyList<PurchaseAuditTaxTotal> TaxTotals,
    string PhcStamp = "");

public sealed record PurchaseAuditResult(
    RecordTable EfaturaRows,
    RecordTable PhcRows,
    DateOnly? PhcSearchStart,
    DateOnly? PhcSearchEnd,
    int Conforming,
    int Different,
    int Partial,
    int MissingInPhc,
    int MissingInEfatura,
    IReadOnlyList<PurchaseAuditDocument> EfaturaDocuments,
    IReadOnlyList<PurchaseAuditDocument> PhcDocuments,
    PurchaseAuditOptions Options,
    IReadOnlyDictionary<string, string> ManualMatches)
{
    public int PairCount => EfaturaRows.Rows.Count;

    public string Summary =>
        $"{Conforming:N0} conforme(s) | {Different:N0} com diferença(s) | " +
        $"{Partial:N0} validação(ões) parcial(is) | {MissingInPhc:N0} sem PHC | " +
        $"{MissingInEfatura:N0} apenas no PHC";
}
