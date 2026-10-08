using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Domain.Reporting;
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

        public string? ModelOverride(AiFeature feature) => feature == AiFeature.Report ? ReportModel : null;

        public string? ReportModel { get; set; }
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

    [TestMethod]
    public void TheModelFollowsTheProducerThenTheFileThenTheFunctionDefault()
    {
        var general = new Source("sk-ant-x");
        var provider = new AiClientProvider(Options.Create(new AiOptions()), general);
        Assert.AreEqual(AiModels.Haiku, provider.ModelFor(AiFeature.Chat), "O de origem das conversas é o pequeno.");
        Assert.AreEqual(AiModels.Sonnet, provider.ModelFor(AiFeature.Report), "O de origem dos relatórios é o intermédio.");

        general.ReportModel = AiModels.Opus;
        Assert.AreEqual(AiModels.Opus, provider.ModelFor(AiFeature.Report), "O produtor escolhe.");
        Assert.AreEqual(AiModels.Haiku, provider.ModelFor(AiFeature.Variance));

        var fileModel = new AiClientProvider(Options.Create(new AiOptions { Model = "claude-sonnet-5-5" }), new Source("sk-ant-x"));
        Assert.AreEqual("claude-sonnet-5-5", fileModel.ModelFor(AiFeature.Chat), "Um modelo único no ficheiro vale para todas as funções.");
    }
}
