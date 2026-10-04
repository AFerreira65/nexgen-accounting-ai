using NexGenAccountingAI.Core.Models;

namespace NexGenAccountingAI.Core.Efatura;

public sealed class EfaturaCsvImportResult
{
    public RecordTable Purchases { get; } = new(SaftTableKeys.EfaturaPurchases, "Compras e-Fatura");

    public RecordTable Lines { get; } = new(SaftTableKeys.EfaturaPurchaseLines, "Linhas das compras e-Fatura");

    public RecordTable Batches { get; } = new(SaftTableKeys.EfaturaCsvBatches, "Lotes CSV e-Fatura");

    public List<string> Warnings { get; } = [];
}
