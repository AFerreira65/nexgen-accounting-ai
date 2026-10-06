namespace NexGenAccountingAI.Application.Chat;

/// <summary>Uma ferramenta que a IA pode pedir (só de leitura), com o esquema dos argumentos.</summary>
/// <param name="PropertiesJson">Um objeto JSON com as propriedades do esquema (<c>{"from": {"type": "string"}}</c>).</param>
public sealed record ChatToolSpec(string Name, string Description, string PropertiesJson, IReadOnlyList<string> Required);

/// <summary>Um bloco de uma mensagem: texto, um pedido de ferramenta, o resultado dela ou um bloco de raciocínio a devolver tal como veio.</summary>
public abstract record ChatBlock
{
    public sealed record Text(string Value) : ChatBlock;

    public sealed record ToolUse(string Id, string Name, string InputJson) : ChatBlock;

    public sealed record ToolResult(string ToolUseId, string Content, bool IsError) : ChatBlock;

    /// <summary>Raciocínio do modelo, que tem de voltar inalterado se a conversa continuar no mesmo pedido.</summary>
    public sealed record Thinking(string Value, string Signature) : ChatBlock;

    public sealed record RedactedThinking(string Data) : ChatBlock;
}

/// <param name="Role"><c>user</c> ou <c>assistant</c>.</param>
public sealed record ChatTurnMessage(string Role, IReadOnlyList<ChatBlock> Blocks);

public sealed record ChatCompletion(IReadOnlyList<ChatBlock> Blocks, string StopReason, int PromptTokens, int OutputTokens);

/// <summary>
/// O modelo de conversa, atrás de uma fronteira: quem conhece o SDK vive em
/// <c>NexGenAccountingAI.Infrastructure.Ai</c> (ADR-0006).
/// </summary>
public interface IChatModelClient
{
    bool IsConfigured { get; }

    string Model { get; }

    Task<ChatCompletion> CompleteAsync(
        string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default);
}

/// <param name="Summary">Uma linha, em português, do que a ferramenta fez (para mostrar ao utilizador).</param>
public sealed record ChatToolOutput(string Content, bool IsError, string Summary);

/// <summary>
/// As ferramentas de uma conversa, ligadas a uma empresa e a um utilizador: só leem, com os direitos dele,
/// e o resultado volta ao modelo como dados, nunca como instruções.
/// </summary>
public interface IChatToolHost
{
    IReadOnlyList<ChatToolSpec> Tools { get; }

    Task<ChatToolOutput> InvokeAsync(string name, string inputJson, CancellationToken cancellationToken = default);
}

public sealed record ChatToolUsed(string Name, string Summary, bool IsError);

/// <param name="HitLimit">O ciclo parou por ter pedido ferramentas a mais, sem chegar a uma resposta final.</param>
public sealed record ChatAnswer(string Text, IReadOnlyList<ChatToolUsed> ToolsUsed, string Model, int PromptTokens, int OutputTokens, bool HitLimit);

/// <summary>
/// O ciclo da conversa (F4F-04): o modelo responde ou pede ferramentas; cada pedido executa-se (só leitura) e o
/// resultado volta ao modelo, até haver uma resposta ou o limite de rondas. Os pedidos de ferramentas de uma
/// mesma ronda executam-se por ordem e todos os resultados voltam numa só mensagem, como a API exige.
/// </summary>
public sealed class DataChatAgent(IChatModelClient model)
{
    /// <summary>O máximo de rondas de ferramentas por pergunta.</summary>
    public const int MaxRounds = 6;

    /// <param name="history">As mensagens anteriores da conversa, só com o texto (o raciocínio e as ferramentas de perguntas antigas não se repetem).</param>
    public async Task<ChatAnswer> AnswerAsync(
        string system, IReadOnlyList<ChatTurnMessage> history, string question, IChatToolHost tools, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(tools);

        var messages = new List<ChatTurnMessage>(history) { new("user", [new ChatBlock.Text(question)]) };
        var used = new List<ChatToolUsed>();
        var promptTokens = 0;
        var outputTokens = 0;
        for (var round = 0; round <= MaxRounds; round++)
        {
            var completion = await model.CompleteAsync(system, messages, tools.Tools, cancellationToken).ConfigureAwait(false);
            promptTokens += completion.PromptTokens;
            outputTokens += completion.OutputTokens;
            var requests = completion.Blocks.OfType<ChatBlock.ToolUse>().ToList();
            var text = string.Concat(completion.Blocks.OfType<ChatBlock.Text>().Select(block => block.Value)).Trim();
            if (requests.Count == 0 || completion.StopReason != "tool_use")
            {
                return new ChatAnswer(
                    text.Length > 0 ? text : "A IA não devolveu texto.", used, model.Model, promptTokens, outputTokens, HitLimit: false);
            }

            if (round == MaxRounds)
            {
                break;
            }

            // A resposta do modelo (com o raciocínio) volta inteira; depois, um resultado por cada pedido.
            messages.Add(new ChatTurnMessage("assistant", completion.Blocks));
            var results = new List<ChatBlock>();
            foreach (var request in requests)
            {
                ChatToolOutput output;
                try
                {
                    output = await tools.InvokeAsync(request.Name, request.InputJson, cancellationToken).ConfigureAwait(false);
                }
                catch (ArgumentException exception)
                {
                    output = new ChatToolOutput(exception.Message, IsError: true, $"{request.Name}: pedido inválido");
                }

                used.Add(new ChatToolUsed(request.Name, output.Summary, output.IsError));
                results.Add(new ChatBlock.ToolResult(request.Id, output.Content, output.IsError));
            }

            messages.Add(new ChatTurnMessage("user", results));
        }

        return new ChatAnswer(
            "Não consegui chegar a uma resposta com os dados que pedi. Reformule a pergunta de forma mais concreta (período, conta ou terceiro).",
            used, model.Model, promptTokens, outputTokens, HitLimit: true);
    }
}
