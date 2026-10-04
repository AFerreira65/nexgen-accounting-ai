using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>Uma linha do orçamento comparada com o realizado.</summary>
/// <param name="Kind"><c>rendimento</c> (classe 7) ou <c>gasto</c> (classe 6).</param>
/// <param name="AnnualBudget">O orçamentado para o exercício inteiro.</param>
/// <param name="Budget">O orçamentado até ao mês pedido.</param>
/// <param name="Actual">O realizado até ao mês pedido, na conta (ou nas contas que o código agrega) e no centro de custo.</param>
/// <param name="Deviation">Realizado menos orçamentado.</param>
/// <param name="Favourable">Se o desvio ajuda o resultado: mais rendimento ou menos gasto do que o orçamentado.</param>
public sealed record BudgetReportLine(
    string AccountCode,
    string AccountName,
    string? CostCenterCode,
    string Kind,
    decimal AnnualBudget,
    decimal Budget,
    decimal Actual,
    decimal Deviation,
    decimal? DeviationPercent,
    bool Favourable);

/// <param name="UnbudgetedIncome">Rendimentos realizados em contas sem orçamento.</param>
/// <param name="UnbudgetedExpenses">Gastos realizados em contas sem orçamento.</param>
public sealed record BudgetReport(
    int Year,
    int Month,
    IReadOnlyList<BudgetReportLine> Lines,
    decimal BudgetIncome,
    decimal ActualIncome,
    decimal BudgetExpenses,
    decimal ActualExpenses,
    decimal BudgetResult,
    decimal ActualResult,
    decimal UnbudgetedIncome,
    decimal UnbudgetedExpenses);

/// <summary>
/// Mantém o orçamento anual por conta de gastos ou de rendimentos — e,
/// opcionalmente, por centro de custo — e compara-o com o realizado até um
/// mês. Uma linha pode orçamentar uma conta ou um grupo, pelo código que o
/// agrega; duas linhas do mesmo centro de custo não se podem sobrepor. O
/// realizado exclui os lançamentos anulados e o apuramento de resultados.
/// </summary>
public sealed class BudgetService(AccountingDbContext context)
{
    /// <summary>
    /// Define o orçamento anual de uma conta (repartido por doze meses iguais).
    /// Um valor zero retira a linha do orçamento.
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<bool?> SetAsync(
        Guid userId, Guid companyId, int year, string accountCode, string? costCenterCode, decimal annualAmount,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        var code = BudgetLine.NormalizeAccountCode(accountCode);
        if (annualAmount < 0m)
        {
            throw new ArgumentException("O valor orçamentado não pode ser negativo.", nameof(annualAmount));
        }

        Guid? costCenterId = null;
        if (!string.IsNullOrWhiteSpace(costCenterCode))
        {
            var normalized = costCenterCode.Trim().ToUpperInvariant();
            costCenterId = await context.CostCenters
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.Code == normalized)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"O centro de custo {normalized} não existe.");
        }

        var existing = await context.BudgetLines
            .Where(item => item.CompanyId == companyId && item.Year == year && item.CostCenterId == costCenterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var own = existing.Where(item => item.AccountCode == code).ToList();
        context.BudgetLines.RemoveRange(own);

        if (annualAmount != 0m)
        {
            var accountExists = await context.Accounts
                .AsNoTracking()
                .AnyAsync(item => item.CompanyId == companyId && item.Code == code, cancellationToken)
                .ConfigureAwait(false);
            if (!accountExists)
            {
                throw new InvalidOperationException($"A conta {code} não existe no plano de contas.");
            }

            // Duas linhas sobrepostas contariam o mesmo realizado duas vezes.
            var overlapping = existing
                .Select(item => item.AccountCode)
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(other => other != code &&
                    (other.StartsWith(code, StringComparison.Ordinal) || code.StartsWith(other, StringComparison.Ordinal)));
            if (overlapping is not null)
            {
                throw new InvalidOperationException(
                    $"A conta {code} sobrepõe-se à conta {overlapping}, que já tem orçamento: orçamente o grupo ou as subcontas, não os dois.");
            }

            var months = BudgetLine.SpreadEvenly(decimal.Round(annualAmount, 2, MidpointRounding.AwayFromZero));
            for (var month = 1; month <= 12; month++)
            {
                context.BudgetLines.Add(BudgetLine.Create(companyId, year, month, code, costCenterId, months[month - 1]));
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>O orçamento do exercício comparado com o realizado de janeiro até <paramref name="month"/>.</summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<BudgetReport?> ReportAsync(
        Guid userId, Guid companyId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        month = Math.Clamp(month, 1, 12);
        var budget = await context.BudgetLines
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Year == year)
            .Select(item => new { item.AccountCode, item.CostCenterId, CostCenterCode = item.CostCenter!.Code, item.Month, item.Amount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var actual = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                (line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7")) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.JournalEntry.Source != EntrySource.Closing &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")))
            .GroupBy(line => new { line.Account.Code, line.CostCenterId })
            .Select(group => new { group.Key.Code, group.Key.CostCenterId, Net = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var codes = budget.Select(item => item.AccountCode).Distinct().ToList();
        var names = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && codes.Contains(account.Code))
            .ToDictionaryAsync(account => account.Code, account => account.Name, cancellationToken)
            .ConfigureAwait(false);

        static decimal Signed(string code, decimal net) => code[0] == '7' ? -net : net;

        var lines = budget
            .GroupBy(item => (item.AccountCode, item.CostCenterId, item.CostCenterCode))
            .Select(group =>
            {
                var code = group.Key.AccountCode;
                var income = code[0] == '7';
                var toDate = group.Where(item => item.Month <= month).Sum(item => item.Amount);
                // Sem centro de custo a linha é da conta inteira; com ele, só das linhas desse centro.
                var realised = actual
                    .Where(item => item.Code.StartsWith(code, StringComparison.Ordinal) &&
                        (group.Key.CostCenterId is null || item.CostCenterId == group.Key.CostCenterId))
                    .Sum(item => Signed(code, item.Net));
                var deviation = realised - toDate;
                return new BudgetReportLine(
                    code, names.GetValueOrDefault(code) ?? code, group.Key.CostCenterCode, income ? "rendimento" : "gasto",
                    group.Sum(item => item.Amount), toDate, realised, deviation,
                    toDate == 0m ? null : decimal.Round(deviation / toDate * 100m, 1),
                    income ? deviation >= 0m : deviation <= 0m);
            })
            .OrderBy(line => line.Kind == "rendimento" ? 0 : 1)
            .ThenBy(line => line.AccountCode, StringComparer.Ordinal)
            .ThenBy(line => line.CostCenterCode, StringComparer.Ordinal)
            .ToList();

        // Os totais contam cada conta uma vez: as linhas por centro de custo de uma conta que também
        // tem orçamento geral são um detalhe dele.
        var general = budget.Where(item => item.CostCenterId is null).Select(item => item.AccountCode).Distinct().ToList();
        bool Counts(BudgetReportLine line) => line.CostCenterCode is null ||
            !general.Any(code => line.AccountCode.StartsWith(code, StringComparison.Ordinal) || code.StartsWith(line.AccountCode, StringComparison.Ordinal));
        var counted = lines.Where(Counts).ToList();
        decimal Total(string kind, Func<BudgetReportLine, decimal> pick) => counted.Where(line => line.Kind == kind).Sum(pick);

        var totalIncome = actual.Where(item => item.Code[0] == '7').Sum(item => -item.Net);
        var totalExpenses = actual.Where(item => item.Code[0] == '6').Sum(item => item.Net);
        var budgetIncome = Total("rendimento", line => line.Budget);
        var budgetExpenses = Total("gasto", line => line.Budget);
        var actualIncome = Total("rendimento", line => line.Actual);
        var actualExpenses = Total("gasto", line => line.Actual);

        return new BudgetReport(
            year, month, lines,
            budgetIncome, actualIncome, budgetExpenses, actualExpenses,
            budgetIncome - budgetExpenses, actualIncome - actualExpenses,
            totalIncome - actualIncome, totalExpenses - actualExpenses);
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
