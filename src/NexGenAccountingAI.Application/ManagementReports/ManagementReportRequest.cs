namespace NexGenAccountingAI.Application.ManagementReports;

/// <summary>
/// O que se pede ao motor de relatórios de gestão: um tipo pré-definido
/// (<see cref="Kind"/>) <em>ou</em> uma pergunta livre (<see cref="Question"/>),
/// sempre para um intervalo de datas do exercício.
/// </summary>
/// <param name="Kind">O tipo pré-definido, ou <c>null</c> quando é uma pergunta livre.</param>
/// <param name="Question">A pergunta livre, ou <c>null</c> quando é um tipo pré-definido.</param>
/// <param name="From">Data inicial do período (inclusive).</param>
/// <param name="To">Data final do período (inclusive).</param>
public sealed record ManagementReportRequest(
    ManagementReportKind? Kind,
    string? Question,
    DateOnly From,
    DateOnly To)
{
    /// <summary>`true` quando o pedido é uma pergunta livre com texto.</summary>
    public bool IsQuestion => !string.IsNullOrWhiteSpace(Question);
}
