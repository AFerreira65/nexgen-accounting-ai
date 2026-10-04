namespace NexGenAccountingAI.Domain.Billing;

/// <summary>A base e o imposto de um documento de venda a uma taxa de IVA.</summary>
public sealed record SalesDocumentTax(decimal Percentage, decimal TaxBase, decimal TaxAmount);

/// <summary>
/// Um documento de venda (fatura, fatura simplificada, fatura-recibo, nota de
/// crédito ou de débito) importado de um SAF-T (PT) de faturação, com a base e o
/// imposto por taxa. Guarda-se por empresa para sugerir o lançamento de cada
/// documento. Os valores são positivos; o tipo diz se é uma nota de crédito.
/// </summary>
public sealed class SalesDocument
{
    public const int InvoiceNoMaxLength = 60;

    public const int JournalEntryReferenceMaxLength = 48;

    private readonly List<SalesDocumentTax> taxes = [];

    private SalesDocument()
    {
    }

    private SalesDocument(Guid id, Guid companyId, Guid importBatchId, string invoiceNo)
    {
        Id = id;
        CompanyId = companyId;
        ImportBatchId = importBatchId;
        InvoiceNo = invoiceNo;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Agrupa os documentos importados na mesma chamada.</summary>
    public Guid ImportBatchId { get; private set; }

    /// <summary>O número do documento (tipo, série e número), único na empresa.</summary>
    public string InvoiceNo { get; private set; } = string.Empty;

    public string? Atcud { get; private set; }

    /// <summary>FT, FS, FR, NC ou ND.</summary>
    public string InvoiceType { get; private set; } = string.Empty;

    public DateOnly InvoiceDate { get; private set; }

    public bool IsVoided { get; private set; }

    public string CustomerTaxId { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string? CustomerCountry { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal TaxPayable { get; private set; }

    public decimal GrossTotal { get; private set; }

    public IReadOnlyList<SalesDocumentTax> Taxes => taxes;

    public string SourceFileName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>A referência do lançamento criado a partir deste documento, se houver.</summary>
    public string? JournalEntryReference { get; private set; }

    public bool IsCreditNote => InvoiceType.Equals("NC", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Os consumidores finais têm no SAF-T o NIF genérico 999999990; nessas vendas
    /// o lançamento não leva terceiro.
    /// </summary>
    public bool IsFinalConsumer => CustomerTaxId is "999999990" or "";

    public static SalesDocument Create(
        Guid companyId,
        Guid importBatchId,
        string invoiceNo,
        string? atcud,
        string invoiceType,
        DateOnly invoiceDate,
        bool isVoided,
        string? customerTaxId,
        string? customerName,
        string? customerCountry,
        decimal netTotal,
        decimal taxPayable,
        decimal grossTotal,
        IEnumerable<SalesDocumentTax> taxes,
        string? sourceFileName)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        var number = (invoiceNo ?? string.Empty).Trim();
        if (number.Length == 0 || number.Length > InvoiceNoMaxLength)
        {
            throw new ArgumentException($"O número do documento é obrigatório e tem no máximo {InvoiceNoMaxLength} caracteres.", nameof(invoiceNo));
        }

        var document = new SalesDocument(Guid.NewGuid(), companyId, importBatchId, number)
        {
            Atcud = string.IsNullOrWhiteSpace(atcud) ? null : atcud.Trim(),
            InvoiceType = (invoiceType ?? string.Empty).Trim().ToUpperInvariant(),
            InvoiceDate = invoiceDate,
            IsVoided = isVoided,
            CustomerTaxId = (customerTaxId ?? string.Empty).Trim().ToUpperInvariant(),
            CustomerName = (customerName ?? string.Empty).Trim(),
            // O SAF-T usa «Desconhecido» no consumidor final: só se guarda um código de país.
            CustomerCountry = customerCountry?.Trim() is { Length: 2 } country ? country.ToUpperInvariant() : null,
            NetTotal = Round(netTotal),
            TaxPayable = Round(taxPayable),
            GrossTotal = Round(grossTotal),
            SourceFileName = (sourceFileName ?? string.Empty).Trim(),
        };
        document.taxes.AddRange((taxes ?? []).Select(tax => new SalesDocumentTax(
            decimal.Round(tax.Percentage, 2), Round(tax.TaxBase), Round(tax.TaxAmount))));
        return document;
    }

    public void LinkToEntry(string journalEntryReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalEntryReference);
        JournalEntryReference = journalEntryReference.Trim();
    }

    private static decimal Round(decimal value) => decimal.Round(Math.Abs(value), 2, MidpointRounding.AwayFromZero);
}
