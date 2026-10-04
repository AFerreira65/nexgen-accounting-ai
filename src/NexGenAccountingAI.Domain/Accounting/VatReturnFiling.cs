namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// Um apuramento periódico de IVA guardado por uma empresa: o período, o excesso
/// a reportar do período anterior indicado pelo utilizador, e a fotografia do
/// cálculo (o <c>VatReturn</c> serializado). Um por <c>(empresa, período)</c>.
/// Não é a declaração periódica oficial da AT.
/// </summary>
public sealed class VatReturnFiling
{
    private VatReturnFiling()
    {
    }

    private VatReturnFiling(
        Guid id, Guid companyId, DateOnly periodFrom, DateOnly periodTo,
        decimal priorPeriodCredit, string snapshotJson)
    {
        Id = id;
        CompanyId = companyId;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        PriorPeriodCredit = priorPeriodCredit;
        SnapshotJson = snapshotJson;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    /// <summary>O excesso a reportar do período anterior, indicado pelo utilizador.</summary>
    public decimal PriorPeriodCredit { get; private set; }

    /// <summary>O <c>VatReturn</c> calculado ao guardar, serializado em JSON.</summary>
    public string SnapshotJson { get; private set; } = "{}";

    public uint RowVersion { get; private set; }

    public static VatReturnFiling Create(
        Guid companyId, DateOnly periodFrom, DateOnly periodTo, decimal priorPeriodCredit, string snapshotJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (periodTo < periodFrom)
        {
            throw new ArgumentException("A data final não pode ser anterior à data inicial.", nameof(periodTo));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        return new VatReturnFiling(
            Guid.NewGuid(), companyId, periodFrom, periodTo,
            decimal.Round(priorPeriodCredit, 2), snapshotJson);
    }

    public void Update(decimal priorPeriodCredit, string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        PriorPeriodCredit = decimal.Round(priorPeriodCredit, 2);
        SnapshotJson = snapshotJson;
    }
}
