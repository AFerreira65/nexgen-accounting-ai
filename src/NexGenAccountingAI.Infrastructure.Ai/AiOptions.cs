namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// Configuração da IA, ligada da secção <c>Ai</c>. A <see cref="ApiKey"/> vem só
/// de <em>user-secrets</em> ou da variável de ambiente <c>ANTHROPIC_API_KEY</c>
/// — nunca de <c>appsettings*.json</c> (ADR-0006).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Um modelo único para todas as funções, no ficheiro de configuração. Vazio (o normal) = o de origem de cada função
    /// (<c>AiModels.Default</c>), que o produtor pode mudar na configuração geral.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>A chave da API da Anthropic. Vazia → a funcionalidade fica desligada.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Teto de <em>tokens</em> da resposta (inclui o raciocínio).</summary>
    public int MaxOutputTokens { get; set; } = 8000;
}
