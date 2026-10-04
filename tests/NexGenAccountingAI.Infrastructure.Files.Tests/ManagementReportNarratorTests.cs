using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class ManagementReportNarratorTests
{
    [TestMethod]
    public void SystemPromptCarriesTheKindTaskAndTheJsonShape()
    {
        var monthly = ManagementReportPrompts.System(new ManagementReportRequest(
            ManagementReportKind.SinteseMensal, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        StringAssert.Contains(monthly, "síntese mensal");
        StringAssert.Contains(monthly, "\"referencias\"");
        StringAssert.Contains(monthly, "Não inventes valores");

        var free = ManagementReportPrompts.System(new ManagementReportRequest(
            null, "Como evoluiu a tesouraria?", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
        StringAssert.Contains(free, "estritamente a partir do dossiê");
    }

    [TestMethod]
    public void UserMessageIncludesTheQuestionAndTheDossierJson()
    {
        var message = ManagementReportPrompts.User(
            new ManagementReportRequest(null, "  E o IVA?  ", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)),
            "{\"companyName\":\"Empresa X\"}");
        StringAssert.Contains(message, "Pergunta do utilizador: E o IVA?");
        StringAssert.Contains(message, "{\"companyName\":\"Empresa X\"}");
    }

    [TestMethod]
    public void MapperReadsAWellFormedResponseAndAppendsTheFixedNotice()
    {
        const string json =
            """
            Aqui está:
            ```json
            {
              "resumo": "Resultado positivo de 800 EUR.",
              "seccoes": [
                { "titulo": "Rendimentos", "corpo": "Subiram 500 EUR face ao homólogo.",
                  "referencias": [ { "tipo": "conta", "codigo": "7111" }, { "tipo": "movimento", "codigo": "C-1" } ] }
              ],
              "alertas": [ "Confirmar o corte de proveitos." ],
              "indicadores": [ { "nome": "Margem líquida", "valor": "53,3%", "comentario": null } ]
            }
            ```
            """;

        var result = ManagementReportNarrationMapper.FromJson(json);

        Assert.AreEqual("Resultado positivo de 800 EUR.", result.Summary);
        Assert.AreEqual(1, result.Sections.Count);
        Assert.AreEqual(2, result.Sections[0].References.Count);
        Assert.AreEqual("conta", result.Sections[0].References[0].Type);
        Assert.AreEqual("7111", result.Sections[0].References[0].Code);
        Assert.AreEqual("movimento", result.Sections[0].References[1].Type);
        Assert.AreEqual(1, result.Alerts.Count);
        Assert.AreEqual("Margem líquida", result.Indicators[0].Name);
        Assert.IsNull(result.Indicators[0].Comment);
        Assert.AreEqual(ManagementReportResult.Notice, result.AiNotice);
    }

    [TestMethod]
    public void MapperDegradesGracefullyOnUnreadableOutput()
    {
        var result = ManagementReportNarrationMapper.FromJson("Desculpa, não consigo responder.");

        Assert.AreEqual(0, result.Sections.Count);
        Assert.AreEqual(1, result.Alerts.Count);
        StringAssert.Contains(result.Alerts[0], "JSON válido");
        Assert.AreEqual(ManagementReportResult.Notice, result.AiNotice);
    }

    [TestMethod]
    public async Task NarratorWithoutAKeyReportsNotConfigured()
    {
        var narrator = new ClaudeManagementReportNarrator(Options.Create(new AiOptions { ApiKey = null }));
        Assert.IsFalse(narrator.IsConfigured);

        var request = new ManagementReportRequest(
            ManagementReportKind.SinteseMensal, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var dossier = new ManagementReportDossier(
            "Empresa X", request.From, request.To, null, null, false,
            [], [], [], [], new DossierCrossCheck(false, 0m, 0m, 0m, 0, 0, true), [], []);

        var narration = await narrator.GenerateAsync(request, dossier, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual("—", narration.Model);
        Assert.AreEqual(0, narration.OutputTokens);
        StringAssert.Contains(narration.Result.Summary, "não estão configurados");

        var nullNarrator = new NullManagementReportNarrator();
        Assert.IsFalse(nullNarrator.IsConfigured);
    }
}
