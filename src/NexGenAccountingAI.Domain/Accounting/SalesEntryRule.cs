namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// A regra de lançamento das vendas a uma taxa de IVA: a parte de cada documento
/// de venda a esta percentagem é sugerida com o lançamento-tipo indicado (a conta
/// de rendimento, a de IVA liquidado e a do cliente).
/// </summary>
public sealed class SalesEntryRule
{
    private SalesEntryRule()
    {
    }

    private SalesEntryRule(Guid id, Guid companyId, decimal taxPercentage)
    {
        Id = id;
        CompanyId = companyId;
        TaxPercentage = taxPercentage;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>A percentagem de IVA (23, 13, 6, 0…).</summary>
    public decimal TaxPercentage { get; private set; }

    public Guid EntryTemplateId { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public EntryTemplate? EntryTemplate { get; private set; }

    public static SalesEntryRule Create(Guid companyId, decimal taxPercentage)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new SalesEntryRule(Guid.NewGuid(), companyId, NormalizePercentage(taxPercentage));
    }

    public void Update(Guid entryTemplateId, bool isActive)
    {
        if (entryTemplateId == Guid.Empty)
        {
            throw new ArgumentException("O lançamento-tipo é obrigatório.", nameof(entryTemplateId));
        }

        EntryTemplateId = entryTemplateId;
        IsActive = isActive;
    }

    public static decimal NormalizePercentage(decimal taxPercentage)
    {
        if (taxPercentage is < 0m or > 100m)
        {
            throw new ArgumentException("A percentagem de IVA tem de estar entre 0 e 100.", nameof(taxPercentage));
        }

        return decimal.Round(taxPercentage, 2);
    }
}
