using System.Text.RegularExpressions;

namespace NexGenAccountingAI.Application.Maps;

/// <summary>
/// O motor de mapas (F4B-01): calcula um mapa de linhas por colunas a partir dos valores
/// das contas de movimento. Só faz contas — não lê a base de dados nem conhece empresas:
/// quem o chama dá-lhe as contas já filtradas pela empresa.
/// <para>
/// Regras: uma linha de contas soma, conta a conta, a medida pedida (por isso «saldo
/// devedor» soma só as contas com saldo devedor, como num balanço); o sinal e a condição
/// de exibição aplicam-se ao resultado; as linhas e as colunas de fórmula calculam-se
/// depois, e podem referir-se umas às outras desde que sem ciclos.
/// </para>
/// </summary>
public static partial class MapEngine
{
    /// <summary>Verifica a definição sem a correr. Devolve os problemas, em português; vazio se estiver certa.</summary>
    public static IReadOnlyList<string> Validate(MapDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var problems = new List<string>();
        if (definition.Rows.Count == 0)
        {
            problems.Add("O mapa não tem linhas.");
        }

        if (definition.Columns.Count == 0)
        {
            problems.Add("O mapa não tem colunas.");
        }

        CheckCodes(definition.Rows.Select(row => row.Code), "linha", problems);
        CheckCodes(definition.Columns.Select(column => column.Code), "coluna", problems);

        var rowCodes = definition.Rows.Select(row => row.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in definition.Rows)
        {
            switch (row.Kind)
            {
                case MapRowKind.Accounts:
                    try
                    {
                        if (MapAccountSelector.Parse(row.Accounts).IsEmpty)
                        {
                            problems.Add($"A linha {row.Code} não indica contas.");
                        }
                    }
                    catch (FormatException exception)
                    {
                        problems.Add($"Linha {row.Code}: {exception.Message}");
                    }

                    break;
                case MapRowKind.Formula:
                    CheckFormula(row.Formula, rowCodes, $"Linha {row.Code}", "linha", problems);
                    break;
            }
        }

        var columnCodes = definition.Columns.Select(column => column.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in definition.Columns)
        {
            if (column.Kind == MapColumnKind.Formula)
            {
                CheckFormula(column.Formula, columnCodes, $"Coluna {column.Code}", "coluna", problems);
            }
            else if (column.Scope == MapScope.Month && column.Month is not (>= 1 and <= 12))
            {
                problems.Add($"A coluna {column.Code} é de um mês fixo mas não diz qual (1 a 12).");
            }
            else if (column.YearOffset is > 0 or < -5)
            {
                problems.Add($"A coluna {column.Code} pede um exercício fora do alcance (do atual até cinco anos antes).");
            }
        }

        if (problems.Count == 0)
        {
            CheckCycles(definition.Rows.Where(row => row.Kind == MapRowKind.Formula).ToDictionary(row => row.Code, row => row.Formula!, StringComparer.OrdinalIgnoreCase), "linhas", problems);
            CheckCycles(definition.Columns.Where(column => column.Kind == MapColumnKind.Formula).ToDictionary(column => column.Code, column => column.Formula!, StringComparer.OrdinalIgnoreCase), "colunas", problems);
        }

        return problems;
    }

    /// <summary>Os exercícios de que o mapa precisa, para quem carrega os dados.</summary>
    public static IReadOnlyList<int> YearsNeeded(MapDefinition definition, MapRequest request) =>
        definition.Columns.Where(column => column.Kind == MapColumnKind.Value).Select(column => request.Year + column.YearOffset).Distinct().Order().ToList();

    /// <exception cref="ArgumentException">A definição ou o pedido não são válidos.</exception>
    public static MapResult Run(MapDefinition definition, MapRequest request, IReadOnlyList<MapAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accounts);
        var problems = Validate(definition);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(definition));
        }

        if (request.FromMonth is < 1 or > 12 || request.ToMonth is < 1 or > 12 || request.ToMonth < request.FromMonth)
        {
            throw new ArgumentException("O período do mapa tem de ser de um mês a outro do mesmo exercício.", nameof(request));
        }

        var columns = definition.Columns;
        var raw = new Dictionary<string, decimal?[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in definition.Rows.Where(row => row.Kind == MapRowKind.Accounts))
        {
            var selector = MapAccountSelector.Parse(row.Accounts);
            var values = new decimal?[columns.Count];
            for (var index = 0; index < columns.Count; index++)
            {
                if (columns[index].Kind != MapColumnKind.Value)
                {
                    continue;
                }

                var total = 0m;
                foreach (var account in accounts)
                {
                    if (selector.Matches(account.Code))
                    {
                        total += Amount(account, columns[index], row.Measure, request);
                    }
                }

                values[index] = ApplySign(total, row.Sign);
            }

            raw[row.Code] = values;
        }

        // Linhas de fórmula, coluna a coluna, pela ordem das dependências (não há ciclos).
        var formulas = definition.Rows.Where(row => row.Kind == MapRowKind.Formula)
            .ToDictionary(row => row.Code, row => (Row: row, Formula: MapFormula.Parse(row.Formula)), StringComparer.OrdinalIgnoreCase);
        decimal? RowValue(string code, int column)
        {
            if (raw.TryGetValue(code, out var known) && (known[column] is not null || !formulas.ContainsKey(code)))
            {
                return known[column];
            }

            if (!formulas.TryGetValue(code, out var formula))
            {
                return null;
            }

            var value = formula.Formula.Evaluate(reference => RowValue(reference, column));
            return value is { } number ? ApplySign(decimal.Round(number, 2), formula.Row.Sign) : null;
        }

        foreach (var (code, _) in formulas)
        {
            var values = new decimal?[columns.Count];
            raw[code] = values;
            for (var index = 0; index < columns.Count; index++)
            {
                if (columns[index].Kind == MapColumnKind.Value)
                {
                    values[index] = RowValue(code, index);
                }
            }
        }

        // Colunas de fórmula: por linha, a partir das outras colunas.
        var columnFormulas = columns.Select(column => column.Kind == MapColumnKind.Formula ? MapFormula.Parse(column.Formula) : null).ToList();
        var columnIndex = columns.Select((column, index) => (column.Code, index)).ToDictionary(item => item.Code, item => item.index, StringComparer.OrdinalIgnoreCase);
        foreach (var values in raw.Values)
        {
            decimal? ColumnValue(string code)
            {
                var index = columnIndex[code];
                if (values[index] is null && columnFormulas[index] is { } nested)
                {
                    values[index] = nested.Evaluate(ColumnValue) is { } number ? decimal.Round(number, 2) : null;
                }

                return values[index];
            }

            for (var index = 0; index < columns.Count; index++)
            {
                if (columnFormulas[index] is not null)
                {
                    values[index] = ColumnValue(columns[index].Code);
                }
            }
        }

        var rows = new List<MapResultRow>(definition.Rows.Count);
        foreach (var row in definition.Rows)
        {
            if (row.Hidden)
            {
                continue;
            }

            if (row.Kind == MapRowKind.Title)
            {
                rows.Add(new MapResultRow(row.Code, row.Label, row.Kind.ToString(), row.Level, true, new decimal?[columns.Count]));
                continue;
            }

            var values = raw[row.Code];
            var visible = row.Show switch
            {
                MapShow.IfNotZero => values.Any(value => value is not null and not 0m),
                MapShow.IfPositive => values.Any(value => value > 0m),
                MapShow.IfNegative => values.Any(value => value < 0m),
                _ => true,
            };
            if (visible)
            {
                rows.Add(new MapResultRow(row.Code, row.Label, row.Kind.ToString(), row.Level, row.Bold, values));
            }
        }

        return new MapResult(columns, rows, []);
    }

    /// <summary>As contas que entram numa linha de contas, com o valor em cada coluna de valores (já com o sinal da linha).</summary>
    public static IReadOnlyList<MapContribution> Explain(MapDefinition definition, MapRequest request, IReadOnlyList<MapAccount> accounts, string rowCode)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var row = definition.Rows.FirstOrDefault(item => string.Equals(item.Code, rowCode, StringComparison.OrdinalIgnoreCase));
        if (row is null || row.Kind != MapRowKind.Accounts)
        {
            return [];
        }

        var selector = MapAccountSelector.Parse(row.Accounts);
        var result = new List<MapContribution>();
        foreach (var account in accounts.Where(account => selector.Matches(account.Code)))
        {
            var values = definition.Columns
                .Select(column => column.Kind == MapColumnKind.Value ? ApplySign(Amount(account, column, row.Measure, request), row.Sign) : (decimal?)null)
                .ToList();
            if (values.Any(value => value is not null and not 0m))
            {
                result.Add(new MapContribution(account.Code, account.Name, values));
            }
        }

        return result;
    }

    /// <summary>
    /// As contas com valor que nenhuma linha de contas apanha, e as que mais de uma
    /// linha visível apanha — o que faz um balanço não fechar.
    /// </summary>
    public static (IReadOnlyList<string> Uncovered, IReadOnlyList<string> Repeated) Coverage(
        MapDefinition definition, MapRequest request, IReadOnlyList<MapAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var selectors = definition.Rows
            .Where(row => row.Kind == MapRowKind.Accounts && !row.Hidden)
            .Select(row => MapAccountSelector.Parse(row.Accounts))
            .ToList();
        var uncovered = new List<string>();
        var repeated = new List<string>();
        var whole = new MapColumn("_", "_", Scope: MapScope.YearToDate);
        foreach (var account in accounts)
        {
            var hits = selectors.Count(selector => selector.Matches(account.Code));
            if (hits == 1)
            {
                continue;
            }

            if (hits > 1)
            {
                repeated.Add(account.Code);
            }
            else if (Amount(account, whole, MapMeasure.Debit, request) != 0m || Amount(account, whole, MapMeasure.Credit, request) != 0m)
            {
                uncovered.Add(account.Code);
            }
        }

        return (uncovered, repeated);
    }

    /// <summary>O valor de uma conta numa coluna de valores, pela medida pedida.</summary>
    public static decimal Amount(MapAccount account, MapColumn column, MapMeasure rowMeasure, MapRequest request)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(request);
        if (!account.Years.TryGetValue(request.Year + column.YearOffset, out var year))
        {
            return 0m;
        }

        var (from, to) = column.Scope switch
        {
            MapScope.Opening => (MapYearAmounts.Opening, request.FromMonth - 1),
            MapScope.YearToDate => (MapYearAmounts.Opening, request.ToMonth),
            MapScope.Month => (column.Month!.Value, column.Month!.Value),
            _ => (request.FromMonth, request.ToMonth),
        };

        var debit = 0m;
        var credit = 0m;
        for (var slot = from; slot <= to; slot++)
        {
            debit += year.Debit[slot];
            credit += year.Credit[slot];
        }

        // O apuramento é de 31 de dezembro: entra com dezembro, se foi pedido.
        if (request.IncludeClosing && to == 12 && column.Scope != MapScope.Opening)
        {
            debit += year.Debit[MapYearAmounts.Closing];
            credit += year.Credit[MapYearAmounts.Closing];
        }

        return (column.Measure ?? rowMeasure) switch
        {
            MapMeasure.Debit => debit,
            MapMeasure.Credit => credit,
            MapMeasure.DebitBalance => Math.Max(0m, debit - credit),
            MapMeasure.CreditBalance => Math.Max(0m, credit - debit),
            _ => debit - credit,
        };
    }

    private static decimal ApplySign(decimal value, MapSign sign) => sign switch
    {
        MapSign.Inverted => -value,
        MapSign.Absolute => Math.Abs(value),
        MapSign.ZeroIfNegative => Math.Max(0m, value),
        _ => value,
    };

    private static void CheckCodes(IEnumerable<string> codes, string what, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code) || !CodePattern().IsMatch(code))
            {
                problems.Add($"O código de {what} «{code}» não é válido: comece por uma letra e use só letras, algarismos e «_» (por exemplo L10).");
            }
            else if (!seen.Add(code))
            {
                problems.Add($"Há mais de uma {what} com o código {code}.");
            }
        }
    }

    private static void CheckFormula(string? text, HashSet<string> known, string where, string what, List<string> problems)
    {
        try
        {
            foreach (var reference in MapFormula.Parse(text).References.Where(reference => !known.Contains(reference)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"{where}: a fórmula refere a {what} {reference}, que não existe.");
            }
        }
        catch (FormatException exception)
        {
            problems.Add($"{where}: {exception.Message}");
        }
    }

    private static void CheckCycles(Dictionary<string, string> formulas, string what, List<string> problems)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool Visit(string code)
        {
            if (!formulas.TryGetValue(code, out var text))
            {
                return false;
            }

            if (state.TryGetValue(code, out var mark))
            {
                return mark == 1;
            }

            state[code] = 1;
            var cyclic = MapFormula.Parse(text).References.Any(Visit);
            state[code] = 2;
            return cyclic;
        }

        var looping = formulas.Keys.Where(Visit).ToList();
        if (looping.Count > 0)
        {
            problems.Add($"As fórmulas das {what} {string.Join(", ", looping)} referem-se umas às outras em círculo.");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex CodePattern();
}
