namespace NexGenAccountingAI.Application.Maps;

/// <summary>O que uma linha (ou uma coluna) mede em cada conta.</summary>
public enum MapMeasure
{
    /// <summary>Saldo: débito menos crédito.</summary>
    Balance,

    /// <summary>Saldo devedor: o saldo de cada conta quando é devedor (as credoras não contam).</summary>
    DebitBalance,

    /// <summary>Saldo credor: o saldo de cada conta quando é credor, em valor positivo.</summary>
    CreditBalance,

    /// <summary>Movimento a débito.</summary>
    Debit,

    /// <summary>Movimento a crédito.</summary>
    Credit,
}

public enum MapRowKind
{
    /// <summary>Soma de contas, escolhidas por <see cref="MapRow.Accounts"/>.</summary>
    Accounts,

    /// <summary>Calculada a partir de outras linhas (<see cref="MapRow.Formula"/>).</summary>
    Formula,

    /// <summary>Título, sem valores.</summary>
    Title,
}

/// <summary>Como o valor de uma linha é apresentado.</summary>
public enum MapSign
{
    Normal,

    /// <summary>Troca o sinal (as contas de rendimentos e de passivo têm saldo credor).</summary>
    Inverted,

    /// <summary>Valor absoluto.</summary>
    Absolute,

    /// <summary>Se der negativo, passa a zero.</summary>
    ZeroIfNegative,
}

/// <summary>Quando uma linha aparece.</summary>
public enum MapShow
{
    Always,
    IfNotZero,
    IfPositive,
    IfNegative,
}

/// <summary>
/// Uma linha de um mapa. O <see cref="Code"/> identifica-a nas fórmulas das outras
/// (<c>L10 + L20</c>); as contas escolhem-se por prefixo, nunca por lista fixa, para
/// uma conta nova entrar sozinha (ver <see cref="MapAccountSelector"/>).
/// </summary>
/// <param name="Level">Indentação (0 é o nível de topo).</param>
/// <param name="Hidden">Linha auxiliar: entra nas fórmulas mas não é mostrada.</param>
public sealed record MapRow(
    string Code,
    string Label,
    MapRowKind Kind = MapRowKind.Accounts,
    string? Accounts = null,
    string? Formula = null,
    MapMeasure Measure = MapMeasure.Balance,
    MapSign Sign = MapSign.Normal,
    MapShow Show = MapShow.Always,
    int Level = 0,
    bool Bold = false,
    bool Hidden = false);

/// <summary>A que parte do exercício uma coluna diz respeito.</summary>
public enum MapScope
{
    /// <summary>Os meses pedidos (de… a…).</summary>
    Period,

    /// <summary>Desde a abertura do exercício até ao último mês pedido.</summary>
    YearToDate,

    /// <summary>O saldo no início do período pedido: a abertura e os meses anteriores.</summary>
    Opening,

    /// <summary>Um mês fixo (<see cref="MapColumn.Month"/>), seja qual for o período pedido.</summary>
    Month,
}

public enum MapColumnKind
{
    /// <summary>Valores das contas, pelo âmbito e exercício da coluna.</summary>
    Value,

    /// <summary>Calculada a partir de outras colunas (<see cref="MapColumn.Formula"/>).</summary>
    Formula,
}

/// <summary>Uma coluna de um mapa.</summary>
/// <param name="YearOffset">0 para o exercício pedido, −1 para o anterior.</param>
/// <param name="Measure">Se preenchida, substitui a medida das linhas (as colunas «Débito» e «Crédito» de um balancete).</param>
/// <param name="Percent">A coluna é uma percentagem (formatação).</param>
public sealed record MapColumn(
    string Code,
    string Label,
    MapColumnKind Kind = MapColumnKind.Value,
    MapScope Scope = MapScope.Period,
    int YearOffset = 0,
    int? Month = null,
    string? Formula = null,
    MapMeasure? Measure = null,
    bool Percent = false);

/// <summary>Um mapa: o cruzamento de linhas com colunas.</summary>
public sealed record MapDefinition(IReadOnlyList<MapRow> Rows, IReadOnlyList<MapColumn> Columns);

/// <summary>O que se pede ao correr um mapa.</summary>
/// <param name="IncludeClosing">Contar o lançamento de apuramento de resultados (por omissão não, para as classes 6 e 7 se verem).</param>
public sealed record MapRequest(int Year, int FromMonth, int ToMonth, bool IncludeClosing = false);

/// <summary>Os valores de uma conta de movimento num exercício: índice 0 a abertura, 1 a 12 os meses, 13 o apuramento.</summary>
public sealed record MapYearAmounts(decimal[] Debit, decimal[] Credit)
{
    public const int Opening = 0;
    public const int Closing = 13;
    public const int Slots = 14;

    public static MapYearAmounts Empty() => new(new decimal[Slots], new decimal[Slots]);
}

/// <summary>Uma conta de movimento com os seus valores por exercício.</summary>
public sealed record MapAccount(string Code, string Name, IReadOnlyDictionary<int, MapYearAmounts> Years);

public sealed record MapResultRow(
    string Code, string Label, string Kind, int Level, bool Bold, IReadOnlyList<decimal?> Values);

/// <summary>Uma conta que entra numa linha, com o valor em cada coluna de valores.</summary>
public sealed record MapContribution(string AccountCode, string AccountName, IReadOnlyList<decimal?> Values);

public sealed record MapResult(
    IReadOnlyList<MapColumn> Columns,
    IReadOnlyList<MapResultRow> Rows,
    IReadOnlyList<string> Warnings);
