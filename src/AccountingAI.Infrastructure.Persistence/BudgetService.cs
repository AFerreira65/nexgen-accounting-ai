using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha do orçamento comparada com o realizado.</summary>
/// <param name="Kind"><c>rendimento</c> (classe 7) ou <c>gasto</c> (classe 6).</param>
/// <param name="AnnualBudget">O orçamentado para o exercício inteiro.</param>
/// <param name="Budget">O orçamentado até ao mês pedido.</param>
/// <param name="Actual">O realizado até ao mês pedido, na conta (ou nas contas que o código agrega) e no centro de custo.</param>
/// <param name="Deviation">Realizado menos orçamentado.</param>
/// <param name="MonthlyBudget">O orçamentado em cada um dos doze meses do exercício.</param>
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
    bool Favourable,
    IReadOnlyList<decimal> MonthlyBudget);

/// <summary>De onde se copia um orçamento: do orçamento ou do realizado de outro exercício.</summary>
public enum BudgetCopySource
{
    Budget,
    Actual,
}

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
/// Mantém o orçamento por conta de gastos ou de rendimentos — e,
/// opcionalmente, por centro de custo —, mês a mês, e compara-o com o
/// realizado até um mês. Uma linha pode orçamentar uma conta ou um grupo, pelo código que o
/// agrega; duas linhas do mesmo centro de custo não se podem sobrepor. O
/// realizado exclui os lançamentos anulados e o apuramento de resultados.
/// </summary>
public sealed class BudgetService(AccountingDbContext context)
{
    /// <summary>
    /// Define o orçamento de uma conta: um valor anual repartido por doze meses
    /// iguais ou, com <paramref name="monthlyAmounts"/>, um valor para cada mês.
    /// Um total zero retira a linha do orçamento.
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<bool?> SetAsync(
        Guid userId, Guid companyId, int year, string accountCode, string? costCenterCode, decimal annualAmount,
        CancellationToken cancellationToken = default, IReadOnlyList<decimal>? monthlyAmounts = null)
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
        if (monthlyAmounts is not null)
        {
            if (monthlyAmounts.Count != 12)
            {
                throw new ArgumentException("Indique um valor para cada um dos doze meses.", nameof(monthlyAmounts));
            }

            if (monthlyAmounts.Any(amount => amount < 0m))
            {
                throw new ArgumentException("O valor orçamentado não pode ser negativo.", nameof(monthlyAmounts));
            }

            monthlyAmounts = monthlyAmounts.Select(amount => decimal.Round(amount, 2, MidpointRounding.AwayFromZero)).ToList();
            annualAmount = monthlyAmounts.Sum();
        }

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

            var months = monthlyAmounts ?? BudgetLine.SpreadEvenly(decimal.Round(annualAmount, 2, MidpointRounding.AwayFromZero));
            for (var month = 1; month <= 12; month++)
            {
                context.BudgetLines.Add(BudgetLine.Create(companyId, year, month, code, costCenterId, months[month - 1]));
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Preenche o orçamento de um exercício ainda sem orçamento a partir de outro
    /// exercício: do orçamento dele (as mesmas linhas, mês a mês) ou do realizado
    /// (uma linha por grupo de dois dígitos, com o realizado de cada mês). O
    /// ajuste em percentagem aplica-se a todos os valores.
    /// </summary>
    /// <returns>O número de linhas criadas; <c>null</c> sem acesso à empresa.</returns>
    public async Task<int?> CopyAsync(
        Guid userId, Guid companyId, int year, BudgetCopySource source, int sourceYear, decimal adjustmentPercent,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (year is < 2000 or > 9999 || sourceYear is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique exercícios válidos.", nameof(year));
        }

        if (source == BudgetCopySource.Budget && sourceYear == year)
        {
            throw new ArgumentException("O exercício de origem tem de ser diferente do exercício a orçamentar.", nameof(sourceYear));
        }

        if (adjustmentPercent is <= -100m or > 1000m)
        {
            throw new ArgumentException("O ajuste tem de estar acima de -100 % e não passar de 1000 %.", nameof(adjustmentPercent));
        }

        if (await context.BudgetLines.AnyAsync(item => item.CompanyId == companyId && item.Year == year, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"O exercício {year} já tem orçamento: retire as linhas antes de copiar.");
        }

        var factor = 1m + (adjustmentPercent / 100m);
        decimal Adjust(decimal amount) => decimal.Round(amount * factor, 2, MidpointRounding.AwayFromZero);

        // (conta, centro de custo) → os doze meses.
        var copied = new List<(string AccountCode, Guid? CostCenterId, IReadOnlyList<decimal> Months)>();
        if (source == BudgetCopySource.Budget)
        {
            var origin = await context.BudgetLines
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.Year == sourceYear)
                .Select(item => new { item.AccountCode, item.CostCenterId, item.Month, item.Amount })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            copied.AddRange(origin
                .GroupBy(item => (item.AccountCode, item.CostCenterId))
                .Select(group => (
                    group.Key.AccountCode,
                    group.Key.CostCenterId,
                    (IReadOnlyList<decimal>)Enumerable.Range(1, 12)
                        .Select(month => Adjust(group.Where(item => item.Month == month).Sum(item => item.Amount)))
                        .ToList())));
        }
        else
        {
            var actual = await ActualLines(companyId, new DateOnly(sourceYear, 1, 1), new DateOnly(sourceYear, 12, 31))
                .GroupBy(line => new { line.Account.Code, line.JournalEntry.EntryDate.Month })
                .Select(group => new { group.Key.Code, group.Key.Month, Net = group.Sum(line => line.Debit - line.Credit) })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // O grupo de dois dígitos quando existe no plano de contas; senão, a própria conta.
            var groups = actual.Select(item => item.Code.Length >= 2 ? item.Code[..2] : item.Code).Distinct().ToList();
            var known = await context.Accounts
                .AsNoTracking()
                .Where(account => account.CompanyId == companyId && groups.Contains(account.Code))
                .Select(account => account.Code)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var keys = actual
                .Select(item => item.Code.Length >= 2 && known.Contains(item.Code[..2]) ? item.Code[..2] : item.Code)
                .Distinct()
                .ToList();
            // Uma conta que agrega outra da lista fica com o realizado das duas, para não haver sobreposição.
            keys = keys.Where(key => !keys.Any(other => other != key && key.StartsWith(other, StringComparison.Ordinal))).ToList();

            foreach (var key in keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                var months = Enumerable.Range(1, 12)
                    .Select(month => actual
                        .Where(item => item.Month == month && item.Code.StartsWith(key, StringComparison.Ordinal))
                        .Sum(item => key[0] == '7' ? -item.Net : item.Net))
                    .ToList();
                var total = months.Sum();
                if (total <= 0m)
                {
                    continue;
                }

                // Um mês com saldo contrário (uma nota de crédito, uma regularização) não tem como ser
                // orçamentado: fica o total do exercício repartido por igual.
                copied.Add((key, null, months.Any(amount => amount < 0m)
                    ? BudgetLine.SpreadEvenly(Adjust(total))
                    : months.Select(Adjust).ToList()));
            }
        }

        copied = copied.Where(item => item.Months.Sum() > 0m).ToList();
        if (copied.Count == 0)
        {
            throw new InvalidOperationException(source == BudgetCopySource.Budget
                ? $"O exercício {sourceYear} não tem orçamento para copiar."
                : $"O exercício {sourceYear} não tem gastos nem rendimentos realizados para copiar.");
        }

        foreach (var (accountCode, costCenterId, months) in copied)
        {
            for (var month = 1; month <= 12; month++)
            {
                context.BudgetLines.Add(BudgetLine.Create(companyId, year, month, accountCode, costCenterId, months[month - 1]));
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return copied.Count;
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
        var actual = await ActualLines(companyId, from, to)
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
                    income ? deviation >= 0m : deviation <= 0m,
                    Enumerable.Range(1, 12).Select(item => group.Where(line => line.Month == item).Sum(line => line.Amount)).ToList());
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

    /// <summary>As linhas de gastos e de rendimentos que contam como realizado: sem os lançamentos anulados e sem o apuramento.</summary>
    private IQueryable<JournalEntryLine> ActualLines(Guid companyId, DateOnly from, DateOnly to) =>
        context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                (line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7")) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.JournalEntry.Source != EntrySource.Closing &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")));

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
