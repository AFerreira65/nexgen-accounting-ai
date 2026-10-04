using NexGenAccountingAI.Application.Reporting;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class AnalysisFieldCatalogTests
{
    private static AnalysisDefinition Def(
        IReadOnlyList<string>? columns = null,
        IReadOnlyList<string>? groupBy = null,
        IReadOnlyList<AnalysisAggregate>? aggregates = null,
        IReadOnlyList<AnalysisFilter>? filters = null) =>
        new(AnalysisSource.Movimentos,
            columns ?? ["accountCode", "amount"],
            filters ?? [],
            groupBy ?? [],
            [],
            aggregates ?? [],
            IncludeGrandTotal: true);

    [TestMethod]
    public void AWellFormedDefinitionRaisesNothing()
    {
        var errors = AnalysisFieldCatalog.Validate(Def(
            columns: ["entryDate", "accountCode", "accountName", "debit", "credit"],
            groupBy: ["accountCode"],
            aggregates: [new("debit", AnalysisAggregateFunction.Sum), new("credit", AnalysisAggregateFunction.Sum)],
            filters: [new("status", "eq", "Conferido"), new("amount", "gte", "100")]));

        Assert.AreEqual(0, errors.Count, string.Join(" / ", errors));
    }

    [TestMethod]
    public void RejectsAnUnknownColumn()
    {
        var errors = AnalysisFieldCatalog.Validate(Def(columns: ["accountCode", "naoExiste"]));
        Assert.IsTrue(errors.Any(message => message.Contains("naoExiste")));
    }

    [TestMethod]
    public void RejectsGroupingByANonGroupableField()
    {
        var errors = AnalysisFieldCatalog.Validate(Def(groupBy: ["debit"]));
        Assert.IsTrue(errors.Any(message => message.Contains("não é agrupável")));
    }

    [TestMethod]
    public void RejectsAggregatingANonAggregatableFieldExceptCount()
    {
        Assert.IsTrue(AnalysisFieldCatalog
            .Validate(Def(aggregates: [new("accountCode", AnalysisAggregateFunction.Sum)]))
            .Any(message => message.Contains("não é agregável")));

        Assert.AreEqual(0, AnalysisFieldCatalog
            .Validate(Def(aggregates: [new("accountCode", AnalysisAggregateFunction.Count)]))
            .Count);
    }

    [TestMethod]
    public void RejectsAnOperatorThatDoesNotApplyToTheFieldType()
    {
        var errors = AnalysisFieldCatalog.Validate(Def(filters: [new("amount", "contains", "1")]));
        Assert.IsTrue(errors.Any(message => message.Contains("não se aplica")));
    }

    [TestMethod]
    public void RequiresAtLeastOneColumn()
    {
        var errors = AnalysisFieldCatalog.Validate(Def(columns: []));
        Assert.IsTrue(errors.Any(message => message.Contains("pelo menos uma coluna")));
    }

    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var definition = Def(
            columns: ["entryDate", "accountCode", "amount"],
            groupBy: ["accountCode"],
            aggregates: [new("amount", AnalysisAggregateFunction.Sum)]);

        var json = AnalysisJson.Serialize(definition);
        StringAssert.Contains(json, "\"source\":\"movimentos\"");
        StringAssert.Contains(json, "\"function\":\"sum\"");

        var back = AnalysisJson.Deserialize(json);
        Assert.IsNotNull(back);
        Assert.AreEqual(AnalysisSource.Movimentos, back!.Source);
        Assert.AreEqual(AnalysisAggregateFunction.Sum, back.Aggregates[0].Function);
    }
}
