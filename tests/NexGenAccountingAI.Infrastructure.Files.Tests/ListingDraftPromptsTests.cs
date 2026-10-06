using System.Text.Json;
using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Infrastructure.Ai;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

/// <summary>A criação de listagens a pedir em português (F4D-05): o enunciado e a leitura da resposta do modelo.</summary>
[TestClass]
public sealed class ListingDraftPromptsTests
{
    [TestMethod]
    public void TheSystemPromptCarriesTheCatalogAndNothingFromTheCompany()
    {
        var system = ListingDraftPrompts.System();
        foreach (var source in ListingCatalog.Sources)
        {
            StringAssert.Contains(system, $"\"chave\":\"{source.Key}\"", $"A origem {source.Key} tem de estar no catálogo.");
            foreach (var field in source.Fields)
            {
                StringAssert.Contains(system, $"\"chave\":\"{field.Key}\"");
            }
        }

        StringAssert.Contains(system, "inicio-mes-anterior");
        Assert.IsFalse(system.Contains("{CATALOGO}", StringComparison.Ordinal), "O marcador do catálogo foi substituído.");
        StringAssert.Contains(ListingDraftPrompts.User("vendas por cliente", new DateOnly(2026, 10, 6)), "Hoje é 2026-10-06");
    }

    [TestMethod]
    public void ReadsTheModelAnswerEvenWhenItComesWrappedInText()
    {
        var wrapped = """
            Aqui está:
            ```json
            {"nome": "Vendas por cliente", "explicacao": "Agrupa por cliente.",
             "definition": {"source": "vendas", "columns": ["data", "documento", "total"], "groupBy": ["cliente"],
               "filter": {"op": "and", "conditions": [{"field": "data", "operator": "between", "value": "@inicio", "value2": "@fim"}]},
               "parameters": [{"name": "inicio", "label": "Desde", "type": "date", "default": "inicio-ano"}, {"name": "fim", "label": "Até", "type": "date", "default": "fim-ano"}]}}
            ```
            """;
        var parsed = ListingDraftPrompts.Parse(wrapped);
        Assert.IsNull(parsed.Error);
        Assert.AreEqual("Vendas por cliente", parsed.Name);
        Assert.AreEqual("Agrupa por cliente.", parsed.Explanation);
        Assert.AreEqual("vendas", parsed.Definition!.Source);
        CollectionAssert.AreEqual(new[] { "cliente" }, parsed.Definition.GroupBy!.ToArray());
        Assert.AreEqual(0, ListingCatalog.Validate(parsed.Definition).Count);
    }

    [TestMethod]
    public void RefusesAnswersThatAreNotADefinition()
    {
        Assert.IsNotNull(ListingDraftPrompts.Parse("Não consigo.").Error);
        Assert.IsNotNull(ListingDraftPrompts.Parse(null).Error);
        Assert.IsNotNull(ListingDraftPrompts.Parse("{ não é json }").Error);
        var noDefinition = ListingDraftPrompts.Parse("""{"nome": "X", "explicacao": "Y"}""");
        Assert.IsNull(noDefinition.Definition);
        Assert.IsNotNull(noDefinition.Error);

        // Uma definição que o catálogo recusa lê-se, mas não passa a validação.
        var invented = ListingDraftPrompts.Parse("""{"nome": "X", "definition": {"source": "vendas", "columns": ["campoInventado"]}}""");
        Assert.IsNotNull(invented.Definition);
        Assert.IsTrue(ListingCatalog.Validate(invented.Definition).Count > 0);
        StringAssert.Contains(ListingDraftPrompts.Fix(ListingCatalog.Validate(invented.Definition)), "campoInventado");
    }

    [TestMethod]
    public void EveryGuidedTemplateSurvivesTheAnswerFormat()
    {
        foreach (var template in ListingTemplates.All)
        {
            var answer = JsonSerializer.Serialize(
                new { nome = template.Name, explicacao = template.Description, definition = template.Definition }, ListingJson.Options);
            var parsed = ListingDraftPrompts.Parse(answer);
            Assert.IsNull(parsed.Error, template.Name);
            Assert.AreEqual(0, ListingCatalog.Validate(parsed.Definition).Count, template.Name);
            Assert.AreEqual(ListingJson.Serialize(template.Definition), ListingJson.Serialize(parsed.Definition!), template.Name);
        }
    }

    [TestMethod]
    public async Task WithoutAKeyNothingIsCalled()
    {
        var drafter = new NullListingDrafter();
        Assert.IsFalse(drafter.IsConfigured);
        var draft = await drafter.DraftAsync("vendas", new DateOnly(2026, 10, 6)).ConfigureAwait(false);
        Assert.IsNull(draft.Definition);
        StringAssert.Contains(draft.Explanation, "não está configurada");
    }
}
