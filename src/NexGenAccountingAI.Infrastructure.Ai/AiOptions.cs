namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// Configuração da IA, ligada da secção <c>Ai</c>. A <see cref="ApiKey"/> vem só
/// de <em>user-secrets</em> ou da variável de ambiente <c>ANTHROPIC_API_KEY</c>
/// — nunca de <c>appsettings*.json</c> (ADR-0006).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>O modelo a usar. Por omissão, o mais capaz.</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>A chave da API da Anthropic. Vazia → a funcionalidade fica desligada.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Teto de <em>tokens</em> da resposta (inclui o raciocínio).</summary>
    public int MaxOutputTokens { get; set; } = 12000;
}
