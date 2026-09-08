namespace AccountingAI.Application.Reporting;

/// <summary>
/// O catálogo de campos de cada origem — a superfície de contrato das análises.
/// É definido em código; o utilizador nunca escreve SQL. O <c>AnalysisRunner</c>
/// traduz cada chave para a consulta.
/// </summary>
public static class AnalysisFieldCatalog
{
    public static readonly IReadOnlyList<AnalysisSource> Sources = [AnalysisSource.Movimentos];

    private static readonly IReadOnlyList<AnalysisField> MovementFields =
    [
        new("entryDate", "Data", AnalysisFieldType.Date, Groupable: true, Aggregatable: false),
        new("year", "Ano", AnalysisFieldType.Number, Groupable: true, Aggregatable: false),
        new("month", "Mês", AnalysisFieldType.Number, Groupable: true, Aggregatable: false),
        new("journalCode", "Diário", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("journalName", "Nome do diário", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("reference", "Referência", AnalysisFieldType.Text, Groupable: false, Aggregatable: false),
        new("documentType", "Tipo de documento", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("documentReference", "Nº de documento", AnalysisFieldType.Text, Groupable: false, Aggregatable: false),
        new("description", "Descrição", AnalysisFieldType.Text, Groupable: false, Aggregatable: false),
        new("status", "Estado", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("source", "Origem", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("accountCode", "Conta", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("accountName", "Nome da conta", AnalysisFieldType.Text, Groupable: true, Aggregatable: false),
        new("lineDescription", "Descrição da linha", AnalysisFieldType.Text, Groupable: false, Aggregatable: false),
        new("debit", "Débito", AnalysisFieldType.Money, Groupable: false, Aggregatable: true),
        new("credit", "Crédito", AnalysisFieldType.Money, Groupable: false, Aggregatable: true),
        new("amount", "Valor (débito − crédito)", AnalysisFieldType.Money, Groupable: false, Aggregatable: true),
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OperatorsByType =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["Text"] = ["eq", "neq", "contains", "startsWith"],
            ["Number"] = ["eq", "neq", "gt", "gte", "lt", "lte"],
            ["Money"] = ["eq", "neq", "gt", "gte", "lt", "lte"],
            ["Date"] = ["eq", "gte", "lte"],
        };

    public static IReadOnlyList<AnalysisField> Fields(AnalysisSource source) => source switch
    {
        AnalysisSource.Movimentos => MovementFields,
        _ => [],
    };

    public static AnalysisField? Field(AnalysisSource source, string key) =>
        Fields(source).FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> OperatorsFor(AnalysisFieldType type) =>
        OperatorsByType.GetValueOrDefault(type.ToString(), []);

    /// <summary>Valida uma definição contra o catálogo; devolve as mensagens de erro (pt-PT), vazio se está bem.</summary>
    public static IReadOnlyList<string> Validate(AnalysisDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();
        var fields = Fields(definition.Source);
        if (fields.Count == 0)
        {
            errors.Add($"Origem desconhecida: {definition.Source}.");
            return errors;
        }

        if (definition.Columns.Count == 0)
        {
            errors.Add("Escolha pelo menos uma coluna.");
        }

        foreach (var key in definition.Columns.Concat(definition.GroupBy).Concat(definition.Sort.Select(s => s.Field)).Concat(definition.Aggregates.Select(a => a.Field)))
        {
            if (Field(definition.Source, key) is null)
            {
                errors.Add($"Campo desconhecido: «{key}».");
            }
        }

        foreach (var key in definition.GroupBy)
        {
            if (Field(definition.Source, key) is { Groupable: false })
            {
                errors.Add($"O campo «{key}» não é agrupável.");
            }
        }

        foreach (var aggregate in definition.Aggregates)
        {
            if (Field(definition.Source, aggregate.Field) is { Aggregatable: false } && aggregate.Function != AnalysisAggregateFunction.Count)
            {
                errors.Add($"O campo «{aggregate.Field}» não é agregável.");
            }
        }

        foreach (var filter in definition.Filters)
        {
            var field = Field(definition.Source, filter.Field);
            if (field is null)
            {
                errors.Add($"Campo de filtro desconhecido: «{filter.Field}».");
                continue;
            }

            if (!OperatorsFor(field.Type).Contains(filter.Operator, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"O operador «{filter.Operator}» não se aplica ao campo «{filter.Field}».");
            }
        }

        return errors;
    }
}
