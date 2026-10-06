namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// Uma anomalia que o utilizador viu e dispensou (F4F-01), com a razão. A chave identifica o achado
/// de forma estável (a regra e os dados em causa), por isso um achado dispensado não volta a aparecer
/// enquanto os dados forem os mesmos.
/// </summary>
public sealed class AnomalyDismissal
{
    public const int KeyMaxLength = 200;

    public const int ReasonMaxLength = 300;

    private AnomalyDismissal()
    {
    }

    private AnomalyDismissal(Guid id, Guid companyId, string findingKey, string? reason, Guid dismissedBy)
    {
        Id = id;
        CompanyId = companyId;
        FindingKey = findingKey;
        Reason = reason;
        DismissedBy = dismissedBy;
        DismissedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string FindingKey { get; private set; } = string.Empty;

    public string? Reason { get; private set; }

    public Guid DismissedBy { get; private set; }

    public DateTimeOffset DismissedAtUtc { get; private set; }

    public static AnomalyDismissal Create(Guid companyId, string findingKey, string? reason, Guid dismissedBy)
    {
        if (companyId == Guid.Empty || dismissedBy == Guid.Empty)
        {
            throw new ArgumentException("A empresa e o utilizador são obrigatórios.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(findingKey);
        var key = findingKey.Trim();
        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return new AnomalyDismissal(
            Guid.NewGuid(), companyId, key.Length <= KeyMaxLength ? key : key[..KeyMaxLength],
            text is { Length: > ReasonMaxLength } ? text[..ReasonMaxLength] : text, dismissedBy);
    }
}
