namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>As funções da IA, para escolher o modelo de cada uma e para saber onde se gastou.</summary>
public enum AiFeature
{
    Chat = 1,
    Report = 2,
    Variance = 3,
    Listing = 4,
    Classification = 5,
}

/// <summary>
/// O orçamento de IA de uma empresa (em USD, a moeda em que a Anthropic cobra): uma <b>franquia mensal</b> que recomeça todos os
/// meses e um <b>crédito extra</b> que não caduca e só se gasta depois de a franquia do mês acabar. Sem franquia definida (nem uma
/// por omissão na configuração geral), o consumo é ilimitado — só se regista.
/// </summary>
public sealed class CompanyAiBudget
{
    private CompanyAiBudget()
    {
    }

    public CompanyAiBudget(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        CompanyId = companyId;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>A franquia mensal em USD; <c>null</c> = usa a franquia por omissão da configuração geral.</summary>
    public decimal? MonthlyAllowanceUsd { get; private set; }

    /// <summary>O crédito extra que ainda resta, em USD.</summary>
    public decimal ExtraCreditUsd { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public void SetAllowance(decimal? monthlyAllowanceUsd, DateTimeOffset now)
    {
        if (monthlyAllowanceUsd is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyAllowanceUsd), "A franquia não pode ser negativa.");
        }

        MonthlyAllowanceUsd = monthlyAllowanceUsd is null ? null : decimal.Round(monthlyAllowanceUsd.Value, 4);
        UpdatedAtUtc = now;
    }

    public void AddCredit(decimal amountUsd, DateTimeOffset now)
    {
        if (amountUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountUsd), "O crédito a juntar tem de ser positivo.");
        }

        ExtraCreditUsd = decimal.Round(ExtraCreditUsd + amountUsd, 4);
        UpdatedAtUtc = now;
    }

    /// <summary>Tira do crédito extra o que a franquia do mês não cobriu.</summary>
    public void SpendExtra(decimal amountUsd, DateTimeOffset now)
    {
        ExtraCreditUsd = Math.Max(0m, decimal.Round(ExtraCreditUsd - Math.Max(0m, amountUsd), 4));
        UpdatedAtUtc = now;
    }
}

/// <summary>Um carregamento de crédito extra, para ficar registado (quem, quando e quanto).</summary>
public sealed class AiCreditTopUp
{
    private AiCreditTopUp()
    {
    }

    public AiCreditTopUp(Guid id, Guid companyId, decimal amountUsd, string? note, Guid? createdBy, DateTimeOffset createdAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        AmountUsd = amountUsd;
        Note = string.IsNullOrWhiteSpace(note) ? null : (note.Trim().Length > 300 ? note.Trim()[..300] : note.Trim());
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public decimal AmountUsd { get; private set; }

    public string? Note { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}

/// <summary>Um uso da IA: quando, em que empresa e função, com que modelo, quantos tokens e quanto custou (USD).</summary>
public sealed class AiUsageRecord
{
    private AiUsageRecord()
    {
    }

    public AiUsageRecord(Guid id, Guid companyId, Guid? userId, AiFeature feature, string model, int inputTokens, int outputTokens, decimal costUsd, DateTimeOffset createdAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        UserId = userId;
        Feature = feature;
        Model = model.Length > 80 ? model[..80] : model;
        InputTokens = Math.Max(0, inputTokens);
        OutputTokens = Math.Max(0, outputTokens);
        CostUsd = decimal.Round(Math.Max(0m, costUsd), 6);
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid? UserId { get; private set; }

    public AiFeature Feature { get; private set; }

    public string Model { get; private set; } = string.Empty;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public decimal CostUsd { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
