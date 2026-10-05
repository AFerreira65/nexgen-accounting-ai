namespace NexGenAccountingAI.Application.Maps;

/// <summary>Um conjunto de colunas pronto a usar num mapa.</summary>
public sealed record MapColumnSet(string Key, string Label, string Description, IReadOnlyList<MapColumn> Columns);

/// <summary>
/// Os conjuntos de colunas que o desenhador oferece: quem desenha um mapa escolhe um e,
/// se quiser, altera-o. São os arranjos mais pedidos — o do período, o balancete
/// clássico, o mês contra o acumulado, o comparativo com o ano anterior e o mensal.
/// </summary>
public static class MapColumnSets
{
    private static readonly string[] MonthLabels =
        ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];

    public const string Period = "periodo";
    public const string Classic = "classico";
    public const string MonthAndYearToDate = "mes-acumulado";
    public const string Comparative = "comparativo";
    public const string Monthly = "mensal";

    public static IReadOnlyList<MapColumnSet> All { get; } =
    [
        new(Period, "Período", "Uma coluna com o valor do período pedido.",
        [
            new("P", "Período"),
        ]),
        new(Classic, "Balancete clássico", "Saldo inicial, movimento a débito e a crédito, e saldo final devedor e credor.",
        [
            new("SI", "Saldo inicial", Scope: MapScope.Opening, Measure: MapMeasure.Balance),
            new("D", "Débito", Measure: MapMeasure.Debit),
            new("C", "Crédito", Measure: MapMeasure.Credit),
            new("SD", "Saldo devedor", Scope: MapScope.YearToDate, Measure: MapMeasure.DebitBalance),
            new("SC", "Saldo credor", Scope: MapScope.YearToDate, Measure: MapMeasure.CreditBalance),
        ]),
        new(MonthAndYearToDate, "Período e acumulado", "O período pedido ao lado do acumulado desde o início do exercício.",
        [
            new("P", "Período"),
            new("A", "Acumulado", Scope: MapScope.YearToDate),
        ]),
        new(Comparative, "Comparativo com o ano anterior", "O acumulado do exercício, o do anterior, a variação e a variação em percentagem.",
        [
            new("N", "Exercício", Scope: MapScope.YearToDate),
            new("N1", "Exercício anterior", Scope: MapScope.YearToDate, YearOffset: -1),
            new("V", "Variação", MapColumnKind.Formula, Formula: "N - N1"),
            new("VP", "Variação %", MapColumnKind.Formula, Formula: "(N - N1) % N1", Percent: true),
        ]),
        new(Monthly, "Mensal", "Os doze meses do exercício e o total.",
        [
            .. Enumerable.Range(1, 12).Select(month => new MapColumn($"M{month}", MonthLabels[month - 1], Scope: MapScope.Month, Month: month)),
            new("T", "Total", MapColumnKind.Formula, Formula: string.Join(" + ", Enumerable.Range(1, 12).Select(month => $"M{month}"))),
        ]),
    ];

    public static MapColumnSet? Find(string? key) =>
        All.FirstOrDefault(set => string.Equals(set.Key, key, StringComparison.OrdinalIgnoreCase));
}
