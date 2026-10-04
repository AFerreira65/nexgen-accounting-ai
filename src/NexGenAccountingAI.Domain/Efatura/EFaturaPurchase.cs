namespace NexGenAccountingAI.Domain.Efatura;

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

    public const int JournalEntryReferenceMaxLength = 48;

    /// <summary>
    /// A referência do lançamento criado a partir desta fatura (lançamento sugerido
    /// finalizado), se houver. Guarda-se a referência e não o Id porque corrigir um
    /// lançamento recria-o com a mesma referência.
    /// </summary>
    public string? JournalEntryReference { get; private set; }

    /// <summary>
    /// Uma nota de crédito do fornecedor: o tipo de documento é <c>NC</c> ou «Nota de
    /// crédito», ou o total vem negativo. Critério da aplicação — o CSV do Portal
    /// não tem um campo próprio.
    /// </summary>
    public bool IsCreditNote => IsCreditNoteType(InvoiceType) || GrossTotal < 0m;

    /// <summary>Uma fatura anulada no Portal não se lança.</summary>
    public bool IsCancelled =>
        Status is not null && Status.Contains("anulad", StringComparison.OrdinalIgnoreCase);

    public void LinkToEntry(string journalEntryReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalEntryReference);
        JournalEntryReference = journalEntryReference.Trim();
    }

    public const int DismissedReasonMaxLength = 200;

    /// <summary>
    /// Porque é que a fatura não se lança (uma despesa particular, um duplicado);
    /// `null` enquanto conta. Uma fatura dispensada sai das sugestões e do cruzamento.
    /// </summary>
    public string? DismissedReason { get; private set; }

    public DateTimeOffset? DismissedAtUtc { get; private set; }

    public void Dismiss(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Indique porque é que a fatura não se lança.", nameof(reason));
        }

        if (normalized.Length > DismissedReasonMaxLength)
        {
            throw new ArgumentException($"O motivo não pode ter mais de {DismissedReasonMaxLength} caracteres.", nameof(reason));
        }

        DismissedReason = normalized;
        DismissedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        DismissedReason = null;
        DismissedAtUtc = null;
    }

    public static bool IsCreditNoteType(string? invoiceType)
    {
        var type = (invoiceType ?? string.Empty).Trim();
        return type.Equals("NC", StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith("NC ", StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith("Nota de cr", StringComparison.OrdinalIgnoreCase);
    }

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
