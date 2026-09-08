namespace AccountingAI.Domain.Efatura;

/// <summary>
/// Uma fatura de compra conhecida pelo Portal e-Fatura (adquirente), importada
/// de um CSV. Guarda-se por empresa para o cruzamento com a contabilidade.
/// </summary>
public sealed class EFaturaPurchase
{
    private EFaturaPurchase()
    {
    }

    private EFaturaPurchase(
        Guid id,
        Guid companyId,
        Guid importBatchId,
        string supplierTaxId,
        string supplierName,
        string invoiceNo,
        string? atcud,
        DateOnly invoiceDate,
        string invoiceType,
        string? status,
        string? sector,
        decimal taxBase,
        decimal taxAmount,
        decimal grossTotal,
        string sourceFileName,
        string documentKey,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        ImportBatchId = importBatchId;
        SupplierTaxId = supplierTaxId;
        SupplierName = supplierName;
        InvoiceNo = invoiceNo;
        Atcud = atcud;
        InvoiceDate = invoiceDate;
        InvoiceType = invoiceType;
        Status = status;
        Sector = sector;
        TaxBase = taxBase;
        TaxAmount = taxAmount;
        GrossTotal = grossTotal;
        SourceFileName = sourceFileName;
        DocumentKey = documentKey;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Agrupa as faturas importadas na mesma chamada.</summary>
    public Guid ImportBatchId { get; private set; }

    public string SupplierTaxId { get; private set; } = string.Empty;

    public string SupplierName { get; private set; } = string.Empty;

    public string InvoiceNo { get; private set; } = string.Empty;

    public string? Atcud { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    public string InvoiceType { get; private set; } = string.Empty;

    public string? Status { get; private set; }

    public string? Sector { get; private set; }

    public decimal TaxBase { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal GrossTotal { get; private set; }

    public string SourceFileName { get; private set; } = string.Empty;

    /// <summary>`NIF|InvoiceNo|InvoiceDate` normalizado — chave de idempotência por empresa.</summary>
    public string DocumentKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static string KeyOf(string supplierTaxId, string invoiceNo, DateOnly invoiceDate) =>
        string.Join('|',
            supplierTaxId.Trim().ToUpperInvariant(),
            invoiceNo.Trim().ToUpperInvariant(),
            invoiceDate.ToString("yyyy-MM-dd"));

    public static EFaturaPurchase Create(
        Guid companyId,
        Guid importBatchId,
        string supplierTaxId,
        string supplierName,
        string invoiceNo,
        string? atcud,
        DateOnly invoiceDate,
        string invoiceType,
        string? status,
        string? sector,
        decimal taxBase,
        decimal taxAmount,
        decimal grossTotal,
        string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierTaxId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNo);

        return new EFaturaPurchase(
            Guid.NewGuid(),
            companyId,
            importBatchId,
            supplierTaxId.Trim(),
            (supplierName ?? string.Empty).Trim(),
            invoiceNo.Trim(),
            string.IsNullOrWhiteSpace(atcud) ? null : atcud.Trim(),
            invoiceDate,
            (invoiceType ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(sector) ? null : sector.Trim(),
            decimal.Round(taxBase, 2),
            decimal.Round(taxAmount, 2),
            decimal.Round(grossTotal, 2),
            (sourceFileName ?? string.Empty).Trim(),
            KeyOf(supplierTaxId, invoiceNo, invoiceDate),
            DateTimeOffset.UtcNow);
    }
}
