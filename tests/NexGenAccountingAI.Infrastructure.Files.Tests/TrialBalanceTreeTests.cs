using NexGenAccountingAI.Application.Maps;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class TrialBalanceTreeTests
{
    private static MapAccount Account(string code, params (int Slot, decimal Debit, decimal Credit)[] amounts)
    {
        var year = MapYearAmounts.Empty();
        foreach (var (slot, debit, credit) in amounts)
        {
            year.Debit[slot] += debit;
            year.Credit[slot] += credit;
        }

        return new MapAccount(code, $"Conta {code}", new Dictionary<int, MapYearAmounts> { [2026] = year });
    }

    private static readonly Dictionary<string, string> Chart = new()
    {
        ["21"] = "Clientes",
        ["211"] = "Clientes c/c",
        ["2111"] = "Clientes gerais",
        ["21110001"] = "Cliente A",
        ["21110002"] = "Cliente B",
        ["11"] = "Caixa",
        ["111"] = "Caixa geral",
        ["72"] = "Prestações de serviços",
    };

    private static readonly MapAccount[] Books =
    [
        Account("111", (0, 100m, 0m), (2, 50m, 0m)),
        Account("21110001", (2, 300m, 0m), (3, 0m, 100m)),
        Account("21110002", (2, 0m, 80m)),
        Account("7211", (2, 0m, 270m)),
        Account("6999", (5, 10m, 10m)),
        Account("31", (0, 0m, 0m)),
    ];

    [TestMethod]
    public void AccountsAddUpThroughTheChartToTheirClass()
    {
        var tree = TrialBalanceTree.Build(Books, Chart, MapColumnSets.Find(MapColumnSets.Classic)!.Columns, new MapRequest(2026, 1, 3));

        CollectionAssert.AreEqual(
            new[] { "1", "11", "111", "2", "21", "211", "2111", "21110001", "21110002", "7", "72", "7211" },
            tree.Nodes.Select(node => node.Code).ToArray(),
            "Por ordem de código; a conta sem valores e a que só mexe depois do período ficam de fora.");

        TrialBalanceNode Node(string code) => tree.Nodes.Single(node => node.Code == code);
        Assert.AreEqual("Meios financeiros líquidos", Node("1").Name, "A classe tem nome mesmo sem estar no plano.");
        Assert.AreEqual(1, Node("2").Depth);
        Assert.AreEqual(5, Node("21110001").Depth);
        Assert.AreEqual("2111", Node("21110001").ParentCode);
        Assert.AreEqual("72", Node("7211").ParentCode, "Salta os níveis que o plano não tem.");
        Assert.IsTrue(Node("2111").HasChildren);
        Assert.IsFalse(Node("21110002").HasChildren);
        Assert.IsTrue(Node("21110002").IsPosting);
        Assert.IsFalse(Node("21").IsPosting);

        // Saldo inicial, débito, crédito, saldo devedor, saldo credor.
        CollectionAssert.AreEqual(new decimal?[] { 0m, 300m, 180m, 200m, 80m }, Node("21").Values.ToArray(),
            "Na conta de agregação, os saldos devedores e credores das subcontas não se compensam.");
        CollectionAssert.AreEqual(new decimal?[] { 100m, 50m, 0m, 150m, 0m }, Node("1").Values.ToArray());
        CollectionAssert.AreEqual(new decimal?[] { 100m, 350m, 450m, 350m, 350m }, tree.Totals.ToArray());
        Assert.AreEqual(450m, tree.TotalDebit, "Com a abertura.");
        Assert.AreEqual(450m, tree.TotalCredit);
    }

    [TestMethod]
    public void FormulaColumnsAreWorkedOutOnEveryLevel()
    {
        var accounts = new[]
        {
            new MapAccount("7211", "Serviços A", new Dictionary<int, MapYearAmounts>
            {
                [2026] = new(Slot(1, 0m), Slot(1, 150m)),
                [2025] = new(Slot(1, 0m), Slot(1, 100m)),
            }),
            new MapAccount("7212", "Serviços B", new Dictionary<int, MapYearAmounts>
            {
                [2026] = new(Slot(1, 0m), Slot(1, 50m)),
            }),
        };

        var tree = TrialBalanceTree.Build(accounts, Chart, MapColumnSets.Find(MapColumnSets.Comparative)!.Columns, new MapRequest(2026, 1, 12));
        CollectionAssert.AreEqual(new decimal?[] { -200m, -100m, -100m, 100m }, tree.Nodes.Single(node => node.Code == "72").Values.ToArray());
        CollectionAssert.AreEqual(new decimal?[] { -50m, 0m, -50m, null }, tree.Nodes.Single(node => node.Code == "7212").Values.ToArray(), "Sem ano anterior não há percentagem.");
    }

    private static decimal[] Slot(int slot, decimal value)
    {
        var values = new decimal[MapYearAmounts.Slots];
        values[slot] = value;
        return values;
    }
}
