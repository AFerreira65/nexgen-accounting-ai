namespace NexGenAccountingAI.Application.Maps;

/// <summary>
/// As colunas de fórmula de um mapa, já lidas, para as calcular linha a linha sem voltar
/// a interpretar o texto (um balancete tem dezenas de milhares de linhas).
/// </summary>
public sealed class MapColumnFormulas
{
    private readonly MapFormula?[] _formulas;
    private readonly Dictionary<string, int> _index;
    private readonly string[] _codes;

    /// <exception cref="FormatException">Uma fórmula de coluna não é válida.</exception>
    public MapColumnFormulas(IReadOnlyList<MapColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _formulas = columns.Select(column => column.Kind == MapColumnKind.Formula ? MapFormula.Parse(column.Formula) : null).ToArray();
        _codes = columns.Select(column => column.Code).ToArray();
        _index = columns.Select((column, index) => (column.Code, index)).ToDictionary(item => item.Code, item => item.index, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Preenche as colunas de fórmula de uma linha a partir das de valores (arredonda ao cêntimo).</summary>
    public void Fill(decimal?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        decimal? ColumnValue(string code)
        {
            var index = _index[code];
            if (values[index] is null && _formulas[index] is { } nested)
            {
                values[index] = nested.Evaluate(ColumnValue) is { } number ? decimal.Round(number, 2) : null;
            }

            return values[index];
        }

        for (var index = 0; index < _formulas.Length; index++)
        {
            if (_formulas[index] is not null)
            {
                values[index] = null;
                values[index] = ColumnValue(_codes[index]);
            }
        }
    }
}
