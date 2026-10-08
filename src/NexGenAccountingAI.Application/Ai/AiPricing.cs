using System.Text.Json;
using NexGenAccountingAI.Domain.Reporting;

namespace NexGenAccountingAI.Application.Ai;

/// <summary>Os modelos de origem de cada função: o mais barato que dá respostas satisfatórias, e um melhor para os relatórios.</summary>
public static class AiModels
{
    public const string Haiku = "claude-haiku-4-5-20251001";

    public const string Sonnet = "claude-sonnet-5-5";

    public const string Opus = "claude-opus-5-5";

    /// <summary>Os modelos que o produtor pode escolher no ecrã, do mais barato para o mais capaz.</summary>
    public static IReadOnlyList<string> Choices { get; } = [Haiku, Sonnet, Opus];

    /// <summary>
    /// Conversa, variações, listagens e classificação usam o modelo pequeno (perguntas sobre números que as ferramentas já trouxeram);
    /// os relatórios de gestão e para a banca usam o intermédio, que escreve melhor.
    /// </summary>
    public static string Default(AiFeature feature) => feature == AiFeature.Report ? Sonnet : Haiku;
}

/// <summary>
/// O que custa cada modelo (USD por milhão de tokens, entrada e saída). Os valores de origem são <b>de referência</b>: confirme-os em
/// anthropic.com/pricing e, se mudaram, corrija-os na configuração geral da IA (substituem os de origem).
/// </summary>
public static class AiPricing
{
    /// <summary>Quanto se paga por milhão de tokens: <c>Input</c> e <c>Output</c>.</summary>
    public readonly record struct Price(decimal Input, decimal Output);

    private static readonly (string Contains, Price Price)[] Defaults =
    [
        ("haiku", new Price(1m, 5m)),
        ("sonnet", new Price(3m, 15m)),
        ("opus", new Price(5m, 25m)),
    ];

    /// <summary>Um modelo desconhecido paga-se pelo preço mais alto, para nunca se gastar mais do que se contou.</summary>
    public static Price Fallback { get; } = new(5m, 25m);

    public static Price PriceOf(string model, string? overridesJson = null)
    {
        if (!string.IsNullOrWhiteSpace(overridesJson))
        {
            try
            {
                var overrides = JsonSerializer.Deserialize<Dictionary<string, decimal[]>>(overridesJson);
                if (overrides is not null && overrides.TryGetValue(model, out var pair) && pair is { Length: 2 } && pair[0] >= 0 && pair[1] >= 0)
                {
                    return new Price(pair[0], pair[1]);
                }
            }
            catch (JsonException)
            {
                // Os preços mal escritos ignoram-se: valem os de origem.
            }
        }

        foreach (var (contains, price) in Defaults)
        {
            if (model.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                return price;
            }
        }

        return Fallback;
    }

    /// <summary>O custo em USD de um uso.</summary>
    public static decimal CostUsd(string model, int inputTokens, int outputTokens, string? overridesJson = null)
    {
        var price = PriceOf(model, overridesJson);
        return decimal.Round(((Math.Max(0, inputTokens) * price.Input) + (Math.Max(0, outputTokens) * price.Output)) / 1_000_000m, 6);
    }
}
