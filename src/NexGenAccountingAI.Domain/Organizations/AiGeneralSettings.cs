namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// A configuração geral da IA, comum a todas as empresas e a todos os postos que usam a mesma base de dados: a chave da API da
/// Anthropic, guardada cifrada (<c>enc:v2:…</c>). Só há uma linha, e só o produtor a altera. Cada empresa decide à parte se a
/// IA está ligada (<see cref="CompanyAiSettings"/>).
/// </summary>
public sealed class AiGeneralSettings
{
    public const int SingletonKey = 1;

    private AiGeneralSettings()
    {
    }

    public AiGeneralSettings(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    /// <summary>Constante, para a base de dados garantir uma só linha.</summary>
    public int Key { get; private set; } = SingletonKey;

    /// <summary>A chave da API, já cifrada.</summary>
    public string? EncryptedApiKey { get; private set; }

    /// <summary>Os últimos caracteres da chave, só para o ecrã a reconhecer sem a mostrar.</summary>
    public string? KeyHint { get; private set; }

    /// <summary>A franquia mensal de IA (USD) das empresas sem franquia própria; <c>null</c> = ilimitado.</summary>
    public decimal? DefaultMonthlyAllowanceUsd { get; private set; }

    /// <summary>Os modelos a usar em cada função (nulos = os de origem da aplicação).</summary>
    public string? ChatModel { get; private set; }

    public string? ReportModel { get; private set; }

    public string? QuickModel { get; private set; }

    /// <summary>Os preços por milhão de tokens (USD) que substituem os de origem, em JSON <c>{"modelo":[entrada,saída]}</c>.</summary>
    public string? PricesJson { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public bool HasKey => !string.IsNullOrEmpty(EncryptedApiKey);

    public void SetKey(string protectedKey, string hint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedKey);
        EncryptedApiKey = protectedKey;
        KeyHint = hint.Length > 8 ? hint[^8..] : hint;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ConfigureUsage(decimal? defaultMonthlyAllowanceUsd, string? chatModel, string? reportModel, string? quickModel, string? pricesJson)
    {
        if (defaultMonthlyAllowanceUsd is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMonthlyAllowanceUsd), "A franquia não pode ser negativa.");
        }

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        DefaultMonthlyAllowanceUsd = defaultMonthlyAllowanceUsd;
        ChatModel = Clean(chatModel);
        ReportModel = Clean(reportModel);
        QuickModel = Clean(quickModel);
        PricesJson = Clean(pricesJson);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ClearKey()
    {
        EncryptedApiKey = null;
        KeyHint = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
