using System.Text.Json;
using NexGenAccountingAI.Application.Chat;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Domain.Reporting;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// O adaptador do modelo de conversa (F4F-04): traduz as mensagens, as ferramentas e a resposta entre os tipos
/// da aplicação e os do SDK. É só a fronteira; o ciclo (ferramentas, limites) vive em <see cref="DataChatAgent"/>.
/// Sem chave, comporta-se como desligado.
/// </summary>
public sealed class ClaudeChatModelClient : IChatModelClient
{
    // Respostas curtas sobre números que as ferramentas já trouxeram: sem raciocínio alargado, que se paga como saída.
    private const int MaxOutputTokens = 3000;

    private readonly AiOptions options;
    private readonly AiClientProvider provider;

    public ClaudeChatModelClient(IOptions<AiOptions> options, AiClientProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.provider = provider ?? new AiClientProvider(options);
    }

    private AnthropicClient? client => provider.Client;

    public bool IsConfigured => client is not null;

    public string Model => provider.ModelFor(AiFeature.Chat);

    public async Task<ChatCompletion> CompleteAsync(
        string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            return new ChatCompletion([new ChatBlock.Text("A conversa com a IA não está configurada.")], "end_turn", 0, 0);
        }

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = provider.ModelFor(AiFeature.Chat),
            MaxTokens = MaxOutputTokens,
            // O enunciado (com o catálogo de listagens, vários milhares de tokens) repete-se em cada ronda: com a cache, as rondas
            // seguintes pagam-no a uma fração do preço.
            System = new List<TextBlockParam> { new() { Text = system, CacheControl = new CacheControlEphemeral() } },
            Tools = tools.Select(ToTool).Select(tool => (ToolUnion)tool).ToList(),
            Messages = messages.Select(ToParam).ToList(),
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var blocks = new List<ChatBlock>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? text))
            {
                blocks.Add(new ChatBlock.Text(text.Text));
            }
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
            {
                blocks.Add(new ChatBlock.ToolUse(toolUse.ID, toolUse.Name, JsonSerializer.Serialize(toolUse.Input)));
            }
            else if (block.TryPickThinking(out ThinkingBlock? thinking))
            {
                blocks.Add(new ChatBlock.Thinking(thinking.Thinking, thinking.Signature));
            }
            else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
            {
                blocks.Add(new ChatBlock.RedactedThinking(redacted.Data));
            }
        }

        return new ChatCompletion(
            blocks, StopReasonText(response), BilledInputTokens(response.Usage), ToInt(response.Usage?.OutputTokens));
    }

    /// <summary>
    /// Os tokens de entrada em «equivalente a preço normal»: a escrita na cache paga-se a 1,25 vezes e a leitura a 0,1 vez, e a API
    /// não os inclui nos tokens de entrada. Sem isto, o consumo registado ficava abaixo do que a Anthropic cobra.
    /// </summary>
    internal static int BilledInputTokens(Usage? usage) => usage is null
        ? 0
        : (int)Math.Min(int.MaxValue, ToInt(usage.InputTokens) + (long)Math.Ceiling(ToInt(usage.CacheCreationInputTokens) * 1.25) + (long)Math.Ceiling(ToInt(usage.CacheReadInputTokens) * 0.1));

    private static string StopReasonText(Message response) => NormalizeStopReason(response.StopReason?.ToString());

    /// <summary>
    /// O motivo de paragem no formato da API (<c>tool_use</c>, <c>end_turn</c>, <c>max_tokens</c>). O SDK devolve-o como texto JSON,
    /// com aspas (<c>"tool_use"</c>), e a conversa só continua depois de um pedido de ferramenta se reconhecer <c>tool_use</c>.
    /// </summary>
    public static string NormalizeStopReason(string? raw) =>
        (raw ?? string.Empty).Trim().Trim('"').Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "tooluse" => "tool_use",
            "endturn" => "end_turn",
            "maxtokens" => "max_tokens",
            "stopsequence" => "stop_sequence",
            var other => other,
        };

    private static Tool ToTool(ChatToolSpec spec)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(spec.PropertiesJson) ?? [];
        return new Tool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = new() { Properties = properties, Required = [.. spec.Required] },
        };
    }

    private static MessageParam ToParam(ChatTurnMessage message)
    {
        var content = new List<ContentBlockParam>();
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case ChatBlock.Text text:
                    content.Add(new TextBlockParam { Text = text.Value });
                    break;
                case ChatBlock.Thinking thinking:
                    content.Add(new ThinkingBlockParam { Thinking = thinking.Value, Signature = thinking.Signature });
                    break;
                case ChatBlock.RedactedThinking redacted:
                    content.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                    break;
                case ChatBlock.ToolUse toolUse:
                    content.Add(new ToolUseBlockParam
                    {
                        ID = toolUse.Id,
                        Name = toolUse.Name,
                        Input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(string.IsNullOrWhiteSpace(toolUse.InputJson) ? "{}" : toolUse.InputJson) ?? [],
                    });
                    break;
                case ChatBlock.ToolResult result:
                    content.Add(new ToolResultBlockParam { ToolUseID = result.ToolUseId, Content = result.Content, IsError = result.IsError });
                    break;
            }
        }

        return new MessageParam { Role = message.Role == "assistant" ? Role.Assistant : Role.User, Content = content };
    }

    private static int ToInt(long? value) => value is { } number && number is >= int.MinValue and <= int.MaxValue ? (int)number : 0;
}

/// <summary>Sem chave de API: a conversa com a IA fica desligada.</summary>
public sealed class NullChatModelClient : IChatModelClient
{
    public bool IsConfigured => false;

    public string Model => "—";

    public Task<ChatCompletion> CompleteAsync(
        string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatCompletion([new ChatBlock.Text("A conversa com a IA não está configurada.")], "end_turn", 0, 0));
}
