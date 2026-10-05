using NexGenAccountingAI.Application.Maps;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class MapTemplatesTests
{
    [TestMethod]
    public void EveryTemplateIsValidAndSurvivesJson()
    {
        foreach (var template in MapTemplates.All)
        {
            var problems = MapEngine.Validate(template.Definition);
            Assert.AreEqual(0, problems.Count, $"{template.Key}: {string.Join(" | ", problems)}");
            var copy = MapJson.Deserialize(MapJson.Serialize(template.Definition));
            Assert.IsNotNull(copy);
            Assert.AreEqual(template.Definition.Rows.Count, copy.Rows.Count);
            Assert.AreEqual(template.Definition.Columns.Count, copy.Columns.Count);
            Assert.AreEqual(template.Definition.Rows[0], copy.Rows[0], "Os enumerados vão por nome e voltam iguais.");
        }

        Assert.IsNull(MapJson.Deserialize("{ isto não é json"));
        Assert.IsNull(MapTemplates.Find("nao-existe"));
    }

    [TestMethod]
    public void BalanceSheetAndIncomeStatementAgreeOnASmallYear()
    {
        static MapAccount Account(string code, int slot, decimal debit, decimal credit)
        {
            var year = MapYearAmounts.Empty();
            year.Debit[slot] = debit;
            year.Credit[slot] = credit;
            return new MapAccount(code, code, new Dictionary<int, MapYearAmounts> { [2026] = year });
        }

        // Capital 1 000 em caixa; vendas 500 a crédito de clientes, com IVA a pagar 50; FSE 200 pagos pelo banco.
        var accounts = new[]
        {
            Account("111", 0, 1000m, 0m), Account("511", 0, 0m, 1000m),
            Account("2111", 3, 550m, 0m), Account("7211", 3, 0m, 500m), Account("2433", 3, 0m, 50m),
            Account("6221", 4, 200m, 0m), Account("1201", 4, 0m, 200m),
        };
        var request = new MapRequest(2026, 1, 12);

        var income = MapEngine.Run(MapTemplates.Find(MapTemplates.IncomeStatement)!.Definition, request, accounts);
        static decimal? Row(MapResult result, string code) => result.Rows.Single(row => row.Code == code).Values[0];
        Assert.AreEqual(500m, Row(income, "L01"));
        Assert.AreEqual(-200m, Row(income, "L07"), "Os gastos saem a negativo.");
        Assert.AreEqual(300m, Row(income, "L24"));

        var balance = MapEngine.Run(MapTemplates.Find(MapTemplates.BalanceSheet)!.Definition, request, accounts);
        Assert.AreEqual(1350m, Row(balance, "A99"), "Caixa 1 000 - 200 + clientes 550.");
        Assert.AreEqual(300m, Row(balance, "C11"), "O resultado do período vem das classes 6 e 7.");
        Assert.AreEqual(50m, Row(balance, "P13"), "O IVA a pagar está no passivo, não no ativo.");
        Assert.AreEqual(0m, Row(balance, "A14"));
        Assert.AreEqual(Row(balance, "A99"), Row(balance, "Z99"), "O balanço fecha.");
        var (uncovered, repeated) = MapEngine.Coverage(MapTemplates.Find(MapTemplates.BalanceSheet)!.Definition, request, accounts);
        Assert.AreEqual(0, uncovered.Count, string.Join(", ", uncovered));
        Assert.AreEqual(0, repeated.Count, string.Join(", ", repeated));
    }
}
