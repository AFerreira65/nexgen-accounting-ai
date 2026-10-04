using NexGenAccountingAI.Core.Models;

namespace NexGenAccountingAI.Core.Efatura;

public sealed class EfaturaQueryResult
{
    public RecordTable Purchases { get; } = new(SaftTableKeys.EfaturaPurchases, "Compras e-Fatura");

    public RecordTable Lines { get; } = new(SaftTableKeys.EfaturaPurchaseLines, "Linhas das compras e-Fatura");

    public List<string> Messages { get; } = [];
}

public sealed record EfaturaQueryProgress(
    DateOnly Date,
    int Page,
    int TotalPages,
    int DocumentsRead,
    string Message);
