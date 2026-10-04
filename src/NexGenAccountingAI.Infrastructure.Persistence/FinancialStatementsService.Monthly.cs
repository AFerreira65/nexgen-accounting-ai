using NexGenAccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um mês da evolução: os movimentos do mês e os saldos no fim do mês.</summary>
/// <param name="Cash">Saldo de caixa e bancos no fim do mês.</param>
/// <param name="Receivables">Saldo de clientes no fim do mês.</param>
/// <param name="Payables">Saldo de fornecedores no fim do mês.</param>
public sealed record MonthlyEvolutionMonth(
    int Month,
    decimal Income,
    decimal Expenses,
    decimal Result,
    decimal PreviousIncome,
    decimal PreviousExpenses,
    decimal PreviousResult,
    decimal Cash,
    decimal Receivables,
    decimal Payables);

/// <param name="LastMonth">O último mês com movimentos no exercício (0 se não houver): depois dele os saldos são só repetição.</param>
public sealed record MonthlyEvolution(int Year, int LastMonth, IReadOnlyList<MonthlyEvolutionMonth> Months);

/// <summary>
/// A evolução mensal de um exercício, para os gráficos de gestão: rendimentos,
/// gastos e resultado de cada mês (sem o apuramento), com o exercício anterior
/// ao lado, e os saldos de tesouraria, clientes e fornecedores no fim de cada mês.
/// </summary>
public sealed partial class FinancialStatementsService
{
    private enum MonthlyKind
    {
        None,
        Income,
        Expense,
        Cash,
        Receivable,
        Payable,
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<MonthlyEvolution?> MonthlyEvolutionAsync(
        Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var model = ModelOf(company);
        var kinds = (await context.Accounts
                .AsNoTracking()
                .Where(account => account.CompanyId == companyId)
                .Select(account => new { account.Id, account.Code, account.TaxonomyReference, account.TaxonomyCode })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(account => account.Id, account => KindOf(account.Code, account.TaxonomyReference, account.TaxonomyCode, model));

        var current = await MonthlyMovementsAsync(companyId, year, closing: false, cancellationToken).ConfigureAwait(false);
        current.AddRange(await MonthlyMovementsAsync(companyId, year, closing: true, cancellationToken).ConfigureAwait(false));
        var previous = await MonthlyMovementsAsync(companyId, year - 1, closing: false, cancellationToken).ConfigureAwait(false);

        decimal Flow(List<MonthlyMovement> movements, int month, MonthlyKind kind) => movements
            .Where(item => item.Month == month && !item.Closing && kinds.GetValueOrDefault(item.AccountId) == kind)
            .Sum(item => kind == MonthlyKind.Income ? item.Credit - item.Debit : item.Debit - item.Credit);

        // Os saldos contam com tudo — abertura e apuramento incluídos — até ao fim do mês.
        decimal Balance(int month, MonthlyKind kind) => current
            .Where(item => item.Month <= month && kinds.GetValueOrDefault(item.AccountId) == kind)
            .Sum(item => kind == MonthlyKind.Payable ? item.Credit - item.Debit : item.Debit - item.Credit);

        var months = Enumerable.Range(1, 12)
            .Select(month =>
            {
                var income = Flow(current, month, MonthlyKind.Income);
                var expenses = Flow(current, month, MonthlyKind.Expense);
                var previousIncome = Flow(previous, month, MonthlyKind.Income);
                var previousExpenses = Flow(previous, month, MonthlyKind.Expense);
                return new MonthlyEvolutionMonth(
                    month, income, expenses, income - expenses, previousIncome, previousExpenses, previousIncome - previousExpenses,
                    Balance(month, MonthlyKind.Cash), Balance(month, MonthlyKind.Receivable), Balance(month, MonthlyKind.Payable));
            })
            .ToList();

        return new MonthlyEvolution(year, current.Count == 0 ? 0 : current.Max(item => item.Month), months);
    }

    private static MonthlyKind KindOf(string code, string? taxonomyReference, int? taxonomyCode, string model)
    {
        var entry = taxonomyReference is not null && taxonomyCode is { } taxonomy
            ? StatementLineCatalog.Find(taxonomyReference, taxonomy, model)
            : null;
        if (entry is null)
        {
            // Sem taxonomia, vale o código da conta no plano do SNC.
            return code switch
            {
                _ when code.StartsWith('7') => MonthlyKind.Income,
                _ when code.StartsWith('6') => MonthlyKind.Expense,
                _ when code.StartsWith("11", StringComparison.Ordinal) || code.StartsWith("12", StringComparison.Ordinal) || code.StartsWith("13", StringComparison.Ordinal) => MonthlyKind.Cash,
                _ when code.StartsWith("21", StringComparison.Ordinal) => MonthlyKind.Receivable,
                _ when code.StartsWith("22", StringComparison.Ordinal) => MonthlyKind.Payable,
                _ => MonthlyKind.None,
            };
        }

        return entry switch
        {
            // O resultado antes de impostos (811) é uma conta de passagem, não um rendimento.
            { Class: "RG" } when (entry.CreditLine ?? entry.DebitLine) != "Resultado antes de impostos" => MonthlyKind.Income,
            { Class: "GR" } => MonthlyKind.Expense,
            { DebitLine: CashLine } => MonthlyKind.Cash,
            { DebitLine: "Clientes" } => MonthlyKind.Receivable,
            { CreditLine: "Fornecedores" } => MonthlyKind.Payable,
            _ => MonthlyKind.None,
        };
    }

    /// <summary>Os movimentos por conta e mês: os do apuramento (<paramref name="closing"/>) ou todos os outros.</summary>
    private async Task<List<MonthlyMovement>> MonthlyMovementsAsync(Guid companyId, int year, bool closing, CancellationToken cancellationToken)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        return await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                closing == (line.JournalEntry.Source == EntrySource.Closing ||
                    line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818"))))
            .GroupBy(line => new { line.AccountId, line.JournalEntry.EntryDate.Month })
            .Select(group => new MonthlyMovement(
                group.Key.AccountId, group.Key.Month, closing, group.Sum(line => line.Debit), group.Sum(line => line.Credit)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record MonthlyMovement(Guid AccountId, int Month, bool Closing, decimal Debit, decimal Credit);
}
