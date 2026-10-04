using System.Text.Json.Serialization;

namespace NexGenAccountingAI.Application.Fiscal;

/// <summary>De onde vem um valor da tabela fiscal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FiscalSourceStatus>))]
public enum FiscalSourceStatus
{
    /// <summary>Lido no texto do código publicado pela AT.</summary>
    Oficial = 0,

    /// <summary>Indicado por fontes secundárias; por confirmar no texto legal.</summary>
    Secundaria = 1,
}

/// <summary>A fonte de um grupo de valores e o grau de confiança.</summary>
public sealed record FiscalSource(string Reference, FiscalSourceStatus Status);

/// <summary>
/// As taxas de IRC de um ano: a geral e a reduzida das PME (e Small Mid Cap)
/// sobre a primeira parcela da matéria coletável.
/// </summary>
public sealed record IrcRates(decimal GeneralRate, decimal SmeRate, decimal SmeThreshold, FiscalSource Source);

/// <summary>Um escalão da derrama estadual: a parcela do lucro tributável e a taxa.</summary>
public sealed record StateSurtaxBracket(decimal From, decimal? To, decimal Rate);

/// <summary>As taxas de IVA de uma região.</summary>
public sealed record VatRegionRates(string Region, decimal Reduced, decimal Intermediate, decimal Normal, FiscalSource Source);

/// <summary>Uma taxa de tributação autónoma.</summary>
public sealed record AutonomousTaxRate(string Key, string Description, decimal Rate);

/// <summary>
/// A tabela fiscal de um ano. <see cref="CarriedForward"/> indica que o ano não
/// está no catálogo e os valores são os do último ano conhecido.
/// </summary>
public sealed record FiscalTable(
    int Year,
    bool CarriedForward,
    IrcRates Irc,
    IReadOnlyList<StateSurtaxBracket> StateSurtax,
    FiscalSource StateSurtaxSource,
    IReadOnlyList<VatRegionRates> Vat,
    IReadOnlyList<AutonomousTaxRate> AutonomousTaxation,
    FiscalSource AutonomousTaxationSource,
    string Disclaimer)
{
    /// <summary>
    /// A coleta de IRC de uma matéria coletável: para uma PME, a taxa reduzida
    /// até ao limite e a taxa geral no excedente. Sem derramas nem tributações
    /// autónomas. Matéria coletável nula ou negativa dá zero.
    /// </summary>
    public decimal IrcOn(decimal taxableIncome, bool smallOrMediumEnterprise)
    {
        if (taxableIncome <= 0)
        {
            return 0m;
        }

        var reducedPart = smallOrMediumEnterprise ? Math.Min(taxableIncome, Irc.SmeThreshold) : 0m;
        var tax = (reducedPart * Irc.SmeRate) + ((taxableIncome - reducedPart) * Irc.GeneralRate);
        return decimal.Round(tax, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>A derrama estadual de um lucro tributável, aplicada por parcelas.</summary>
    public decimal StateSurtaxOn(decimal taxableProfit)
    {
        var total = 0m;
        foreach (var bracket in StateSurtax)
        {
            if (taxableProfit <= bracket.From)
            {
                continue;
            }

            var top = bracket.To is { } limit ? Math.Min(taxableProfit, limit) : taxableProfit;
            total += (top - bracket.From) * bracket.Rate;
        }

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Catálogo, em código, dos valores fiscais que mudam de ano para ano e que a
/// aplicação precisa de <b>propor</b> — nunca de impor: cada valor continua
/// editável no ponto em que é usado. Cada grupo traz a fonte e se foi lido no
/// texto oficial ou vem de fonte secundária. A derrama municipal (por município)
/// e a dedutibilidade do IVA não estão aqui.
/// </summary>
public static class FiscalTableCatalog
{
    public const int FirstYear = 2024;

    public const int LastYear = 2028;

    public const string Disclaimer =
        "Valores de apoio, propostos pela aplicação e sempre editáveis onde são usados. " +
        "Não substituem a consulta da lei em vigor nem cobrem regimes especiais, benefícios fiscais ou taxas regionais de IRC.";

    private static readonly FiscalSource IrcOfficial = new(
        "CIRC, artigo 87.º, n.os 1 e 2, na redação da Lei n.º 64/2025, de 7 de novembro, e respetiva disposição transitória " +
        "(texto publicado no Portal das Finanças, consultado em 2026-10-03).",
        FiscalSourceStatus.Oficial);

    private static readonly FiscalSource IrcSecondary = new(
        "Fontes secundárias (Lei n.º 45-A/2024 e leis do orçamento anteriores); por confirmar no texto legal.",
        FiscalSourceStatus.Secundaria);

    private static readonly FiscalSource StateSurtaxOfficial = new(
        "CIRC, artigo 87.º-A, na redação da Lei n.º 114/2017, de 29 de dezembro (texto publicado no Portal das Finanças, consultado em 2026-10-03).",
        FiscalSourceStatus.Oficial);

    private static readonly FiscalSource AutonomousOfficial = new(
        "CIRC, artigo 88.º, n.os 1, 2, 3, 7, 9, 14 e 18 (texto publicado no Portal das Finanças, consultado em 2026-10-03). " +
        "Não inclui reduções transitórias nem os restantes números do artigo.",
        FiscalSourceStatus.Oficial);

    private static readonly FiscalSource AutonomousNotCovered = new(
        "Não incluído: as taxas e os escalões das viaturas eram outros antes de 2025 e não foram confirmados.",
        FiscalSourceStatus.Secundaria);

    private const int AutonomousTaxationFirstYear = 2025;

    private static readonly FiscalSource VatMainlandOfficial = new(
        "CIVA, artigo 18.º, n.º 1 (texto publicado no Portal das Finanças, consultado em 2026-10-03).",
        FiscalSourceStatus.Oficial);

    private static readonly FiscalSource VatRegionsSecondary = new(
        "Taxas fixadas pelas Assembleias Legislativas Regionais (CIVA, artigo 18.º, n.º 3); valores indicados por fontes secundárias.",
        FiscalSourceStatus.Secundaria);

    private static readonly StateSurtaxBracket[] StateSurtax =
    [
        new(1_500_000m, 7_500_000m, 0.03m),
        new(7_500_000m, 35_000_000m, 0.05m),
        new(35_000_000m, null, 0.09m),
    ];

    private static readonly AutonomousTaxRate[] AutonomousTaxation =
    [
        new("despesas-nao-documentadas", "Despesas não documentadas", 0.50m),
        new("despesas-nao-documentadas-isentos", "Despesas não documentadas — sujeitos passivos isentos", 0.70m),
        new("viaturas-ate-37500", "Encargos com viaturas ligeiras — custo de aquisição inferior a 37 500 €", 0.08m),
        new("viaturas-37500-45000", "Encargos com viaturas ligeiras — de 37 500 € a menos de 45 000 €", 0.25m),
        new("viaturas-45000-ou-mais", "Encargos com viaturas ligeiras — 45 000 € ou mais", 0.32m),
        new("viaturas-hibridas-plug-in-1", "Híbridas plug-in e GNV — 1.º escalão", 0.025m),
        new("viaturas-hibridas-plug-in-2", "Híbridas plug-in e GNV — 2.º escalão", 0.075m),
        new("viaturas-hibridas-plug-in-3", "Híbridas plug-in e GNV — 3.º escalão", 0.15m),
        new("despesas-representacao", "Despesas de representação", 0.10m),
        new("ajudas-de-custo", "Ajudas de custo e deslocação em viatura própria", 0.05m),
        new("agravamento-prejuizo-fiscal", "Agravamento com prejuízo fiscal (pontos percentuais)", 0.10m),
    ];

    private static readonly Dictionary<int, IrcRates> IrcByYear = new()
    {
        [2024] = new(0.21m, 0.17m, 50_000m, IrcSecondary),
        [2025] = new(0.20m, 0.16m, 50_000m, IrcSecondary),
        [2026] = new(0.19m, 0.15m, 50_000m, IrcOfficial),
        [2027] = new(0.18m, 0.15m, 50_000m, IrcOfficial),
        [2028] = new(0.17m, 0.15m, 50_000m, IrcOfficial),
    };

    /// <summary>
    /// A tabela de um ano. Antes de <see cref="FirstYear"/> devolve <c>null</c>
    /// (o catálogo não cobre esses anos); depois de <see cref="LastYear"/>
    /// devolve os valores do último ano, assinalados como transportados.
    /// </summary>
    public static FiscalTable? For(int year)
    {
        if (year < FirstYear)
        {
            return null;
        }

        var known = Math.Min(year, LastYear);
        return new FiscalTable(
            year,
            year > LastYear,
            IrcByYear[known],
            StateSurtax,
            StateSurtaxOfficial,
            [
                new("Continente", 0.06m, 0.13m, 0.23m, VatMainlandOfficial),
                new("Acores", 0.04m, 0.09m, 0.16m, VatRegionsSecondary),
                new("Madeira", 0.04m, 0.12m, 0.22m, VatRegionsSecondary),
            ],
            // As taxas e os escalões das viaturas mudaram em 2025: o texto consultado
            // não serve para os anos anteriores, que ficam sem tabela.
            known >= AutonomousTaxationFirstYear ? AutonomousTaxation : [],
            known >= AutonomousTaxationFirstYear ? AutonomousOfficial : AutonomousNotCovered,
            Disclaimer);
    }
}
