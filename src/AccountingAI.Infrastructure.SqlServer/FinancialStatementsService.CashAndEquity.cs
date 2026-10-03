using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>Uma conta de contrapartida que contribuiu para uma rubrica dos fluxos de caixa.</summary>
public sealed record CashFlowAccount(string AccountCode, string AccountName, decimal Amount);

/// <summary>Uma linha da demonstração dos fluxos de caixa: positivo é recebimento, negativo é pagamento.</summary>
public sealed record CashFlowLine(
    string Section, string Label, decimal Amount, decimal Comparative, bool IsTotal, IReadOnlyList<CashFlowAccount> Accounts);

public sealed record CashFlowStatement(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CashFlowLine> Lines,
    decimal Operating,
    decimal Investing,
    decimal Financing,
    decimal NetChange,
    decimal OpeningCash,
    decimal ClosingCash);

/// <summary>Uma linha da demonstração das alterações no capital próprio: um valor por coluna e o total.</summary>
public sealed record EquityChangesRow(string Label, IReadOnlyList<decimal> Amounts, decimal Total, bool IsTotal = false);

public sealed record EquityChangesStatement(
    DateOnly From, DateOnly To, string Model, IReadOnlyList<string> Columns, IReadOnlyList<EquityChangesRow> Rows, decimal ClosingEquity);

/// <summary>
/// A demonstração dos fluxos de caixa (método direto) e a das alterações no
/// capital próprio. Ao contrário do balanço e da demonstração de resultados,
/// a AT não publica uma tabela que ligue as contas a estas rubricas: a
/// classificação é feita pelas contas de contrapartida de cada lançamento,
/// segundo os códigos do plano de contas do SNC, e é um critério da aplicação
/// — por isso cada rubrica dos fluxos de caixa mostra as contas que a formam.
/// </summary>
public sealed partial class FinancialStatementsService
{
    private const string CashLine = "Caixa e depósitos bancários";
    private const string Operating = "Atividades operacionais";
    private const string Investing = "Atividades de investimento";
    private const string Financing = "Atividades de financiamento";
    private const string Receipts = "Recebimentos — ";
    private const string Payments = "Pagamentos — ";

    private static readonly string[] OperatingBeforeSubtotal = ["Recebimentos de clientes", "Pagamentos a fornecedores", "Pagamentos ao pessoal"];

    private static readonly string[] OperatingAfterSubtotal =
        ["Pagamento / recebimento do imposto sobre o rendimento", "Outros recebimentos / pagamentos"];

    private static readonly string[] InvestingOrder =
    [
        Payments + "Ativos fixos tangíveis", Payments + "Ativos intangíveis", Payments + "Investimentos financeiros", Payments + "Outros ativos",
        Receipts + "Ativos fixos tangíveis", Receipts + "Ativos intangíveis", Receipts + "Investimentos financeiros", Receipts + "Outros ativos",
        Receipts + "Subsídios ao investimento", Receipts + "Juros e rendimentos similares", Receipts + "Dividendos",
    ];

    private static readonly string[] FinancingOrder =
    [
        Receipts + "Financiamentos obtidos", Receipts + "Realizações de capital e de outros instrumentos de capital próprio",
        Receipts + "Outras operações de financiamento",
        Payments + "Financiamentos obtidos", Payments + "Juros e gastos similares", Payments + "Dividendos",
        Payments + "Reduções de capital e de outros instrumentos de capital próprio", Payments + "Outras operações de financiamento",
    ];

    // A conta de contrapartida decide a rubrica; ganha o prefixo mais comprido.
    // «±» escolhe recebimentos ou pagamentos pelo sinal do movimento.
    private static readonly (string Prefix, string Section, string Label)[] CashFlowRules = new (string Prefix, string Section, string Label)[]
    {
        ("21", Operating, "Recebimentos de clientes"),
        ("71", Operating, "Recebimentos de clientes"),
        ("72", Operating, "Recebimentos de clientes"),
        ("22", Operating, "Pagamentos a fornecedores"),
        ("31", Operating, "Pagamentos a fornecedores"),
        ("62", Operating, "Pagamentos a fornecedores"),
        ("23", Operating, "Pagamentos ao pessoal"),
        ("63", Operating, "Pagamentos ao pessoal"),
        ("241", Operating, "Pagamento / recebimento do imposto sobre o rendimento"),
        ("2711", Investing, "±Ativos fixos tangíveis"),
        ("43", Investing, "±Ativos fixos tangíveis"),
        ("453", Investing, "±Ativos fixos tangíveis"),
        ("6871", Investing, "±Ativos fixos tangíveis"),
        ("7871", Investing, "±Ativos fixos tangíveis"),
        ("44", Investing, "±Ativos intangíveis"),
        ("454", Investing, "±Ativos intangíveis"),
        ("41", Investing, "±Investimentos financeiros"),
        ("451", Investing, "±Investimentos financeiros"),
        ("42", Investing, "±Outros ativos"),
        ("452", Investing, "±Outros ativos"),
        ("455", Investing, "±Outros ativos"),
        ("46", Investing, "±Outros ativos"),
        ("593", Investing, Receipts + "Subsídios ao investimento"),
        ("79", Investing, Receipts + "Juros e rendimentos similares"),
        ("792", Investing, Receipts + "Dividendos"),
        ("25", Financing, "±Financiamentos obtidos"),
        ("51", Financing, "±capital"),
        ("52", Financing, "±capital"),
        ("53", Financing, "±capital"),
        ("54", Financing, "±capital"),
        ("261", Financing, "±capital"),
        ("262", Financing, "±capital"),
        ("69", Financing, Payments + "Juros e gastos similares"),
        ("264", Financing, Payments + "Dividendos"),
        ("265", Financing, Payments + "Dividendos"),
        ("263", Financing, "±Outras operações de financiamento"),
        ("266", Financing, "±Outras operações de financiamento"),
        ("268", Financing, "±Outras operações de financiamento"),
    }.OrderByDescending(rule => rule.Prefix.Length).ToArray();

    private static readonly HashSet<string> CapitalColumns = new(StringComparer.Ordinal)
    {
        "Capital Subscrito", "Outros instrumentos de capital próprio", "Prémios de emissão",
    };

    /// <summary>
    /// Fluxos de caixa pelo método direto: cada lançamento que mexe em caixa ou
    /// bancos é repartido pelas suas linhas de contrapartida. As transferências
    /// entre contas de caixa anulam-se; os lançamentos de abertura não são fluxos.
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<CashFlowStatement?> CashFlowsAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var accounts = await AccountsAsync(companyId, ModelOf(company), cancellationToken).ConfigureAwait(false);
        var cashIds = accounts.Values.Where(account => account.IsCash).Select(account => account.Id).ToList();
        var current = await CashFlowsOfAsync(companyId, from, to, accounts, cashIds, cancellationToken).ConfigureAwait(false);
        var comparative = await CashFlowsOfAsync(companyId, from.AddYears(-1), to.AddYears(-1), accounts, cashIds, cancellationToken).ConfigureAwait(false);

        var lines = new List<CashFlowLine>();
        var totals = new Dictionary<string, (decimal Current, decimal Comparative)>();

        void Add(string section, IEnumerable<string> labels)
        {
            foreach (var label in labels)
            {
                var amount = current.Flows.GetValueOrDefault((section, label));
                var previous = comparative.Flows.GetValueOrDefault((section, label));
                if (amount is null && previous is null)
                {
                    continue;
                }

                var sum = amount?.Values.Sum() ?? 0m;
                var previousSum = previous?.Values.Sum() ?? 0m;
                var detail = (amount ?? new Dictionary<Guid, decimal>())
                    .Where(item => item.Value != 0m)
                    .Select(item => new CashFlowAccount(accounts[item.Key].Code, accounts[item.Key].Name, item.Value))
                    .OrderBy(item => item.AccountCode, StringComparer.Ordinal)
                    .ToList();
                lines.Add(new CashFlowLine(section, label, sum, previousSum, false, detail));
                var total = totals.GetValueOrDefault(section);
                totals[section] = (total.Current + sum, total.Comparative + previousSum);
            }
        }

        void Total(string section, string label, decimal amount, decimal previous) =>
            lines.Add(new CashFlowLine(section, label, amount, previous, true, []));

        Add(Operating, OperatingBeforeSubtotal);
        var generated = totals.GetValueOrDefault(Operating);
        Total(Operating, "Caixa gerada pelas operações", generated.Current, generated.Comparative);
        Add(Operating, OperatingAfterSubtotal);
        var operating = totals.GetValueOrDefault(Operating);
        Total(Operating, "Fluxos de caixa das atividades operacionais (1)", operating.Current, operating.Comparative);
        Add(Investing, InvestingOrder);
        var investing = totals.GetValueOrDefault(Investing);
        Total(Investing, "Fluxos de caixa das atividades de investimento (2)", investing.Current, investing.Comparative);
        Add(Financing, FinancingOrder);
        var financing = totals.GetValueOrDefault(Financing);
        Total(Financing, "Fluxos de caixa das atividades de financiamento (3)", financing.Current, financing.Comparative);

        var change = operating.Current + investing.Current + financing.Current;
        var previousChange = operating.Comparative + investing.Comparative + financing.Comparative;
        Total(string.Empty, "Variação de caixa e seus equivalentes (1+2+3)", change, previousChange);
        Total(string.Empty, "Caixa e seus equivalentes no início do período", current.ClosingCash - change, comparative.ClosingCash - previousChange);
        Total(string.Empty, "Caixa e seus equivalentes no fim do período", current.ClosingCash, comparative.ClosingCash);

        return new CashFlowStatement(
            from, to, lines, operating.Current, investing.Current, financing.Current, change, current.ClosingCash - change, current.ClosingCash);
    }

    /// <summary>
    /// Alterações no capital próprio: da posição no início à posição no fim,
    /// uma coluna por rubrica do capital próprio do balanço. A posição no fim
    /// coincide com o capital próprio do balanço à mesma data.
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<EquityChangesStatement?> EquityChangesAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var model = ModelOf(company);
        var accounts = await AccountsAsync(companyId, model, cancellationToken).ConfigureAwait(false);
        var equityIds = accounts.Values.Where(account => account.EquityColumn is not null).Select(account => account.Id).ToList();
        var yearStart = new DateOnly(to.Year, 1, 1);
        var entries = await EntriesTouchingAsync(companyId, yearStart, to, equityIds, includeOpening: true, cancellationToken).ConfigureAwait(false);

        const int Opening = 0, Transfers = 1, OtherChanges = 2, Result = 3, Contributions = 5, Distributions = 6, OtherOwners = 7;
        var cells = new Dictionary<(int Row, string Column), decimal>();
        void Sum(int row, string column, decimal amount) => cells[(row, column)] = cells.GetValueOrDefault((row, column)) + amount;

        foreach (var entry in entries)
        {
            var before = entry.Source == EntrySource.Opening || entry.Date < from;
            // Num apuramento, o que entra na conta de resultados já está no resultado calculado.
            var closing = entry.Lines.Any(line => accounts[line.AccountId].IsResult);
            var onlyEquity = entry.Lines.All(line => accounts[line.AccountId].EquityColumn is not null);
            var distribution = entry.Lines.Any(line =>
                accounts[line.AccountId].Code.StartsWith("264", StringComparison.Ordinal) ||
                accounts[line.AccountId].Code.StartsWith("265", StringComparison.Ordinal));
            var equityLines = entry.Lines
                .Select(line => (Column: accounts[line.AccountId].EquityColumn, Amount: line.Credit - line.Debit))
                .Where(line => line.Column is not null && line.Amount != 0m && !(closing && line.Column == ResultLine))
                .ToList();
            // Numa aplicação de resultados com dividendos, o que vai para reservas e resultados
            // transitados é transferência; só o que sai do capital próprio é distribuição.
            var increases = equityLines.Where(line => line.Amount > 0m).Sum(line => line.Amount);
            var decreases = -equityLines.Where(line => line.Amount < 0m).Sum(line => line.Amount);
            var transferred = decreases == 0m ? 0m : Math.Min(1m, increases / decreases);
            foreach (var (column, amount) in equityLines)
            {
                if (before)
                {
                    Sum(Opening, column!, amount);
                }
                else if (onlyEquity)
                {
                    Sum(Transfers, column!, amount);
                }
                else if (distribution)
                {
                    var transfer = amount > 0m ? amount : Math.Round(amount * transferred, 2);
                    Sum(Transfers, column!, transfer);
                    Sum(Distributions, column!, amount - transfer);
                }
                else
                {
                    var row = column == "Dividendos antecipados" ? Distributions
                        : CapitalColumns.Contains(column!) ? (amount > 0m ? Contributions : OtherOwners)
                        : column == "Ações (quotas) próprias" ? OtherOwners
                        : OtherChanges;
                    Sum(row, column!, amount);
                }
            }
        }

        // O resultado gerado antes do período (num período que não começa em janeiro) já é posição inicial.
        if (from > yearStart)
        {
            Sum(Opening, ResultLine, await ResultOfAsync(companyId, yearStart, from.AddDays(-1), model, cancellationToken).ConfigureAwait(false));
        }

        Sum(Result, ResultLine, await ResultOfAsync(companyId, from, to, model, cancellationToken).ConfigureAwait(false));

        var order = BalanceSections.Single(section => section.Key == "C").Order;
        var columns = order
            .Concat(cells.Keys.Select(key => key.Column))
            .Distinct(StringComparer.Ordinal)
            .Where(column => cells.Any(cell => cell.Key.Column == column && cell.Value != 0m))
            .ToList();

        EquityChangesRow Row(string label, bool isTotal, params int[] sources)
        {
            var amounts = columns.Select(column => sources.Sum(source => cells.GetValueOrDefault((source, column)))).ToList();
            return new EquityChangesRow(label, amounts, amounts.Sum(), isTotal);
        }

        var rows = new List<EquityChangesRow>
        {
            Row("Posição no início do período", true, Opening),
            Row("Aplicação de resultados e transferências entre rubricas", false, Transfers),
            Row("Outras alterações reconhecidas no capital próprio", false, OtherChanges),
            Row(ResultLine, false, Result),
            Row("Resultado integral", true, OtherChanges, Result),
            Row("Realizações de capital e de prémios de emissão", false, Contributions),
            Row("Distribuições", false, Distributions),
            Row("Outras operações com detentores de capital", false, OtherOwners),
            Row("Posição no fim do período", true, Opening, Transfers, OtherChanges, Result, Contributions, Distributions, OtherOwners),
        };

        return new EquityChangesStatement(from, to, ModelName(model), columns, rows, rows[^1].Total);
    }

    private async Task<(Dictionary<(string Section, string Label), Dictionary<Guid, decimal>> Flows, decimal ClosingCash)> CashFlowsOfAsync(
        Guid companyId, DateOnly from, DateOnly to, Dictionary<Guid, AccountInfo> accounts, List<Guid> cashIds, CancellationToken cancellationToken)
    {
        var flows = new Dictionary<(string Section, string Label), Dictionary<Guid, decimal>>();
        var entries = await EntriesTouchingAsync(companyId, from, to, cashIds, includeOpening: false, cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            var counterparts = entry.Lines
                .Where(line => !accounts[line.AccountId].IsCash && line.Credit != line.Debit)
                .Select(line => (line.AccountId, accounts[line.AccountId].Code, Amount: line.Credit - line.Debit))
                .ToList();
            // O IVA de uma compra ou de uma venda a pronto segue a rubrica da operação.
            var main = counterparts
                .Where(line => !line.Code.StartsWith("243", StringComparison.Ordinal))
                .OrderByDescending(line => Math.Abs(line.Amount))
                .Select(line => ((string Section, string Label)?)CashFlowRubric(line.Code, line.Amount))
                .FirstOrDefault();
            foreach (var line in counterparts)
            {
                var rubric = line.Code.StartsWith("243", StringComparison.Ordinal) && main is not null
                    ? main.Value
                    : CashFlowRubric(line.Code, line.Amount);
                if (!flows.TryGetValue(rubric, out var byAccount))
                {
                    flows[rubric] = byAccount = [];
                }

                byAccount[line.AccountId] = byAccount.GetValueOrDefault(line.AccountId) + line.Amount;
            }
        }

        var closingCash = cashIds.Count == 0
            ? 0m
            : await context.JournalEntryLines
                .AsNoTracking()
                .Where(line => line.CompanyId == companyId &&
                    cashIds.Contains(line.AccountId) &&
                    line.JournalEntry.Status != JournalEntryStatus.Voided &&
                    line.JournalEntry.EntryDate >= new DateOnly(to.Year, 1, 1) &&
                    line.JournalEntry.EntryDate <= to)
                .SumAsync(line => line.Debit - line.Credit, cancellationToken)
                .ConfigureAwait(false);
        return (flows, closingCash);
    }

    private static (string Section, string Label) CashFlowRubric(string accountCode, decimal amount)
    {
        foreach (var (prefix, section, label) in CashFlowRules)
        {
            if (!accountCode.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (label == "±capital")
            {
                return (section, amount >= 0m
                    ? Receipts + "Realizações de capital e de outros instrumentos de capital próprio"
                    : Payments + "Reduções de capital e de outros instrumentos de capital próprio");
            }

            return (section, label[0] == '±' ? (amount >= 0m ? Receipts : Payments) + label[1..] : label);
        }

        return (Operating, "Outros recebimentos / pagamentos");
    }

    private async Task<decimal> ResultOfAsync(Guid companyId, DateOnly from, DateOnly to, string model, CancellationToken cancellationToken) =>
        (await BalancesAsync(companyId, from, to, includeClosing: false, cancellationToken).ConfigureAwait(false))
        .Where(account => account.TaxonomyCode is { } code && account.TaxonomyReference is { } reference &&
            StatementLineCatalog.Find(reference, code, model)?.Class is "GR" or "RG")
        .Sum(account => account.Credit - account.Debit);

    /// <summary>Os lançamentos não anulados do intervalo que têm pelo menos uma linha nas contas dadas, com todas as suas linhas.</summary>
    private async Task<List<EntryLines>> EntriesTouchingAsync(
        Guid companyId, DateOnly from, DateOnly to, List<Guid> accountIds, bool includeOpening, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                (includeOpening || line.JournalEntry.Source != EntrySource.Opening) &&
                line.JournalEntry.Lines.Any(other => accountIds.Contains(other.AccountId)))
            .Select(line => new { line.JournalEntryId, line.JournalEntry.EntryDate, line.JournalEntry.Source, line.AccountId, line.Debit, line.Credit })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .GroupBy(row => row.JournalEntryId)
            .Select(entry => new EntryLines(
                entry.First().EntryDate, entry.First().Source,
                entry.Select(row => new EntryLine(row.AccountId, row.Debit, row.Credit)).ToList()))
            .ToList();
    }

    private async Task<Dictionary<Guid, AccountInfo>> AccountsAsync(Guid companyId, string model, CancellationToken cancellationToken)
    {
        var rows = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .Select(account => new { account.Id, account.Code, account.Name, account.TaxonomyReference, account.TaxonomyCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row =>
        {
            var entry = row.TaxonomyReference is { } reference && row.TaxonomyCode is { } code
                ? StatementLineCatalog.Find(reference, code, model)
                : null;
            // Sem taxonomia, caixa e bancos reconhecem-se pelo código (11, 12 e 13).
            var isCash = entry is not null
                ? entry.DebitLine == CashLine
                : row.Code.StartsWith("11", StringComparison.Ordinal) || row.Code.StartsWith("12", StringComparison.Ordinal) || row.Code.StartsWith("13", StringComparison.Ordinal);
            var line = entry?.CreditLine ?? entry?.DebitLine;
            var equityColumn = entry?.Class == "C" && line is not null
                ? (line.StartsWith(ResultLine, StringComparison.Ordinal) ? ResultLine : line)
                : null;
            return new AccountInfo(row.Id, row.Code, row.Name, isCash, equityColumn, entry?.Class is "GR" or "RG");
        });
    }

    private sealed record AccountInfo(Guid Id, string Code, string Name, bool IsCash, string? EquityColumn, bool IsResult);

    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit);

    private sealed record EntryLines(DateOnly Date, EntrySource Source, List<EntryLine> Lines);
}
