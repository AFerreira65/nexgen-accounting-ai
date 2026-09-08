namespace AccountingAI.Application.Reporting;

/// <summary>
/// Corre uma <see cref="AnalysisDefinition"/> contra a origem, num intervalo de
/// datas, e devolve o <see cref="AnalysisResult"/>. Devolve <c>null</c> quando a
/// empresa não existe ou o utilizador não lhe tem acesso; lança
/// <see cref="ArgumentException"/> quando a definição não passa no catálogo.
/// </summary>
public interface IAnalysisRunner
{
    Task<AnalysisResult?> RunAsync(
        Guid userId,
        Guid companyId,
        AnalysisDefinition definition,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);
}
