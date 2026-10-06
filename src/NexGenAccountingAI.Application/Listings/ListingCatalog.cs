using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexGenAccountingAI.Application.Listings;

/// <summary>O catálogo das origens de dados que uma listagem pode usar (F4D-01), com nomes em português.</summary>
public static class ListingCatalog
{
    public const string JournalLines = "linhas";

    public static IReadOnlyList<ListingSource> Sources { get; } =
    [
        new(JournalLines, "Linhas de lançamentos",
            "Cada linha de cada lançamento da empresa, com a conta, o terceiro e o centro de custo. Serve de extrato, de razão e de base a quase todas as análises.",
            [
                new("data", "Data", ListingFieldType.Date),
                new("lancamento", "Lançamento", ListingFieldType.Text),
                new("diario", "Diário", ListingFieldType.Text, Groupable: true),
                new("documento", "Documento", ListingFieldType.Text),
                new("estado", "Estado", ListingFieldType.Text, Groupable: true),
                new("conta", "Conta", ListingFieldType.Text, Groupable: true),
                new("nomeConta", "Nome da conta", ListingFieldType.Text, Groupable: true),
                new("classe", "Classe", ListingFieldType.Text, Groupable: true),
                new("terceiro", "Terceiro", ListingFieldType.Text, Groupable: true),
                new("nif", "NIF do terceiro", ListingFieldType.Text),
                new("descricao", "Descrição", ListingFieldType.Text),
                new("centroCusto", "Centro de custo", ListingFieldType.Text, Groupable: true),
                new("ano", "Ano", ListingFieldType.Integer, Groupable: true),
                new("mes", "Mês", ListingFieldType.Integer, Groupable: true),
                new("debito", "Débito", ListingFieldType.Money, Measure: true),
                new("credito", "Crédito", ListingFieldType.Money, Measure: true),
                new("saldo", "Saldo (débito − crédito)", ListingFieldType.Money, Measure: true),
            ]),
    ];

    public static ListingSource? Source(string? key) => Sources.FirstOrDefault(source => string.Equals(source.Key, key, StringComparison.Ordinal));

    /// <summary>Máximos que protegem o servidor de definições desmedidas.</summary>
    public const int MaxColumns = 30, MaxGroupLevels = 3, MaxConditions = 50, MaxDepth = 4, MaxParameters = 10, MaxSort = 5;

    /// <summary>Os problemas da definição, em português; vazio quando está bem.</summary>
    public static IReadOnlyList<string> Validate(ListingDefinition? definition)
    {
        var problems = new List<string>();
        if (definition is null)
        {
            return ["A listagem não tem definição."];
        }

        var source = Source(definition.Source);
        if (source is null)
        {
            return [$"A origem «{definition.Source}» não existe."];
        }

        var columns = definition.Columns ?? [];
        if (columns.Count == 0)
        {
            problems.Add("Escolha pelo menos uma coluna.");
        }

        if (columns.Count > MaxColumns)
        {
            problems.Add($"Há colunas a mais (o máximo é {MaxColumns}).");
        }

        foreach (var key in columns.Distinct(StringComparer.Ordinal))
        {
            if (source.Field(key) is null)
            {
                problems.Add($"A coluna «{key}» não existe na origem «{source.Label}».");
            }
        }

        if (columns.Count != columns.Distinct(StringComparer.Ordinal).Count())
        {
            problems.Add("Há colunas repetidas.");
        }

        var groupBy = definition.GroupBy ?? [];
        if (groupBy.Count > MaxGroupLevels)
        {
            problems.Add($"O agrupamento tem níveis a mais (o máximo é {MaxGroupLevels}).");
        }

        foreach (var key in groupBy)
        {
            if (source.Field(key) is not { Groupable: true })
            {
                problems.Add($"Não se pode agrupar por «{key}».");
            }
        }

        if (groupBy.Count != groupBy.Distinct(StringComparer.Ordinal).Count())
        {
            problems.Add("O agrupamento repete um campo.");
        }

        var sort = definition.Sort ?? [];
        if (sort.Count > MaxSort)
        {
            problems.Add($"A ordenação tem campos a mais (o máximo é {MaxSort}).");
        }

        foreach (var item in sort)
        {
            if (source.Field(item.Field) is not { Sortable: true })
            {
                problems.Add($"Não se pode ordenar por «{item.Field}».");
            }
        }

        var parameters = definition.Parameters ?? [];
        if (parameters.Count > MaxParameters)
        {
            problems.Add($"Há parâmetros a mais (o máximo é {MaxParameters}).");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name) || !parameter.Name.All(c => char.IsAsciiLetterOrDigit(c)))
            {
                problems.Add($"O nome do parâmetro «{parameter.Name}» só pode ter letras e números.");
            }
            else if (!names.Add(parameter.Name))
            {
                problems.Add($"O parâmetro «{parameter.Name}» está repetido.");
            }

            if (parameter.Type is not ("date" or "text" or "number"))
            {
                problems.Add($"O parâmetro «{parameter.Name}» tem um tipo desconhecido.");
            }

            if (parameter.Type == "date" && !string.IsNullOrWhiteSpace(parameter.Default) && ListingParameters.ResolveDate(parameter.Default, new DateOnly(2000, 1, 1)) is null)
            {
                problems.Add($"O valor por omissão do parâmetro «{parameter.Name}» não é uma data.");
            }
        }

        var conditions = 0;
        if (definition.Filter is not null)
        {
            ValidateGroup(definition.Filter, source, names, 1, ref conditions, problems);
        }

        return problems;
    }

    private static void ValidateGroup(
        ListingFilterGroup group, ListingSource source, HashSet<string> parameters, int depth, ref int conditions, List<string> problems)
    {
        if (depth > MaxDepth)
        {
            problems.Add($"Há grupos de filtros dentro de grupos a mais (o máximo é {MaxDepth}).");
            return;
        }

        if (group.Op is not (ListingFilterGroup.And or ListingFilterGroup.Or))
        {
            problems.Add("Um grupo de filtros tem de ser «e» ou «ou».");
        }

        foreach (var condition in group.Conditions ?? [])
        {
            conditions++;
            if (conditions > MaxConditions)
            {
                problems.Add($"Há condições a mais (o máximo é {MaxConditions}).");
                return;
            }

            var field = source.Field(condition.Field);
            if (field is not { Filterable: true })
            {
                problems.Add($"Não se pode filtrar por «{condition.Field}».");
                continue;
            }

            if (!ListingOperators.For(field.Type).Contains(condition.Operator))
            {
                problems.Add($"O operador «{condition.Operator}» não serve ao campo «{field.Label}».");
                continue;
            }

            if (ListingOperators.NeedsNoValue(condition.Operator))
            {
                continue;
            }

            CheckValue(condition.Value, field, parameters, problems);
            if (condition.Operator == ListingOperators.Between)
            {
                CheckValue(condition.Value2, field, parameters, problems);
            }
        }

        foreach (var child in group.Groups ?? [])
        {
            ValidateGroup(child, source, parameters, depth + 1, ref conditions, problems);
        }
    }

    private static void CheckValue(string? value, ListingField field, HashSet<string> parameters, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"Falta o valor da condição sobre «{field.Label}».");
            return;
        }

        if (value.StartsWith('@'))
        {
            if (!parameters.Contains(value[1..]))
            {
                problems.Add($"A condição sobre «{field.Label}» usa o parâmetro «{value}», que não está definido.");
            }

            return;
        }

        var ok = field.Type switch
        {
            ListingFieldType.Date => ListingParameters.ResolveDate(value, new DateOnly(2000, 1, 1)) is not null,
            ListingFieldType.Integer => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            ListingFieldType.Money => TryDecimal(value, out _),
            _ => true,
        };
        if (!ok)
        {
            problems.Add($"O valor «{value}» não serve ao campo «{field.Label}».");
        }
    }

    /// <summary>Lê um número escrito com ponto ou vírgula decimal.</summary>
    public static bool TryDecimal(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}

/// <summary>A definição de uma listagem em JSON, para a guardar e para a API.</summary>
public static class ListingJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ListingDefinition definition) => JsonSerializer.Serialize(definition, Options);

    public static ListingDefinition? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ListingDefinition>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
