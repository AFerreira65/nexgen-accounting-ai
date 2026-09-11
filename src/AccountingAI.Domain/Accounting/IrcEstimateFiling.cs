namespace AccountingAI.Domain.Accounting;

/// <summary>
/// Uma estimativa de IRC guardada por uma empresa: o período, a taxa e as
/// correções fiscais indicadas pelo utilizador, e a fotografia do cálculo (o
/// <c>IrcEstimate</c> serializado). Um por <c>(empresa, período)</c>. Não é o
/// Modelo 22.
/// </summary>
public sealed class IrcEstimateFiling
{
    private IrcEstimateFiling()
    {
    }

    private IrcEstimateFiling(
        Guid id, Guid companyId, DateOnly periodFrom, DateOnly periodTo,
        decimal taxRate, string adjustmentsJson, string snapshotJson)
    {
        Id = id;
        CompanyId = companyId;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        TaxRate = taxRate;
        AdjustmentsJson = adjustmentsJson;
        SnapshotJson = snapshotJson;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    /// <summary>A taxa indicada pelo utilizador (fração, ex.: 0.21).</summary>
    public decimal TaxRate { get; private set; }

    /// <summary>As correções fiscais indicadas pelo utilizador, serializadas em JSON.</summary>
    public string AdjustmentsJson { get; private set; } = "[]";

    /// <summary>O <c>IrcEstimate</c> calculado ao guardar, serializado em JSON.</summary>
    public string SnapshotJson { get; private set; } = "{}";

    public byte[] RowVersion { get; private set; } = [];

    public static IrcEstimateFiling Create(
        Guid companyId, DateOnly periodFrom, DateOnly periodTo,
        decimal taxRate, string adjustmentsJson, string snapshotJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (periodTo < periodFrom)
        {
            throw new ArgumentException("A data final não pode ser anterior à data inicial.", nameof(periodTo));
        }

        if (taxRate <= 0m || taxRate > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRate), "A taxa deve estar entre 0% e 100%.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(adjustmentsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        return new IrcEstimateFiling(
            Guid.NewGuid(), companyId, periodFrom, periodTo, taxRate, adjustmentsJson, snapshotJson);
    }

    public void Update(decimal taxRate, string adjustmentsJson, string snapshotJson)
    {
        if (taxRate <= 0m || taxRate > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRate), "A taxa deve estar entre 0% e 100%.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(adjustmentsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        TaxRate = taxRate;
        AdjustmentsJson = adjustmentsJson;
        SnapshotJson = snapshotJson;
    }
}
