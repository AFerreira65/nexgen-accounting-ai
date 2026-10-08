using System.Text.Json;
using NexGenAccountingAI.Application.ManagementReports;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Domain.Reporting;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// A implementação real do narrador: monta o pedido (enunciado de sistema pt-PT
/// por tipo, o dossiê em JSON como mensagem do utilizador, <em>adaptive
/// thinking</em>, teto de <em>tokens</em>), chama a API do Claude e mapeia a
/// resposta para <see cref="ManagementReportResult"/>. É a única classe que
/// conhece o SDK (ADR-0006). Sem chave, comporta-se como o narrador nulo.
/// </summary>
public sealed class ClaudeManagementReportNarrator : IManagementReportNarrator
{
    private static readonly JsonSerializerOptions DossierOptions = new(JsonSerializerDefaults.Web);

    private readonly AiOptions options;
    private readonly AiClientProvider provider;

    public ClaudeManagementReportNarrator(IOptions<AiOptions> options, AiClientProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.provider = provider ?? new AiClientProvider(options);
    }

    private AnthropicClient? client => provider.Client;

    public bool IsConfigured => client is not null;

    public string Model => provider.ModelFor(AiFeature.Report);

    public async Task<ManagementReportNarration> GenerateAsync(
        ManagementReportRequest request,
        ManagementReportDossier dossier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dossier);

        if (client is null)
        {
            return NullManagementReportNarrator.NotConfigured;
        }

        var dossierJson = JsonSerializer.Serialize(dossier, DossierOptions);

        ThinkingConfigParam? thinking = null;
        if (!provider.ModelFor(AiFeature.Report).Contains("haiku", StringComparison.OrdinalIgnoreCase))
        {
            thinking = new ThinkingConfigAdaptive();
        }

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = provider.ModelFor(AiFeature.Report),
            MaxTokens = options.MaxOutputTokens,
            // O modelo pequeno não usa raciocínio alargado: escreve o relatório diretamente, e mais barato.
            Thinking = thinking,
            System = new List<TextBlockParam>
            {
                new() { Text = ManagementReportPrompts.System(request) },
            },
            Messages =
            [
                new() { Role = Role.User, Content = ManagementReportPrompts.User(request, dossierJson) },
            ],
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = string.Concat(response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text));

        var result = ManagementReportNarrationMapper.FromJson(text);
        return new ManagementReportNarration(
            result,
            provider.ModelFor(AiFeature.Report),
            ToInt(response.Usage?.InputTokens),
            ToInt(response.Usage?.OutputTokens));
    }

    private static int ToInt(long? value) => value is { } number && number is >= int.MinValue and <= int.MaxValue
        ? (int)number
        : 0;
}
