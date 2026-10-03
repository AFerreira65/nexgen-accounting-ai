using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>Uma rubrica do mapa de exploração: um valor por mês e o total do exercício.</summary>
public sealed record OperatingMapRow(string Label, IReadOnlyList<decimal> Months, decimal Total, bool IsTotal = false);

/// <param name="LastMonth">O último mês com movimentos (0 se não houver).</param>
public sealed record OperatingMap(
    int Year, int LastMonth, string Model, IReadOnlyList<OperatingMapRow> Rows, IReadOnlyList<StatementExcludedAccount> Excluded);

/// <summary>Um rácio: o valor do período e o do período homólogo; <c>null</c> quando o denominador é zero.</summary>
/// <param name="Unit"><c>x</c> (quociente), <c>%</c>, <c>dias</c> ou <c>€</c>.</param>
public sealed record FinancialRatio(string Group, string Label, decimal? Value, decimal? Comparative, string Unit, string Formula);

/// <param name="BalanceDifference">A diferença do balanço que serve de base (ativo − capital próprio − passivo): com ela diferente de zero os rácios não são fiáveis.</param>
/// <param name="ExcludedAccounts">Quantas contas com saldo ficaram fora das demonstrações.</param>
public sealed record FinancialRatios(
    DateOnly From, DateOnly To, string Model, int Days, IReadOnlyList<FinancialRatio> Ratios, decimal BalanceDifference, int ExcludedAccounts);

/// <summary>
/// O mapa de exploração mensal (a demonstração de resultados por naturezas,
/// mês a mês) e os rácios financeiros. Ambos assentam nas mesmas rubricas das
/// demonstrações financeiras, pelo que herdam as suas regras e limitações.
/// </summary>
public sealed partial class FinancialStatementsService
{
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<OperatingMap?> OperatingMapAsync(
        Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var model = ModelOf(company);
        var accounts = (await context.Accounts
                .AsNoTracking()
                .Where(account => account.CompanyId == companyId)
                .Select(account => new { account.Id, account.Code, account.Name, account.TaxonomyReference, account.TaxonomyCode })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(account => account.Id);
        var movements = await MonthlyMovementsAsync(companyId, year, closing: false, cancellationToken).ConfigureAwait(false);

        var excluded = new List<StatementExcludedAccount>();
        var months = Enumerable.Range(1, 12)
            .Select(month => IncomeLines(
                movements
                    .Where(item => item.Month == month)
                    .Select(item =>
                    {
                        var account = accounts[item.AccountId];
                        return new AccountBalance(account.Code, account.Name, account.TaxonomyReference, account.TaxonomyCode, item.Debit, item.Credit);
                    })
                    .ToList(),
                model,
                excluded))
            .ToList();

        var rows = new List<OperatingMapRow>();
        var running = new decimal[12];
        var used = new HashSet<string>(StringComparer.Ordinal);

        void Add(IEnumerable<string> labels)
        {
            foreach (var label in labels)
            {
                if (!used.Add(label))
                {
                    continue;
                }

                var values = months.Select(month => month.GetValueOrDefault(label)).ToList();
                if (values.All(value => value == 0m))
                {
                    continue;
                }

                rows.Add(new OperatingMapRow(label, values, values.Sum()));
                for (var index = 0; index < 12; index++)
                {
                    running[index] += values[index];
                }
            }
        }

        void Total(string label) => rows.Add(new OperatingMapRow(label, running.ToArray(), running.Sum(), IsTotal: true));

        var known = IncomeBeforeDepreciation.Concat(IncomeBeforeOperating).Concat(IncomeBeforeTax).Append(TaxLine).ToHashSet(StringComparer.Ordinal);
        Add(IncomeBeforeDepreciation);
        Add(months.SelectMany(month => month.Keys).Where(label => !known.Contains(label)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Total("Resultado antes de depreciações, gastos de financiamento e impostos");
        Add(IncomeBeforeOperating);
        Total("Resultado operacional (antes de gastos de financiamento e impostos)");
        Add(IncomeBeforeTax);
        Total("Resultado antes de impostos");
        Add([TaxLine]);
        Total(ResultLine);

        return new OperatingMap(
            year,
            movements.Count == 0 ? 0 : movements.Max(item => item.Month),
            ModelName(model),
            rows,
            excluded
                .GroupBy(item => (item.AccountCode, item.AccountName, item.Reason))
                .Select(group => new StatementExcludedAccount(group.Key.AccountCode, group.Key.AccountName, group.Sum(item => item.Balance), group.Key.Reason))
                .OrderBy(item => item.AccountCode, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    /// Rácios calculados sobre o balanço à data final e a demonstração de
    /// resultados do período, com o período homólogo. Os valores de resultados
    /// são os do período — não são anualizados; os prazos médios usam os dias
    /// do período.
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<FinancialRatios?> RatiosAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var balance = await BalanceSheetAsync(userId, companyId, to, cancellationToken).ConfigureAwait(false);
        var income = await IncomeStatementAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);
        if (balance is null || income is null)
        {
            return null;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var current = RatiosOf(balance, income, days, line => line.Amount);
        var comparative = RatiosOf(balance, income, days, line => line.Comparative);
        var ratios = current
            .Zip(comparative, (now, before) => now with { Comparative = before.Value })
            .ToList();
        return new FinancialRatios(
            from, to, balance.Model, days, ratios, balance.Difference, balance.Excluded.Count + income.Excluded.Count);
    }

    private static List<FinancialRatio> RatiosOf(
        BalanceSheetStatement balance, IncomeStatementByNature income, int days, Func<StatementLine, decimal> pick)
    {
        decimal Balance(string section, string label) =>
            balance.Lines.Where(line => line.Section == section && line.Label == label).Sum(pick);
        decimal Income(string label) => income.Lines.Where(line => line.Label == label).Sum(pick);
        static decimal? Divide(decimal numerator, decimal denominator, decimal factor = 1m) =>
            denominator == 0m ? null : Math.Round(numerator / denominator * factor, 2);

        var currentAssets = Balance("Ativo corrente", "Total do ativo corrente");
        var currentLiabilities = Balance("Passivo corrente", "Total do passivo corrente");
        var assets = Balance(string.Empty, "Total do ativo");
        var liabilities = Balance(string.Empty, "Total do passivo");
        var equity = Balance("Capital próprio", "Total do capital próprio");
        var inventories = Balance("Ativo corrente", "Inventários") + Balance("Ativo corrente", "Ativos biológicos");
        var cash = Balance("Ativo corrente", "Caixa e depósitos bancários");
        var customers = Balance("Ativo corrente", "Clientes");
        var suppliers = Balance("Passivo corrente", "Fornecedores");
        var sales = Income("Vendas e serviços prestados");
        // Os gastos vêm com sinal negativo na demonstração de resultados.
        var costOfSales = -Income("Custo das mercadorias vendidas e das matérias consumidas");
        var externalSupplies = -Income("Fornecimentos e serviços externos");
        var ebitda = Income("Resultado antes de depreciações, gastos de financiamento e impostos");
        var operating = Income("Resultado operacional (antes de gastos de financiamento e impostos)");
        var net = Income(ResultLine);

        const string Liquidity = "Liquidez", Structure = "Estrutura financeira", Profitability = "Rendibilidade", Activity = "Atividade";
        return
        [
            new(Liquidity, "Liquidez geral", Divide(currentAssets, currentLiabilities), null, "x", "Ativo corrente ÷ Passivo corrente"),
            new(Liquidity, "Liquidez reduzida", Divide(currentAssets - inventories, currentLiabilities), null, "x", "(Ativo corrente − Inventários) ÷ Passivo corrente"),
            new(Liquidity, "Liquidez imediata", Divide(cash, currentLiabilities), null, "x", "Caixa e depósitos bancários ÷ Passivo corrente"),
            new(Liquidity, "Fundo de maneio", currentAssets - currentLiabilities, null, "€", "Ativo corrente − Passivo corrente"),
            new(Structure, "Autonomia financeira", Divide(equity, assets, 100m), null, "%", "Capital próprio ÷ Ativo"),
            new(Structure, "Solvabilidade", Divide(equity, liabilities), null, "x", "Capital próprio ÷ Passivo"),
            new(Structure, "Endividamento", Divide(liabilities, assets, 100m), null, "%", "Passivo ÷ Ativo"),
            new(Profitability, "Rendibilidade do capital próprio", Divide(net, equity, 100m), null, "%", "Resultado líquido do período ÷ Capital próprio"),
            new(Profitability, "Rendibilidade do ativo", Divide(operating, assets, 100m), null, "%", "Resultado operacional ÷ Ativo"),
            new(Profitability, "Margem EBITDA", Divide(ebitda, sales, 100m), null, "%", "Resultado antes de depreciações, gastos de financiamento e impostos ÷ Vendas e serviços prestados"),
            new(Profitability, "Margem líquida", Divide(net, sales, 100m), null, "%", "Resultado líquido do período ÷ Vendas e serviços prestados"),
            new(Activity, "Prazo médio de recebimento", Divide(customers, sales, days), null, "dias", "Clientes ÷ Vendas e serviços prestados × dias do período"),
            new(Activity, "Prazo médio de pagamento", Divide(suppliers, costOfSales + externalSupplies, days), null, "dias", "Fornecedores ÷ (Custo das mercadorias vendidas e das matérias consumidas + Fornecimentos e serviços externos) × dias do período"),
            new(Activity, "Duração média dos inventários", Divide(inventories, costOfSales, days), null, "dias", "Inventários ÷ Custo das mercadorias vendidas e das matérias consumidas × dias do período"),
        ];
    }
}
