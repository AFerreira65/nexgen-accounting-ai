namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>
/// Interruptor dos relatórios de gestão com IA de uma empresa: se a
/// funcionalidade está ativa e se a síntese mensal é gerada automaticamente.
/// Uma linha por empresa.
/// </summary>
public sealed class CompanyAiSettings
{
    private CompanyAiSettings()
    {
    }

    private CompanyAiSettings(Guid id, Guid companyId)
    {
        Id = id;
        CompanyId = companyId;
        Enabled = false;
        AutoMonthly = false;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Se os relatórios de gestão com IA estão disponíveis para a empresa.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Se a síntese mensal é gerada automaticamente (F2-05E).</summary>
    public bool AutoMonthly { get; private set; }

    public uint RowVersion { get; private set; }

    public static CompanyAiSettings Create(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new CompanyAiSettings(Guid.NewGuid(), companyId);
    }

    public void Update(bool enabled, bool autoMonthly)
    {
        Enabled = enabled;
        AutoMonthly = autoMonthly;
    }
}
