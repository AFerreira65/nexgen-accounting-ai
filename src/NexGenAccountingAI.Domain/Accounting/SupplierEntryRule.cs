namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// A regra de lançamento de um fornecedor: as faturas do e-Fatura deste NIF são
/// sugeridas com o lançamento-tipo indicado (o diário, o tipo de documento e as
/// contas de gasto, de IVA e do fornecedor).
/// </summary>
public sealed class SupplierEntryRule
{
    public const int TaxIdMaxLength = 20;

    public const int DescriptionMaxLength = 300;

    private SupplierEntryRule()
    {
    }

    private SupplierEntryRule(Guid id, Guid companyId, string supplierTaxId)
    {
        Id = id;
        CompanyId = companyId;
        SupplierTaxId = supplierTaxId;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>O NIF do fornecedor, como vem no e-Fatura.</summary>
    public string SupplierTaxId { get; private set; } = string.Empty;

    public Guid EntryTemplateId { get; private set; }

    /// <summary>A descrição do lançamento; `null` para usar a do lançamento-tipo ou o nome do fornecedor.</summary>
    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public EntryTemplate? EntryTemplate { get; private set; }

    public static SupplierEntryRule Create(Guid companyId, string supplierTaxId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new SupplierEntryRule(Guid.NewGuid(), companyId, NormalizeTaxId(supplierTaxId));
    }

    public void Update(Guid entryTemplateId, string? description, bool isActive)
    {
        if (entryTemplateId == Guid.Empty)
        {
            throw new ArgumentException("O lançamento-tipo é obrigatório.", nameof(entryTemplateId));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > DescriptionMaxLength })
        {
            throw new ArgumentException($"A descrição não pode ter mais de {DescriptionMaxLength} caracteres.", nameof(description));
        }

        EntryTemplateId = entryTemplateId;
        Description = normalizedDescription;
        IsActive = isActive;
    }

    public static string NormalizeTaxId(string? supplierTaxId)
    {
        var normalized = new string((supplierTaxId ?? string.Empty).Where(character => !char.IsWhiteSpace(character)).ToArray())
            .ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("O NIF do fornecedor é obrigatório.", nameof(supplierTaxId));
        }

        if (normalized.Length > TaxIdMaxLength)
        {
            throw new ArgumentException($"O NIF não pode ter mais de {TaxIdMaxLength} caracteres.", nameof(supplierTaxId));
        }

        return normalized;
    }
}
