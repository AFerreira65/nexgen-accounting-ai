using NexGenAccountingAI.Application.Chat;
using NexGenAccountingAI.Application.Listings;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

/// <summary>O ciclo da conversa com a IA (F4F-04): ferramentas, limites e o que volta ao modelo.</summary>
[TestClass]
public sealed class DataChatAgentTests
{
    private sealed class ScriptedModel(params ChatCompletion[] script) : IChatModelClient
    {
        private int next;

        public List<IReadOnlyList<ChatTurnMessage>> Requests { get; } = [];

        public bool IsConfigured => true;

        public string Model => "modelo-de-teste";

        public Task<ChatCompletion> CompleteAsync(
            string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            return Task.FromResult(script[Math.Min(next++, script.Length - 1)]);
        }
    }

    private sealed class FakeTools : IChatToolHost
    {
        public List<(string Name, string Input)> Calls { get; } = [];

        public IReadOnlyList<ChatToolSpec> Tools { get; } = [new("variance", "Compara", "{}", [])];

        public Task<ChatToolOutput> InvokeAsync(string name, string inputJson, CancellationToken cancellationToken = default)
        {
            Calls.Add((name, inputJson));
            return name == "boom"
                ? throw new ArgumentException("Pedido inválido.")
                : Task.FromResult(new ChatToolOutput("{\"resultado\":450}", IsError: false, $"{name}: ok"));
        }
    }

    private static ChatCompletion Text(string text) => new([new ChatBlock.Text(text)], "end_turn", 10, 5);

    private static ChatCompletion Tool(string id, string name, string input = "{}") =>
        new([new ChatBlock.Thinking("a pensar", "assinatura"), new ChatBlock.ToolUse(id, name, input)], "tool_use", 20, 8);

    [TestMethod]
    public async Task AnswersDirectlyWhenNoToolIsNeeded()
    {
        var model = new ScriptedModel(Text("  Olá!  "));
        var answer = await new DataChatAgent(model).AnswerAsync("sistema", [], "Olá?", new FakeTools()).ConfigureAwait(false);
        Assert.AreEqual("Olá!", answer.Text);
        Assert.AreEqual(0, answer.ToolsUsed.Count);
        Assert.IsFalse(answer.HitLimit);
        Assert.AreEqual(10, answer.PromptTokens);
        Assert.AreEqual("modelo-de-teste", answer.Model);
    }

    [TestMethod]
    public async Task RunsTheRequestedToolsAndSendsTheResultsBackWithTheThinking()
    {
        var model = new ScriptedModel(Tool("t1", "variance", "{\"from\":\"2026-09-01\"}"), Text("O resultado foi de 450 €."));
        var tools = new FakeTools();
        var history = new[] { new ChatTurnMessage("user", [new ChatBlock.Text("Antes")]), new ChatTurnMessage("assistant", [new ChatBlock.Text("Resposta antes")]) };
        var answer = await new DataChatAgent(model).AnswerAsync("sistema", history, "Como foi setembro?", tools).ConfigureAwait(false);

        Assert.AreEqual("O resultado foi de 450 €.", answer.Text);
        CollectionAssert.AreEqual(new[] { ("variance", "{\"from\":\"2026-09-01\"}") }, tools.Calls.ToArray());
        Assert.AreEqual("variance: ok", answer.ToolsUsed.Single().Summary);
        Assert.AreEqual(30, answer.PromptTokens, "As duas chamadas somam-se.");
        Assert.AreEqual(13, answer.OutputTokens);

        // A primeira chamada leva a memória e a pergunta; a segunda leva também a resposta do modelo e o resultado.
        Assert.AreEqual(3, model.Requests[0].Count);
        Assert.AreEqual("Como foi setembro?", ((ChatBlock.Text)model.Requests[0][2].Blocks.Single()).Value);
        var second = model.Requests[1];
        Assert.AreEqual(5, second.Count);
        Assert.AreEqual("assistant", second[3].Role);
        Assert.IsTrue(second[3].Blocks.Any(block => block is ChatBlock.Thinking { Signature: "assinatura" }), "O raciocínio volta inalterado.");
        Assert.AreEqual("user", second[4].Role);
        var result = (ChatBlock.ToolResult)second[4].Blocks.Single();
        Assert.AreEqual("t1", result.ToolUseId);
        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public async Task ReturnsOneResultPerRequestInASingleMessageAndTurnsBadRequestsIntoErrors()
    {
        var both = new ChatCompletion([new ChatBlock.ToolUse("a", "variance", "{}"), new ChatBlock.ToolUse("b", "boom", "{}")], "tool_use", 1, 1);
        var model = new ScriptedModel(both, Text("Feito."));
        var answer = await new DataChatAgent(model).AnswerAsync("sistema", [], "Pergunta", new FakeTools()).ConfigureAwait(false);
        var results = model.Requests[1][^1].Blocks.OfType<ChatBlock.ToolResult>().ToList();
        CollectionAssert.AreEqual(new[] { "a", "b" }, results.Select(item => item.ToolUseId).ToArray());
        Assert.IsFalse(results[0].IsError);
        Assert.IsTrue(results[1].IsError, "O pedido inválido volta como erro para a IA o corrigir.");
        Assert.AreEqual("Pedido inválido.", results[1].Content);
        CollectionAssert.AreEqual(new[] { false, true }, answer.ToolsUsed.Select(item => item.IsError).ToArray());
    }

    [TestMethod]
    public async Task StopsAfterTheMaximumNumberOfRounds()
    {
        var model = new ScriptedModel(Tool("loop", "variance"));
        var tools = new FakeTools();
        var answer = await new DataChatAgent(model).AnswerAsync("sistema", [], "Pergunta sem fim", tools).ConfigureAwait(false);
        Assert.IsTrue(answer.HitLimit);
        Assert.AreEqual(DataChatAgent.MaxRounds, tools.Calls.Count);
        StringAssert.Contains(answer.Text, "Reformule");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new DataChatAgent(model).AnswerAsync("sistema", [], "  ", tools)).ConfigureAwait(false);
    }

    [TestMethod]
    public void TheSystemPromptCarriesTheCatalogTheCompanyAndTheDataRule()
    {
        var system = DataChatPrompts.System("Vidreira, Lda.", new DateOnly(2026, 10, 7));
        StringAssert.Contains(system, "Vidreira, Lda.");
        StringAssert.Contains(system, "2026-10-07");
        StringAssert.Contains(system, "\"chave\":\"vendas\"");
        StringAssert.Contains(system, "ignora-as");
        Assert.IsFalse(system.Contains("{CATALOGO}", StringComparison.Ordinal) || system.Contains("{EMPRESA}", StringComparison.Ordinal));
        Assert.AreEqual(ListingCatalogPrompt.Json(), ListingCatalogPrompt.Json());
    }
}
