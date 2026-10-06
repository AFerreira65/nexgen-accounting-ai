using System.Globalization;

namespace NexGenAccountingAI.Application.Listings;

/// <summary>O tipo de um campo de uma origem de dados.</summary>
public enum ListingFieldType
{
    Text,
    Date,
    Integer,
    Money,
}

/// <summary>Um campo de uma origem de dados (F4D-01): o que se pode mostrar, filtrar, ordenar e agrupar.</summary>
/// <param name="Key">O identificador estável, usado nas definições guardadas.</param>
/// <param name="Groupable">Pode ser nível de agrupamento (só texto e inteiros pequenos).</param>
/// <param name="Measure">É um valor somado nos subtotais (débito, crédito, saldo).</param>
public sealed record ListingField(
    string Key, string Label, ListingFieldType Type, bool Filterable = true, bool Sortable = true, bool Groupable = false, bool Measure = false);

/// <summary>Uma origem de dados pronta, com os nomes em português e os campos que já sabe.</summary>
public sealed record ListingSource(string Key, string Label, string Description, IReadOnlyList<ListingField> Fields)
{
    public ListingField? Field(string key) => Fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.Ordinal));
}

/// <summary>Os operadores de uma condição de filtro.</summary>
public static class ListingOperators
{
    public const string Equal = "eq", NotEqual = "ne", Greater = "gt", GreaterOrEqual = "ge", Less = "lt", LessOrEqual = "le", Between = "between",
        Contains = "contains", NotContains = "notcontains", StartsWith = "starts", Empty = "empty", NotEmpty = "notempty";

    public static IReadOnlyList<string> For(ListingFieldType type) => type switch
    {
        ListingFieldType.Text => [Contains, NotContains, Equal, NotEqual, StartsWith, Empty, NotEmpty],
        _ => [Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Between],
    };

    /// <summary>Operadores que não levam valor.</summary>
    public static bool NeedsNoValue(string op) => op is Empty or NotEmpty;
}

/// <summary>
/// Uma condição: <paramref name="Value"/> é um valor escrito ou uma referência a um parâmetro
/// (<c>@dataInicio</c>); <paramref name="Value2"/> só serve ao operador «entre».
/// </summary>
public sealed record ListingCondition(string Field, string Operator, string? Value = null, string? Value2 = null);

/// <summary>Condições ligadas por «e» ou «ou», que podem conter grupos (parêntesis).</summary>
public sealed record ListingFilterGroup(string Op, IReadOnlyList<ListingCondition>? Conditions = null, IReadOnlyList<ListingFilterGroup>? Groups = null)
{
    public const string And = "and", Or = "or";
}

public sealed record ListingSort(string Field, bool Descending = false);

/// <summary>Um parâmetro pedido ao abrir a listagem, com o valor por omissão (que pode ser relativo).</summary>
/// <param name="Type"><c>date</c>, <c>text</c> ou <c>number</c>.</param>
/// <param name="Default">Um valor, ou para datas uma data relativa (<see cref="ListingParameters.DateTokens"/>).</param>
public sealed record ListingParameter(string Name, string Label, string Type, string? Default = null);

/// <summary>A definição de uma listagem: dados, nunca código.</summary>
public sealed record ListingDefinition(
    string Source,
    IReadOnlyList<string> Columns,
    ListingFilterGroup? Filter = null,
    IReadOnlyList<ListingSort>? Sort = null,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<ListingParameter>? Parameters = null);

/// <summary>O que se pede ao correr: os valores dos parâmetros, o caminho dentro dos grupos e a página.</summary>
/// <param name="GroupPath">Os valores dos grupos já abertos, do primeiro nível para baixo (<c>null</c> é o grupo «sem valor»).</param>
public sealed record ListingRequest(
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyList<string?>? GroupPath = null,
    int Skip = 0,
    int Take = 200);

public sealed record ListingColumn(string Key, string Label, string Type, bool Measure);

/// <summary>Um grupo: o valor da chave, quantas linhas tem e as somas dos valores.</summary>
public sealed record ListingGroup(string? Key, string Label, int Count, IReadOnlyDictionary<string, decimal> Sums);

/// <summary>A ligação de uma linha ao registo de onde veio: o lançamento e o terceiro.</summary>
public sealed record ListingRowLink(string? EntryReference, Guid? PartyId);

public sealed record ListingRow(IReadOnlyList<object?> Cells, ListingRowLink Link);

/// <summary>O resultado: ou grupos (a abrir) ou linhas, e sempre os totais do que está aberto.</summary>
/// <param name="Kind"><c>groups</c> ou <c>rows</c>.</param>
/// <param name="TotalRows">Quantas linhas há no total (para paginar).</param>
public sealed record ListingResult(
    string Kind,
    IReadOnlyList<ListingColumn> Columns,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<ListingGroup> Groups,
    IReadOnlyList<ListingRow> Rows,
    int TotalRows,
    IReadOnlyDictionary<string, decimal> Totals,
    IReadOnlyList<string> Warnings);

/// <summary>Os valores de datas relativas e a resolução dos parâmetros.</summary>
public static class ListingParameters
{
    public static IReadOnlyList<string> DateTokens { get; } =
    [
        "hoje", "inicio-mes", "fim-mes", "inicio-mes-anterior", "fim-mes-anterior", "inicio-ano", "fim-ano", "inicio-ano-anterior", "fim-ano-anterior",
    ];

    /// <summary>Resolve uma data escrita (<c>2026-03-31</c>) ou relativa a <paramref name="today"/>; <c>null</c> se não for nenhuma das duas.</summary>
    public static DateOnly? ResolveDate(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        var previousMonth = today.AddMonths(-1);
        return value switch
        {
            "hoje" => today,
            "inicio-mes" => new DateOnly(today.Year, today.Month, 1),
            "fim-mes" => new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            "inicio-mes-anterior" => new DateOnly(previousMonth.Year, previousMonth.Month, 1),
            "fim-mes-anterior" => new DateOnly(previousMonth.Year, previousMonth.Month, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month)),
            "inicio-ano" => new DateOnly(today.Year, 1, 1),
            "fim-ano" => new DateOnly(today.Year, 12, 31),
            "inicio-ano-anterior" => new DateOnly(today.Year - 1, 1, 1),
            "fim-ano-anterior" => new DateOnly(today.Year - 1, 12, 31),
            _ => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
        };
    }

    /// <summary>Os valores efetivos dos parâmetros: o pedido, ou o valor por omissão da definição.</summary>
    public static IReadOnlyDictionary<string, string> Effective(ListingDefinition definition, IReadOnlyDictionary<string, string>? supplied)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters ?? [])
        {
            if (supplied is not null && supplied.TryGetValue(parameter.Name, out var given) && !string.IsNullOrWhiteSpace(given))
            {
                values[parameter.Name] = given.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(parameter.Default))
            {
                values[parameter.Name] = parameter.Default.Trim();
            }
        }

        return values;
    }
}
