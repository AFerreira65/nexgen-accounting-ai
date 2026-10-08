using NexGenAccountingAI.Application.Ai;

namespace NexGenAccountingAI.Application.Chat;

/// <summary>
/// Põe um <see cref="AiTextProtector"/> à frente de um modelo de conversa: o que sai (enunciado, mensagens, resultados das
/// ferramentas) vai sem nomes nem identificadores, e o que volta (texto e pedidos de ferramentas) é reposto antes de a aplicação o
/// usar — os pedidos de ferramentas voltam com os nomes verdadeiros, para a ferramenta os encontrar. Um objeto por pergunta: o
/// protetor guarda a numeração dos marcadores durante todas as rondas.
/// </summary>
public sealed class ProtectingChatModelClient(IChatModelClient inner, AiTextProtector protector) : IChatModelClient
{
    public bool IsConfigured => inner.IsConfigured;

    public string Model => inner.Model;

    public async Task<ChatCompletion> CompleteAsync(
        string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default)
    {
        var protectedMessages = messages.Select(message => new ChatTurnMessage(message.Role, message.Blocks.Select(Protect).ToList())).ToList();
        var completion = await inner.CompleteAsync(protector.Protect(system), protectedMessages, tools, cancellationToken).ConfigureAwait(false);
        return completion with { Blocks = completion.Blocks.Select(Restore).ToList() };
    }

    private ChatBlock Protect(ChatBlock block) => block switch
    {
        ChatBlock.Text text => new ChatBlock.Text(protector.Protect(text.Value)),
        ChatBlock.ToolResult result => result with { Content = protector.Protect(result.Content) },
        ChatBlock.ToolUse use => use with { InputJson = protector.Protect(use.InputJson) },
        _ => block,
    };

    private ChatBlock Restore(ChatBlock block) => block switch
    {
        ChatBlock.Text text => new ChatBlock.Text(protector.Restore(text.Value)),
        ChatBlock.ToolUse use => use with { InputJson = protector.Restore(use.InputJson, jsonEscaped: true) },
        _ => block,
    };
}
