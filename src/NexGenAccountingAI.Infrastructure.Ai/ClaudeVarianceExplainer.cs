using NexGenAccountingAI.Application.Analysis;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// Pede ao Claude que explique, em poucas linhas, as maiores variações de uma comparação entre períodos
/// (F4F-03). Recebe só os números dessa comparação, já calculados; não inventa causas. Sem chave, não chama nada.
/// </summary>
public sealed class ClaudeVarianceExplainer : IVarianceExplainer
{
    private const int MaxOutputTokens = 1500;

    private readonly AiOptions options;
    private readonly AiClientProvider provider;

    public ClaudeVarianceExplainer(IOptions<AiOptions> options, AiClientProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.provider = provider ?? new AiClientProvider(options);
    }

    private AnthropicClient? client => provider.Client;

    public bool IsConfigured => client is not null;

    public string Model => options.Model;

    public async Task<VarianceExplanation> ExplainAsync(string reportJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportJson);
        if (client is null)
        {
            return new VarianceExplanation("A explicação com IA não está configurada.", "—", 0, 0);
        }

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = MaxOutputTokens,
            System = new List<TextBlockParam> { new() { Text = VariancePrompts.System } },
            Messages = [new() { Role = Role.User, Content = VariancePrompts.User(reportJson) }],
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = string.Concat(response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text)).Trim();
        return new VarianceExplanation(
            text, options.Model,
            response.Usage?.InputTokens is { } input && input <= int.MaxValue ? (int)input : 0,
            response.Usage?.OutputTokens is { } output && output <= int.MaxValue ? (int)output : 0);
    }
}

/// <summary>O enunciado da explicação de variações.</summary>
public static class VariancePrompts
{
    public const string System = """
        És um contabilista certificado português a explicar a um empresário o que mudou nos gastos e nos rendimentos
        entre dois períodos. Recebes, em JSON, os totais dos dois períodos e as contas com maior variação (valor atual,
        valor anterior, diferença, percentagem e, nas maiores, os terceiros que mais contribuíram).

        Regras:
        - Usa só os números do JSON. Não inventes valores, contas, terceiros nem causas.
        - Quando sugerires uma causa possível, diz que é uma hipótese a confirmar (por exemplo «pode dever-se a…»).
        - Escreve em português europeu, em texto corrido, com no máximo 150 palavras, sem listas nem títulos.
        - Começa pelo efeito no resultado, depois as duas ou três variações que mais pesam.
        - Termina com uma frase a dizer o que convém verificar primeiro.
        - Valores em euros, com vírgula decimal e espaço como separador de milhares.
        """;

    public static string User(string reportJson) => $"Comparação entre os dois períodos:\n{reportJson}";
}

/// <summary>Sem chave de API: a explicação com IA fica desligada.</summary>
public sealed class NullVarianceExplainer : IVarianceExplainer
{
    public bool IsConfigured => false;

    public string Model => "—";

    public Task<VarianceExplanation> ExplainAsync(string reportJson, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VarianceExplanation("A explicação com IA não está configurada.", "—", 0, 0));
}
