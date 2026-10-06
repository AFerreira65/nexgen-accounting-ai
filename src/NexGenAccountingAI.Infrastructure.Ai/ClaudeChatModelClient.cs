using System.Text.Json;
using NexGenAccountingAI.Application.Chat;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// O adaptador do modelo de conversa (F4F-04): traduz as mensagens, as ferramentas e a resposta entre os tipos
/// da aplicação e os do SDK. É só a fronteira; o ciclo (ferramentas, limites) vive em <see cref="DataChatAgent"/>.
/// Sem chave, comporta-se como desligado.
/// </summary>
public sealed class ClaudeChatModelClient : IChatModelClient
{
    private const int MaxOutputTokens = 8000;

    private readonly AiOptions options;
    private readonly AnthropicClient? client;

    public ClaudeChatModelClient(IOptions<AiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        if (!string.IsNullOrWhiteSpace(this.options.ApiKey))
        {
            client = new AnthropicClient { ApiKey = this.options.ApiKey };
        }
    }

    public bool IsConfigured => client is not null;

    public string Model => options.Model;

    public async Task<ChatCompletion> CompleteAsync(
        string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            return new ChatCompletion([new ChatBlock.Text("A conversa com a IA não está configurada.")], "end_turn", 0, 0);
        }

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = MaxOutputTokens,
            Thinking = new ThinkingConfigAdaptive(),
            System = new List<TextBlockParam> { new() { Text = system } },
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
            blocks, StopReasonText(response), ToInt(response.Usage?.InputTokens), ToInt(response.Usage?.OutputTokens));
    }

    private static string StopReasonText(Message response) =>
        (response.StopReason?.ToString() ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "tooluse" => "tool_use",
            "endturn" => "end_turn",
            "maxtokens" => "max_tokens",
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
