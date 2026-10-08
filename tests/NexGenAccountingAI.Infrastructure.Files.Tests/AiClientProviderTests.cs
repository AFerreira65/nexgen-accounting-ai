using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class AiClientProviderTests
{
    private sealed class Source(string? key) : IAiApiKeySource
    {
        public string? Key { get; set; } = key;

        public string? CurrentKey() => Key;
    }

    [TestMethod]
    public void TheGeneralKeyWinsOverTheFileAndTheClientFollowsKeyChanges()
    {
        var general = new Source(null);
        var provider = new AiClientProvider(Options.Create(new AiOptions { ApiKey = "sk-ant-do-ficheiro" }), general);
        Assert.AreEqual("ficheiro", provider.Source);
        var fromFile = provider.Client;
        Assert.IsNotNull(fromFile);
        Assert.AreSame(fromFile, provider.Client, "Com a mesma chave, o mesmo cliente.");

        general.Key = "  sk-ant-geral  ";
        Assert.AreEqual("geral", provider.Source);
        var fromGeneral = provider.Client;
        Assert.IsNotNull(fromGeneral);
        Assert.AreNotSame(fromFile, fromGeneral, "Com outra chave, um cliente novo.");

        general.Key = null;
        Assert.AreEqual("ficheiro", provider.Source);
        var withoutAnything = new AiClientProvider(Options.Create(new AiOptions { ApiKey = null }), general);
        Assert.AreEqual("nenhuma", withoutAnything.Source);
        Assert.IsNull(withoutAnything.Client);
    }

    [TestMethod]
    public async Task TestingWithoutAKeyExplainsWhatIsMissing()
    {
        var provider = new AiClientProvider(Options.Create(new AiOptions { ApiKey = null }), new Source(null));
        StringAssert.Contains((await provider.TestAsync().ConfigureAwait(false))!, "nenhuma chave");
    }

    [TestMethod]
    public void TheStopReasonIsNormalisedWhateverTheSdkQuoting()
    {
        Assert.AreEqual("tool_use", ClaudeChatModelClient.NormalizeStopReason("\"tool_use\""), "O SDK devolve o texto JSON, com aspas.");
        Assert.AreEqual("tool_use", ClaudeChatModelClient.NormalizeStopReason("tool_use"));
        Assert.AreEqual("tool_use", ClaudeChatModelClient.NormalizeStopReason("ToolUse"));
        Assert.AreEqual("end_turn", ClaudeChatModelClient.NormalizeStopReason("\"end_turn\""));
        Assert.AreEqual("max_tokens", ClaudeChatModelClient.NormalizeStopReason("\"max_tokens\""));
        Assert.AreEqual(string.Empty, ClaudeChatModelClient.NormalizeStopReason(null));
    }
}
