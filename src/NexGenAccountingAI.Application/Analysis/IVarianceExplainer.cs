namespace NexGenAccountingAI.Application.Analysis;

/// <summary>O texto que a IA escreveu sobre as variações, com os metadados da chamada.</summary>
public sealed record VarianceExplanation(string Text, string Model, int PromptTokens, int OutputTokens);

/// <summary>
/// Escreve, a partir dos números de uma comparação entre períodos (em JSON), um texto curto a dizer o que mais
/// mudou e o que convém verificar (F4F-03). A fronteira que conhece o SDK vive em
/// <c>NexGenAccountingAI.Infrastructure.Ai</c> (ADR-0006). Só recebe os números da comparação.
/// </summary>
public interface IVarianceExplainer
{
    /// <summary><c>true</c> quando há uma chave de API configurada.</summary>
    bool IsConfigured { get; }

    string Model { get; }

    Task<VarianceExplanation> ExplainAsync(string reportJson, CancellationToken cancellationToken = default);
}
