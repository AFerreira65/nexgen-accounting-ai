namespace NexGenAccountingAI.Application.Maps;

/// <summary>Uma linha do balancete em árvore: uma classe, uma conta de agregação ou uma conta de movimento.</summary>
/// <param name="Depth">1 para a classe; cada nível abaixo soma um.</param>
/// <param name="ParentCode">A conta acima na árvore (<c>null</c> na classe).</param>
/// <param name="IsPosting">A conta tem valores próprios (é de movimento).</param>
public sealed record TrialBalanceNode(
    string Code, string Name, int Depth, string? ParentCode, bool HasChildren, bool IsPosting, IReadOnlyList<decimal?> Values);

public sealed record TrialBalanceTreeResult(
    IReadOnlyList<MapColumn> Columns,
    IReadOnlyList<TrialBalanceNode> Nodes,
    IReadOnlyList<decimal?> Totals,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>
/// O balancete em árvore (F4B-02): as contas de movimento com valores e, por cima delas,
/// as contas do plano de que dependem até à classe, cada uma com a soma das que tem por
/// baixo. A árvore segue o plano de contas da empresa — uma conta está por baixo de outra
/// quando o código desta é o início do seu. Só faz contas: recebe os valores já lidos.
/// </summary>
public static class TrialBalanceTree
{
    private static readonly Dictionary<char, string> ClassNames = new()
    {
        ['1'] = "Meios financeiros líquidos",
        ['2'] = "Contas a receber e a pagar",
        ['3'] = "Inventários e ativos biológicos",
        ['4'] = "Investimentos",
        ['5'] = "Capital, reservas e resultados transitados",
        ['6'] = "Gastos",
        ['7'] = "Rendimentos",
        ['8'] = "Resultados",
        ['9'] = "Contabilidade de gestão",
        ['0'] = "Contas extrapatrimoniais",
    };

    /// <param name="chart">O plano de contas: código e nome de todas as contas (de agregação e de movimento).</param>
    /// <returns>Todos os nós com algum valor, por ordem de código (a ordem em que se leem numa árvore).</returns>
    public static TrialBalanceTreeResult Build(
        IReadOnlyList<MapAccount> accounts,
        IReadOnlyDictionary<string, string> chart,
        IReadOnlyList<MapColumn> columns,
        MapRequest request)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(request);

        var formulas = new MapColumnFormulas(columns);
        var whole = new MapColumn("_", "_", Scope: MapScope.YearToDate);
        var sums = new Dictionary<string, (decimal?[] Values, string? Parent, int Depth, bool Posting)>(StringComparer.Ordinal);
        var totals = new decimal?[columns.Count];
        var totalDebit = 0m;
        var totalCredit = 0m;
        var own = new decimal[columns.Count];
        var chain = new List<string>();

        foreach (var account in accounts)
        {
            if (account.Code.Length == 0)
            {
                continue;
            }

            var any = false;
            for (var index = 0; index < columns.Count; index++)
            {
                own[index] = columns[index].Kind == MapColumnKind.Value ? MapEngine.Amount(account, columns[index], MapMeasure.Balance, request) : 0m;
                any |= own[index] != 0m;
            }

            var debit = MapEngine.Amount(account, whole, MapMeasure.Debit, request);
            var credit = MapEngine.Amount(account, whole, MapMeasure.Credit, request);
            totalDebit += debit;
            totalCredit += credit;
            if (!any && debit == 0m && credit == 0m)
            {
                continue;
            }

            // A cadeia da classe até à conta: o primeiro algarismo e os prefixos que existem no plano.
            chain.Clear();
            chain.Add(account.Code[..1]);
            for (var length = 2; length < account.Code.Length; length++)
            {
                var prefix = account.Code[..length];
                if (chart.ContainsKey(prefix))
                {
                    chain.Add(prefix);
                }
            }

            if (account.Code.Length > 1)
            {
                chain.Add(account.Code);
            }

            for (var position = 0; position < chain.Count; position++)
            {
                var code = chain[position];
                var last = position == chain.Count - 1;
                if (!sums.TryGetValue(code, out var node))
                {
                    node = (new decimal?[columns.Count], position == 0 ? null : chain[position - 1], position + 1, false);
                }

                for (var index = 0; index < columns.Count; index++)
                {
                    if (columns[index].Kind == MapColumnKind.Value)
                    {
                        node.Values[index] = (node.Values[index] ?? 0m) + own[index];
                    }
                }

                sums[code] = node with { Posting = node.Posting || last };
            }

            for (var index = 0; index < columns.Count; index++)
            {
                if (columns[index].Kind == MapColumnKind.Value)
                {
                    totals[index] = (totals[index] ?? 0m) + own[index];
                }
            }
        }

        formulas.Fill(totals);
        var parents = sums.Values.Where(node => node.Parent is not null).Select(node => node.Parent!).ToHashSet(StringComparer.Ordinal);
        var names = accounts.ToDictionary(account => account.Code, account => account.Name, StringComparer.Ordinal);
        var nodes = new List<TrialBalanceNode>(sums.Count);
        foreach (var (code, node) in sums.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            formulas.Fill(node.Values);
            var name = chart.TryGetValue(code, out var charted) ? charted
                : names.TryGetValue(code, out var named) ? named
                : code.Length == 1 && ClassNames.TryGetValue(code[0], out var className) ? className
                : code;
            nodes.Add(new TrialBalanceNode(code, name, node.Depth, node.Parent, parents.Contains(code), node.Posting, node.Values));
        }

        return new TrialBalanceTreeResult(columns, nodes, totals, totalDebit, totalCredit);
    }
}
