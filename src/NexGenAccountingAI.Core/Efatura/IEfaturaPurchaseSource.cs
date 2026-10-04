namespace NexGenAccountingAI.Core.Efatura;

public interface IEfaturaPurchaseSource
{
    Task<EfaturaQueryResult> GetPurchasesAsync(
        DateOnly startDate,
        DateOnly endDate,
        IProgress<EfaturaQueryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
