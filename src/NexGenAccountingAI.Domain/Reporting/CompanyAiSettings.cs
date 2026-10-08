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

    /// <summary>
    /// Se os nomes e os identificadores (NIF, IBAN, email) não saem para a IA: são trocados por marcadores antes do pedido e
    /// repostos na resposta. Ligado por omissão.
    /// </summary>
    public bool AnonymizeNames { get; private set; } = true;

    /// <summary>
    /// Quanto tempo se guardam as conversas com o assistente: <c>null</c> = sem prazo; <c>0</c> = não se guardam (cada pergunta é
    /// independente); <c>N</c> = apagam-se ao fim de N dias.
    /// </summary>
    public int? ConversationRetentionDays { get; private set; }

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

    public void ConfigurePrivacy(bool anonymizeNames, int? conversationRetentionDays)
    {
        if (conversationRetentionDays is < 0 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(conversationRetentionDays), "O prazo tem de estar entre 0 e 3650 dias.");
        }

        AnonymizeNames = anonymizeNames;
        ConversationRetentionDays = conversationRetentionDays;
    }
}
