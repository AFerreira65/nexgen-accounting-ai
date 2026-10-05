using NexGenAccountingAI.Application.Maps;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class MapEngineTests
{
    /// <summary>Uma conta com valores num exercício: (lugar, débito, crédito), lugar 0 = abertura, 13 = apuramento.</summary>
    private static MapAccount Account(string code, int year, params (int Slot, decimal Debit, decimal Credit)[] amounts) =>
        new(code, $"Conta {code}", new Dictionary<int, MapYearAmounts> { [year] = Year(amounts) });

    private static MapYearAmounts Year(params (int Slot, decimal Debit, decimal Credit)[] amounts)
    {
        var year = MapYearAmounts.Empty();
        foreach (var (slot, debit, credit) in amounts)
        {
            year.Debit[slot] += debit;
            year.Credit[slot] += credit;
        }

        return year;
    }

    private static readonly IReadOnlyList<MapAccount> Books =
    [
        Account("111", 2026, (0, 1000m, 0m), (2, 500m, 0m), (3, 0m, 200m)),
        Account("2111", 2026, (2, 615m, 0m), (3, 0m, 615m)),
        Account("2112", 2026, (3, 0m, 100m)),
        Account("511", 2026, (0, 0m, 1000m)),
        Account("6221", 2026, (3, 200m, 0m), (13, 0m, 200m)),
        Account("6222", 2026, (1, 50m, 0m)),
        Account("7211", 2026, (2, 0m, 500m), (13, 500m, 0m)),
    ];

    [TestMethod]
    public void SelectorTakesPrefixesRangesAndExclusions()
    {
        var selector = MapAccountSelector.Parse("61..63, 7 - 6221 - 72");
        Assert.IsTrue(selector.Matches("611"));
        Assert.IsTrue(selector.Matches("6222"));
        Assert.IsTrue(selector.Matches("63"));
        Assert.IsTrue(selector.Matches("711"));
        Assert.IsFalse(selector.Matches("6221"));
        Assert.IsFalse(selector.Matches("62211"));
        Assert.IsFalse(selector.Matches("7211"));
        Assert.IsFalse(selector.Matches("64"));
        Assert.IsFalse(selector.Matches("6"), "Uma conta mais curta do que o prefixo não é apanhada.");
        Assert.IsTrue(MapAccountSelector.Parse("  ").IsEmpty);
        Assert.ThrowsExactly<FormatException>(() => MapAccountSelector.Parse("61 * 2"));
        Assert.ThrowsExactly<FormatException>(() => MapAccountSelector.Parse("63..61"));
    }

    [TestMethod]
    public void FormulaDoesArithmeticWithPrecedenceAndPercent()
    {
        var values = new Dictionary<string, decimal?> { ["A"] = 10m, ["B"] = 4m, ["Z"] = 0m, ["N"] = null };
        decimal? Run(string text) => MapFormula.Parse(text).Evaluate(code => values[code]);

        Assert.AreEqual(18m, Run("A + B * 2"));
        Assert.AreEqual(28m, Run("(A + B) * 2"));
        Assert.AreEqual(-6m, Run("-A + B"));
        Assert.AreEqual(250m, Run("A % B"));
        Assert.AreEqual(12.5m, Run("A * 1,25"));
        Assert.IsNull(Run("A / Z"), "Dividir por zero dá «sem valor», não rebenta.");
        Assert.IsNull(Run("A + N"));
        CollectionAssert.AreEqual(new[] { "A", "B" }, MapFormula.Parse("A - B").References.ToArray());
        Assert.ThrowsExactly<FormatException>(() => MapFormula.Parse("A +"));
        Assert.ThrowsExactly<FormatException>(() => MapFormula.Parse("(A + B"));
        Assert.ThrowsExactly<FormatException>(() => MapFormula.Parse(""));
    }

    [TestMethod]
    public void RowsSumAccountsAndFormulasBuildOnThem()
    {
        var definition = new MapDefinition(
            [
                new("T1", "Resultados", MapRowKind.Title),
                new("L10", "Vendas e serviços", Accounts: "71 + 72", Sign: MapSign.Inverted, Level: 1),
                new("L20", "Fornecimentos e serviços", Accounts: "62", Level: 1),
                new("L30", "Gastos com pessoal", Accounts: "63", Show: MapShow.IfNotZero, Level: 1),
                new("AUX", "Auxiliar", Accounts: "6222", Hidden: true),
                new("L90", "Resultado", MapRowKind.Formula, Formula: "L10 - L20 - L30", Bold: true),
                new("L95", "Margem %", MapRowKind.Formula, Formula: "L90 % L10"),
                new("L96", "Peso da auxiliar", MapRowKind.Formula, Formula: "AUX % L20"),
            ],
            MapColumnSets.Find(MapColumnSets.MonthAndYearToDate)!.Columns);

        var result = MapEngine.Run(definition, new MapRequest(2026, 2, 3), Books);

        CollectionAssert.AreEqual(
            new[] { "T1", "L10", "L20", "L90", "L95", "L96" },
            result.Rows.Select(row => row.Code).ToArray(),
            "A linha sem valores (63) e a auxiliar não aparecem.");
        decimal? Cell(string row, int column) => result.Rows.Single(item => item.Code == row).Values[column];
        Assert.AreEqual(500m, Cell("L10", 0), "Os rendimentos têm saldo credor: o sinal é trocado.");
        Assert.AreEqual(200m, Cell("L20", 0), "No período (fev–mar) só os 200 de março.");
        Assert.AreEqual(250m, Cell("L20", 1), "No acumulado entram os 50 de janeiro.");
        Assert.AreEqual(300m, Cell("L90", 0));
        Assert.AreEqual(250m, Cell("L90", 1));
        Assert.AreEqual(60m, Cell("L95", 0));
        Assert.AreEqual(20m, Cell("L96", 1), "A linha escondida entra nas fórmulas.");
        Assert.IsNull(Cell("T1", 0));
    }

    [TestMethod]
    public void ClassicColumnsSplitDebitAndCreditBalancesAccountByAccount()
    {
        var definition = new MapDefinition(
            [
                new("L1", "Clientes", Accounts: "21"),
                new("L2", "Caixa", Accounts: "11"),
            ],
            MapColumnSets.Find(MapColumnSets.Classic)!.Columns);

        var result = MapEngine.Run(definition, new MapRequest(2026, 2, 3), Books);
        var clients = result.Rows[0].Values;
        Assert.AreEqual(0m, clients[0]);
        Assert.AreEqual(615m, clients[1]);
        Assert.AreEqual(715m, clients[2]);
        Assert.AreEqual(0m, clients[3], "A 2111 fica saldada.");
        Assert.AreEqual(100m, clients[4], "A 2112 fica credora: não se compensa com as devedoras.");
        var cash = result.Rows[1].Values;
        Assert.AreEqual(1000m, cash[0], "O saldo inicial é a abertura (e os meses antes do período).");
        Assert.AreEqual(1300m, cash[3]);
        Assert.AreEqual(0m, cash[4]);
    }

    [TestMethod]
    public void ClosingEntryOnlyCountsWhenAskedAndOnlyWithDecember()
    {
        var definition = new MapDefinition([new("L1", "Rendimentos", Accounts: "7", Sign: MapSign.Inverted)], [new("A", "Acumulado", Scope: MapScope.YearToDate)]);

        Assert.AreEqual(500m, MapEngine.Run(definition, new MapRequest(2026, 1, 12), Books).Rows[0].Values[0]);
        Assert.AreEqual(0m, MapEngine.Run(definition, new MapRequest(2026, 1, 12, IncludeClosing: true), Books).Rows[0].Values[0]);
        Assert.AreEqual(500m, MapEngine.Run(definition, new MapRequest(2026, 1, 11, IncludeClosing: true), Books).Rows[0].Values[0], "O apuramento é de dezembro.");
    }

    [TestMethod]
    public void ComparativeAndMonthlyColumnsReadOtherYearsAndMonths()
    {
        var accounts = new[]
        {
            new MapAccount("7211", "Serviços", new Dictionary<int, MapYearAmounts>
            {
                [2026] = Year((1, 0m, 100m), (2, 0m, 140m)),
                [2025] = Year((1, 0m, 80m), (2, 0m, 120m), (5, 0m, 999m)),
            }),
        };
        var row = new MapRow[] { new("L1", "Serviços", Accounts: "72", Sign: MapSign.Inverted) };

        var comparative = MapEngine.Run(new MapDefinition(row, MapColumnSets.Find(MapColumnSets.Comparative)!.Columns), new MapRequest(2026, 1, 2), accounts);
        CollectionAssert.AreEqual(new decimal?[] { 240m, 200m, 40m, 20m }, comparative.Rows[0].Values.ToArray(), "O ano anterior compara-se até ao mesmo mês.");
        CollectionAssert.AreEqual(new[] { 2025, 2026 }, MapEngine.YearsNeeded(new MapDefinition(row, MapColumnSets.Find(MapColumnSets.Comparative)!.Columns), new MapRequest(2026, 1, 2)).ToArray());

        var monthly = MapEngine.Run(new MapDefinition(row, MapColumnSets.Find(MapColumnSets.Monthly)!.Columns), new MapRequest(2026, 1, 12), accounts);
        Assert.AreEqual(100m, monthly.Rows[0].Values[0]);
        Assert.AreEqual(140m, monthly.Rows[0].Values[1]);
        Assert.AreEqual(0m, monthly.Rows[0].Values[4]);
        Assert.AreEqual(240m, monthly.Rows[0].Values[12], "A coluna de total soma os doze meses.");
    }

    [TestMethod]
    public void ExplainListsTheAccountsBehindARowAndCoverageFindsGapsAndOverlaps()
    {
        var definition = new MapDefinition(
            [
                new("L1", "Fornecimentos", Accounts: "62"),
                new("L2", "Trabalhos especializados", Accounts: "6221"),
                new("L3", "Rendimentos", Accounts: "7", Sign: MapSign.Inverted),
                new("L4", "Bancos", Accounts: "12"),
                new("L5", "Clientes a receber", Accounts: "21", Measure: MapMeasure.DebitBalance),
                new("L6", "Clientes a pagar", Accounts: "21", Measure: MapMeasure.CreditBalance),
            ],
            [new("A", "Acumulado", Scope: MapScope.YearToDate)]);
        var request = new MapRequest(2026, 1, 12);

        var behind = MapEngine.Explain(definition, request, Books, "L1");
        CollectionAssert.AreEqual(new[] { "6221", "6222" }, behind.Select(item => item.AccountCode).ToArray());
        Assert.AreEqual(200m, behind[0].Values[0]);
        Assert.AreEqual(500m, MapEngine.Explain(definition, request, Books, "L3").Single().Values[0], "Com o sinal da linha.");
        Assert.AreEqual(0, MapEngine.Explain(definition, request, Books, "NAO").Count);

        var (uncovered, repeated) = MapEngine.Coverage(definition, request, Books);
        CollectionAssert.AreEqual(new[] { "111" }, uncovered.ToArray(), "Só nas classes que o mapa toca: a 511 não conta.");
        CollectionAssert.AreEqual(new[] { "6221" }, repeated.ToArray(), "Uma conta a saldo devedor numa linha e a saldo credor noutra não é repetição.");
    }

    [TestMethod]
    public void ValidationExplainsWhatIsWrongInPortuguese()
    {
        var problems = MapEngine.Validate(new MapDefinition(
            [
                new("L1", "Sem contas"),
                new("L1", "Repetida", Accounts: "62"),
                new("2X", "Código inválido", Accounts: "62"),
                new("L4", "Fórmula", MapRowKind.Formula, Formula: "L1 + L99"),
            ],
            [
                new("M", "Mês sem mês", Scope: MapScope.Month),
                new("F", "Fórmula", MapColumnKind.Formula, Formula: "M +"),
            ]));

        Assert.IsTrue(problems.Any(text => text.Contains("L1 não indica contas", StringComparison.Ordinal)), string.Join(" | ", problems));
        Assert.IsTrue(problems.Any(text => text.Contains("mais de uma linha com o código L1", StringComparison.Ordinal)));
        Assert.IsTrue(problems.Any(text => text.Contains("«2X»", StringComparison.Ordinal)));
        Assert.IsTrue(problems.Any(text => text.Contains("L99, que não existe", StringComparison.Ordinal)));
        Assert.IsTrue(problems.Any(text => text.Contains("não diz qual", StringComparison.Ordinal)));
        Assert.IsTrue(problems.Any(text => text.StartsWith("Coluna F:", StringComparison.Ordinal)));

        var cyclic = MapEngine.Validate(new MapDefinition(
            [
                new("A", "A", MapRowKind.Formula, Formula: "B + 1"),
                new("B", "B", MapRowKind.Formula, Formula: "A + 1"),
            ],
            [new("P", "Período")]));
        Assert.IsTrue(cyclic.Single().Contains("em círculo", StringComparison.Ordinal));
        Assert.ThrowsExactly<ArgumentException>(() => MapEngine.Run(new MapDefinition([], []), new MapRequest(2026, 1, 12), Books));
        Assert.ThrowsExactly<ArgumentException>(() => MapEngine.Run(
            new MapDefinition([new("L1", "Caixa", Accounts: "11")], [new("P", "Período")]), new MapRequest(2026, 5, 3), Books));
    }

    [TestMethod]
    public void EveryColumnSetIsValid()
    {
        foreach (var set in MapColumnSets.All)
        {
            var problems = MapEngine.Validate(new MapDefinition([new("L1", "Caixa", Accounts: "11")], set.Columns));
            Assert.AreEqual(0, problems.Count, $"{set.Key}: {string.Join(" | ", problems)}");
        }
    }
}
