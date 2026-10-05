namespace NexGenAccountingAI.Application.Maps;

/// <summary>Um mapa do sistema, pronto a duplicar e alterar.</summary>
/// <param name="ExcludeClosing">O mapa lê-se antes do apuramento de resultados (as classes 6 e 7 ainda com valores).</param>
public sealed record MapTemplate(string Key, string Name, string Description, bool ExcludeClosing, MapDefinition Definition);

/// <summary>
/// Os mapas de partida (F4B-03): a demonstração de resultados e o balanço sintéticos
/// pelo SNC, e um mapa de exploração mensal. São pontos de partida pelas contas do
/// código de contas SNC — cada empresa duplica-os e acerta-os ao seu plano.
/// </summary>
public static class MapTemplates
{
    public const string IncomeStatement = "demonstracao-resultados";
    public const string BalanceSheet = "balanco";
    public const string MonthlyOperations = "exploracao-mensal";

    public static IReadOnlyList<MapTemplate> All { get; } =
    [
        new(IncomeStatement, "Demonstração de resultados por naturezas",
            "As rubricas da DR do SNC a partir das classes 6, 7 e 81; os gastos saem a negativo. Lê-se antes do apuramento.",
            ExcludeClosing: true,
            new MapDefinition(
            [
                Line("L01", "Vendas e serviços prestados", "71 + 72"),
                Line("L02", "Subsídios à exploração", "75"),
                Line("L03", "Ganhos/perdas imputados de subsidiárias, associadas e empreendimentos conjuntos", "785 + 685"),
                Line("L04", "Variação nos inventários da produção", "73"),
                Line("L05", "Trabalhos para a própria entidade", "74"),
                Line("L06", "Custo das mercadorias vendidas e das matérias consumidas", "61"),
                Line("L07", "Fornecimentos e serviços externos", "62"),
                Line("L08", "Gastos com o pessoal", "63"),
                Line("L09", "Imparidade de inventários (perdas/reversões)", "652 + 7622"),
                Line("L10", "Imparidade de dívidas a receber (perdas/reversões)", "651 + 7621"),
                Line("L11", "Provisões (aumentos/reduções)", "67 + 763"),
                Line("L12", "Imparidade de investimentos não depreciáveis/amortizáveis (perdas/reversões)", "653..658 + 7623..7628"),
                Line("L13", "Aumentos/reduções de justo valor", "77 + 66"),
                Line("L14", "Outros rendimentos", "78 - 785"),
                Line("L15", "Outros gastos", "68 - 685"),
                Total("L16", "Resultado antes de depreciações, gastos de financiamento e impostos", "L01 + L02 + L03 + L04 + L05 + L06 + L07 + L08 + L09 + L10 + L11 + L12 + L13 + L14 + L15"),
                Line("L17", "Gastos/reversões de depreciação e de amortização", "64 + 761"),
                Line("L18", "Imparidade de investimentos depreciáveis/amortizáveis (perdas/reversões)", "659 + 7629"),
                Total("L19", "Resultado operacional (antes de gastos de financiamento e impostos)", "L16 + L17 + L18"),
                Line("L20", "Juros e rendimentos similares obtidos", "79"),
                Line("L21", "Juros e gastos similares suportados", "69"),
                Total("L22", "Resultado antes de impostos", "L19 + L20 + L21"),
                Line("L23", "Imposto sobre o rendimento do período", "812"),
                Total("L24", "Resultado líquido do período", "L22 + L23"),
            ],
            MapColumnSets.Find(MapColumnSets.Comparative)!.Columns)),

        new(BalanceSheet, "Balanço sintético",
            "O ativo, o capital próprio e o passivo pelas classes 1 a 5, com o resultado do período calculado das classes 6, 7 e 81. Lê-se antes do apuramento; a separação entre corrente e não corrente é por conta e deve ser acertada ao plano da empresa.",
            ExcludeClosing: true,
            new MapDefinition(
            [
                new("T1", "ATIVO", MapRowKind.Title),
                new("T2", "Ativo não corrente", MapRowKind.Title, Level: 1),
                Asset("A01", "Ativos fixos tangíveis", "43"),
                Asset("A02", "Propriedades de investimento", "42"),
                Asset("A03", "Ativos intangíveis", "44"),
                Asset("A04", "Ativos biológicos", "372"),
                Asset("A05", "Participações financeiras e outros investimentos financeiros", "41"),
                Asset("A06", "Ativos por impostos diferidos", "2741"),
                Total("A10", "Total do ativo não corrente", "A01 + A02 + A03 + A04 + A05 + A06", 1),
                new("T3", "Ativo corrente", MapRowKind.Title, Level: 1),
                Asset("A11", "Inventários", "32 + 33 + 34 + 35 + 36 + 371 + 38 + 39"),
                Asset("A12", "Clientes", "21 - 218"),
                Asset("A13", "Adiantamentos a fornecedores", "228 + 2713"),
                Side("A14", "Estado e outros entes públicos", "24", MapMeasure.DebitBalance),
                Side("A15", "Outras contas a receber", "23 + 26 + 27 - 2713 - 2741 - 2742", MapMeasure.DebitBalance),
                Asset("A16", "Diferimentos", "281"),
                Side("A17", "Outros ativos financeiros", "14", MapMeasure.DebitBalance),
                Asset("A18", "Caixa e depósitos bancários", "11 + 12 + 13"),
                Total("A20", "Total do ativo corrente", "A11 + A12 + A13 + A14 + A15 + A16 + A17 + A18", 1),
                Total("A99", "Total do ativo", "A10 + A20"),
                new("T4", "CAPITAL PRÓPRIO E PASSIVO", MapRowKind.Title),
                new("T5", "Capital próprio", MapRowKind.Title, Level: 1),
                Equity("C01", "Capital subscrito", "51"),
                Equity("C02", "Ações (quotas) próprias", "52"),
                Equity("C03", "Outros instrumentos de capital próprio", "53"),
                Equity("C04", "Prémios de emissão", "54"),
                Equity("C05", "Reservas legais", "551"),
                Equity("C06", "Outras reservas", "55 - 551"),
                Equity("C07", "Resultados transitados", "56"),
                Equity("C08", "Ajustamentos em ativos financeiros", "57"),
                Equity("C09", "Excedentes de revalorização", "58"),
                Equity("C10", "Outras variações no capital próprio", "59"),
                Equity("C11", "Resultado líquido do período", "6 + 7 + 81"),
                Total("C20", "Total do capital próprio", "C01 + C02 + C03 + C04 + C05 + C06 + C07 + C08 + C09 + C10 + C11", 1),
                new("T6", "Passivo não corrente", MapRowKind.Title, Level: 1),
                Equity("P01", "Provisões", "29"),
                Side("P02", "Financiamentos obtidos (não corrente)", "2512 + 2513 + 2514 + 253 + 258", MapMeasure.CreditBalance),
                Equity("P03", "Passivos por impostos diferidos", "2742"),
                Total("P10", "Total do passivo não corrente", "P01 + P02 + P03", 1),
                new("T7", "Passivo corrente", MapRowKind.Title, Level: 1),
                Side("P11", "Fornecedores", "22 - 228", MapMeasure.CreditBalance),
                Equity("P12", "Adiantamentos de clientes", "218"),
                Side("P13", "Estado e outros entes públicos", "24", MapMeasure.CreditBalance),
                Side("P14", "Financiamentos obtidos (corrente)", "25 - 2512 - 2513 - 2514 - 253 - 258", MapMeasure.CreditBalance),
                Side("P15", "Outras contas a pagar", "23 + 26 + 27 - 2713 - 2741 - 2742", MapMeasure.CreditBalance),
                Equity("P16", "Diferimentos", "282"),
                Side("P17", "Outros passivos financeiros", "14", MapMeasure.CreditBalance),
                Total("P20", "Total do passivo corrente", "P11 + P12 + P13 + P14 + P15 + P16 + P17", 1),
                Total("P99", "Total do passivo", "P10 + P20"),
                Total("Z99", "Total do capital próprio e do passivo", "C20 + P99"),
            ],
            [
                new("N", "Exercício", Scope: MapScope.YearToDate),
                new("N1", "Exercício anterior", Scope: MapScope.YearToDate, YearOffset: -1),
            ])),

        new(MonthlyOperations, "Mapa de exploração mensal",
            "Vendas, gastos e resultado mês a mês, com o total do exercício. Lê-se antes do apuramento.",
            ExcludeClosing: true,
            new MapDefinition(
            [
                Line("L01", "Vendas e serviços prestados", "71 + 72"),
                Line("L02", "Outros rendimentos", "73 + 74 + 75 + 77 + 78 + 79"),
                Total("L03", "Rendimentos", "L01 + L02"),
                Line("L11", "Custo das mercadorias e matérias", "61"),
                Line("L12", "Fornecimentos e serviços externos", "62"),
                Line("L13", "Gastos com o pessoal", "63"),
                Line("L14", "Depreciações e amortizações", "64"),
                Line("L15", "Outros gastos", "65 + 66 + 67 + 68 + 69"),
                Total("L16", "Gastos", "L11 + L12 + L13 + L14 + L15"),
                Total("L20", "Resultado antes de impostos", "L03 + L16"),
                new("L21", "Margem sobre os rendimentos", MapRowKind.Formula, Formula: "L20 % L03"),
            ],
            MapColumnSets.Find(MapColumnSets.Monthly)!.Columns)),
    ];

    public static MapTemplate? Find(string? key) =>
        All.FirstOrDefault(template => string.Equals(template.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Uma rubrica de resultados: o saldo invertido, para os rendimentos saírem positivos e os gastos negativos.</summary>
    private static MapRow Line(string code, string label, string accounts) =>
        new(code, label, Accounts: accounts, Sign: MapSign.Inverted, Level: 1);

    private static MapRow Total(string code, string label, string formula, int level = 0) =>
        new(code, label, MapRowKind.Formula, Formula: formula, Level: level, Bold: true);

    /// <summary>Uma rubrica do ativo: o saldo tal como está (devedor positivo).</summary>
    private static MapRow Asset(string code, string label, string accounts) =>
        new(code, label, Accounts: accounts, Level: 2);

    /// <summary>Uma rubrica do capital próprio ou do passivo: o saldo invertido (credor positivo).</summary>
    private static MapRow Equity(string code, string label, string accounts) =>
        new(code, label, Accounts: accounts, Sign: MapSign.Inverted, Level: 2);

    /// <summary>Uma rubrica que só leva as contas com saldo de um dos lados (o Estado está no ativo e no passivo).</summary>
    private static MapRow Side(string code, string label, string accounts, MapMeasure measure) =>
        new(code, label, Accounts: accounts, Measure: measure, Level: 2);
}
