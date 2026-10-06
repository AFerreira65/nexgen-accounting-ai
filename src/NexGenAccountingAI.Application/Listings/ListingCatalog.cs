using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexGenAccountingAI.Application.Listings;

/// <summary>O catálogo das origens de dados que uma listagem pode usar (F4D-01), com nomes em português.</summary>
public static class ListingCatalog
{
    public const string JournalLines = "linhas";

    /// <summary>A coluna que soma os valores das linhas pela ordem da listagem (só existe nas origens com um valor por linha).</summary>
    public const string RunningBalance = "saldoAcumulado";

    /// <summary>Os tons das regras de formatação condicional.</summary>
    public static IReadOnlyList<string> FormatTones { get; } = ["danger", "warning", "success", "muted"];

    public const string Balances = "saldos", Sales = "vendas", Purchases = "compras", Assets = "ativos", Bank = "bancos", Archive = "arquivo";

    private const string Voided = "Os lançamentos anulados ficam de fora, salvo se filtrar pelo estado.";

    public static IReadOnlyList<ListingSource> Sources { get; } =
    [
        new(JournalLines, "Linhas de lançamentos",
            "Cada linha de cada lançamento da empresa, com a conta, o terceiro e o centro de custo. Serve de extrato, de razão e de base a quase todas as análises. " + Voided,
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
                new(RunningBalance, "Saldo acumulado", ListingFieldType.Money, Filterable: false, Sortable: false),
            ]),
        new(Balances, "Saldos por conta e mês",
            "O débito, o crédito e o saldo de cada conta em cada mês, já somados. É a base das evoluções e das comparações entre períodos. " + Voided,
            [
                new("conta", "Conta", ListingFieldType.Text, Groupable: true),
                new("nomeConta", "Nome da conta", ListingFieldType.Text, Groupable: true),
                new("classe", "Classe", ListingFieldType.Text, Groupable: true),
                new("ano", "Ano", ListingFieldType.Integer, Groupable: true),
                new("mes", "Mês", ListingFieldType.Integer, Groupable: true),
                new("debito", "Débito", ListingFieldType.Money, Measure: true),
                new("credito", "Crédito", ListingFieldType.Money, Measure: true),
                new("saldo", "Saldo (débito − crédito)", ListingFieldType.Money, Measure: true),
            ]),
        new(Sales, "Documentos de venda",
            "As faturas e notas importadas do SAF-T de vendas, com o cliente e o lançamento em que ficaram contabilizadas. Os documentos anulados ficam de fora, salvo se filtrar pelo estado.",
            [
                new("data", "Data", ListingFieldType.Date),
                new("documento", "Documento", ListingFieldType.Text),
                new("tipo", "Tipo", ListingFieldType.Text, Groupable: true),
                new("cliente", "Cliente", ListingFieldType.Text, Groupable: true),
                new("nif", "NIF do cliente", ListingFieldType.Text),
                new("pais", "País", ListingFieldType.Text, Groupable: true),
                new("estado", "Estado", ListingFieldType.Text, Groupable: true),
                new("contabilizado", "Contabilizado", ListingFieldType.Text, Groupable: true),
                new("lancamento", "Lançamento", ListingFieldType.Text),
                new("ano", "Ano", ListingFieldType.Integer, Groupable: true),
                new("mes", "Mês", ListingFieldType.Integer, Groupable: true),
                new("liquido", "Valor líquido", ListingFieldType.Money, Measure: true),
                new("iva", "IVA", ListingFieldType.Money, Measure: true),
                new("total", "Total", ListingFieldType.Money, Measure: true),
            ]),
        new(Purchases, "Compras do e-Fatura",
            "As faturas de compra descarregadas do e-Fatura, com o fornecedor e a situação: por lançar, lançada ou dispensada.",
            [
                new("data", "Data", ListingFieldType.Date),
                new("documento", "Documento", ListingFieldType.Text),
                new("tipo", "Tipo", ListingFieldType.Text, Groupable: true),
                new("fornecedor", "Fornecedor", ListingFieldType.Text, Groupable: true),
                new("nif", "NIF do fornecedor", ListingFieldType.Text),
                new("estadoAt", "Estado na AT", ListingFieldType.Text, Groupable: true),
                new("setor", "Setor", ListingFieldType.Text, Groupable: true),
                new("situacao", "Situação", ListingFieldType.Text, Groupable: true),
                new("lancamento", "Lançamento", ListingFieldType.Text),
                new("ano", "Ano", ListingFieldType.Integer, Groupable: true),
                new("mes", "Mês", ListingFieldType.Integer, Groupable: true),
                new("base", "Base tributável", ListingFieldType.Money, Measure: true),
                new("iva", "IVA", ListingFieldType.Money, Measure: true),
                new("total", "Total", ListingFieldType.Money, Measure: true),
            ]),
        new(Assets, "Ativos fixos",
            "O cadastro de ativos fixos, com a conta, as datas de aquisição, de início de uso e de abate, e os valores.",
            [
                new("codigo", "Código", ListingFieldType.Text),
                new("descricao", "Descrição", ListingFieldType.Text),
                new("conta", "Conta do ativo", ListingFieldType.Text, Groupable: true),
                new("aquisicao", "Data de aquisição", ListingFieldType.Date),
                new("inicioUso", "Início de utilização", ListingFieldType.Date),
                new("abate", "Data de abate", ListingFieldType.Date),
                new("estado", "Estado", ListingFieldType.Text, Groupable: true),
                new("ano", "Ano de aquisição", ListingFieldType.Integer, Groupable: true),
                new("valorAquisicao", "Valor de aquisição", ListingFieldType.Money, Measure: true),
                new("valorResidual", "Valor residual", ListingFieldType.Money, Measure: true),
            ]),
        new(Bank, "Movimentos bancários",
            "Os movimentos dos extratos bancários importados, com a conta bancária e se já estão conciliados.",
            [
                new("data", "Data", ListingFieldType.Date),
                new("descricao", "Descrição", ListingFieldType.Text),
                new("referencia", "Referência", ListingFieldType.Text),
                new("conta", "Conta bancária", ListingFieldType.Text, Groupable: true),
                new("conciliado", "Conciliado", ListingFieldType.Text, Groupable: true),
                new("ano", "Ano", ListingFieldType.Integer, Groupable: true),
                new("mes", "Mês", ListingFieldType.Integer, Groupable: true),
                new("valor", "Valor", ListingFieldType.Money, Measure: true),
                new(RunningBalance, "Saldo acumulado", ListingFieldType.Money, Filterable: false, Sortable: false),
            ]),
        new(Archive, "Arquivo digital",
            "Os documentos arquivados, com os dados lidos do código QR e o lançamento a que estão ligados.",
            [
                new("ficheiro", "Ficheiro", ListingFieldType.Text),
                new("tipo", "Tipo de ficheiro", ListingFieldType.Text, Groupable: true),
                new("nifEmissor", "NIF do emissor", ListingFieldType.Text, Groupable: true),
                new("documento", "Documento", ListingFieldType.Text),
                new("dataDocumento", "Data do documento", ListingFieldType.Date),
                new("ligado", "Ligado a lançamento", ListingFieldType.Text, Groupable: true),
                new("lancamento", "Lançamento", ListingFieldType.Text),
                new("notas", "Notas", ListingFieldType.Text),
                new("total", "Total (QR)", ListingFieldType.Money, Measure: true),
                new("iva", "IVA (QR)", ListingFieldType.Money, Measure: true),
            ]),
    ];

    public static ListingSource? Source(string? key) => Sources.FirstOrDefault(source => string.Equals(source.Key, key, StringComparison.Ordinal));

    /// <summary>Máximos que protegem o servidor de definições desmedidas.</summary>
    public const int MaxFormatRules = 10, MaxColumns = 30, MaxGroupLevels = 3, MaxConditions = 50, MaxDepth = 4, MaxParameters = 10, MaxSort = 5;

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

        foreach (var rule in definition.Formatting ?? [])
        {
            var field = source.Field(rule.Column);
            if (field is null || !columns.Contains(rule.Column))
            {
                problems.Add($"A regra de formatação usa a coluna «{rule.Column}», que não está na listagem.");
                continue;
            }

            if (field.Type == ListingFieldType.Text || !ListingOperators.For(field.Type).Contains(rule.Operator) || rule.Operator == ListingOperators.Between)
            {
                problems.Add($"A regra de formatação sobre «{field.Label}» tem um operador que não serve (use igual, diferente, maior ou menor).");
            }
            else if (field.Type == ListingFieldType.Date ? ListingParameters.ResolveDate(rule.Value, new DateOnly(2000, 1, 1)) is null : !TryDecimal(rule.Value, out _))
            {
                problems.Add($"O valor «{rule.Value}» da regra de formatação sobre «{field.Label}» não serve.");
            }

            if (!FormatTones.Contains(rule.Tone))
            {
                problems.Add($"O tom «{rule.Tone}» da regra de formatação não existe.");
            }
        }

        if ((definition.Formatting?.Count ?? 0) > MaxFormatRules)
        {
            problems.Add($"Há regras de formatação a mais (o máximo é {MaxFormatRules}).");
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
