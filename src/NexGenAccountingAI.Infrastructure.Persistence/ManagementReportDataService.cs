using NexGenAccountingAI.Application.CrossCheck;
using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Monta o <see cref="ManagementReportDossier"/> de forma determinística a partir
/// do balancete (período e homólogo), dos resumos de resultados e de posição, de
/// um punhado de rácios, do cruzamento (F2-03) e dos maiores movimentos do
/// período. Só lê; exclui os lançamentos anulados. É o único input de dados que
/// chega ao modelo (ADR-0006).
/// </summary>
public sealed class ManagementReportDataService(
    AccountingDbContext context,
    AccountLedgerService ledger,
    ICrossCheckService crossCheck) : IManagementReportDataService
{
    public async Task<ManagementReportDossier?> BuildDossierAsync(
        Guid userId,
        Guid companyId,
        ManagementReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var company = await context.Companies
            .AsNoTracking()
            .Where(item => item.Id == companyId &&
                item.IsActive &&
                item.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .Select(item => new { item.LegalName })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var from = request.From;
        var to = request.To;
        var comparativeFrom = from.AddYears(-1);
        var comparativeTo = to.AddYears(-1);

        var current = await ledger.TrialBalanceAsync(companyId, from, to, cancellationToken).ConfigureAwait(false);
        var comparative = await ledger
            .TrialBalanceAsync(companyId, comparativeFrom, comparativeTo, cancellationToken)
            .ConfigureAwait(false);
        var hasComparative = comparative.Rows.Count > 0;

        var limits = new List<string>();
        var delta = BuildTrialBalanceDelta(current, comparative, limits);
        var income = BuildIncomeSummary(current, comparative);
        var position = BuildPositionSummary(current, comparative);
        var ratios = BuildRatios(current);
        var movements = await BuildTopMovementsAsync(companyId, from, to, limits, cancellationToken).ConfigureAwait(false);
        var cross = await BuildCrossCheckAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);

        if (!hasComparative)
        {
            limits.Add("Sem exercício anterior carregado para o mesmo período — a variação homóloga é omitida.");
        }

        return new ManagementReportDossier(
            company.LegalName,
            from,
            to,
            hasComparative ? comparativeFrom : null,
            hasComparative ? comparativeTo : null,
            hasComparative,
            delta,
            income,
            position,
            ratios,
            cross,
            movements,
            limits);
    }

    private static IReadOnlyList<DossierAccountDelta> BuildTrialBalanceDelta(
        TrialBalance current, TrialBalance comparative, List<string> limits)
    {
        var comparativeByCode = comparative.Rows.ToDictionary(
            row => row.AccountCode,
            row => decimal.Round(row.Debit - row.Credit, 2),
            StringComparer.Ordinal);

        var byCode = new Dictionary<string, DossierAccountDelta>(StringComparer.Ordinal);
        foreach (var row in current.Rows)
        {
            var currentMovement = decimal.Round(row.Debit - row.Credit, 2);
            var comparativeMovement = comparativeByCode.GetValueOrDefault(row.AccountCode);
            if (currentMovement == 0m && comparativeMovement == 0m)
            {
                continue;
            }

            byCode[row.AccountCode] = new DossierAccountDelta(
                row.AccountCode,
                row.AccountName,
                ClassOf(row.AccountCode),
                currentMovement,
                comparativeMovement,
                decimal.Round(currentMovement - comparativeMovement, 2));
        }

        foreach (var row in comparative.Rows)
        {
            if (byCode.ContainsKey(row.AccountCode))
            {
                continue;
            }

            var comparativeMovement = decimal.Round(row.Debit - row.Credit, 2);
            if (comparativeMovement == 0m)
            {
                continue;
            }

            byCode[row.AccountCode] = new DossierAccountDelta(
                row.AccountCode,
                row.AccountName,
                ClassOf(row.AccountCode),
                0m,
                comparativeMovement,
                decimal.Round(-comparativeMovement, 2));
        }

        var ordered = byCode.Values
            .OrderByDescending(item => Math.Abs(item.Current))
            .ThenByDescending(item => Math.Abs(item.Comparative))
            .ThenBy(item => item.AccountCode, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count > ManagementReportDossier.MaxAccountRows)
        {
            limits.Add(
                $"Balancete: listadas as {ManagementReportDossier.MaxAccountRows} contas de maior movimento de {ordered.Count}.");
            ordered = ordered.Take(ManagementReportDossier.MaxAccountRows).ToList();
        }

        return ordered;
    }

    private static IReadOnlyList<DossierAmount> BuildIncomeSummary(TrialBalance current, TrialBalance comparative)
    {
        static decimal Income(TrialBalance balance) => decimal.Round(
            balance.Rows.Where(row => row.AccountCode.StartsWith('7')).Sum(row => row.Credit - row.Debit), 2);
        static decimal Expense(TrialBalance balance) => decimal.Round(
            balance.Rows.Where(row => row.AccountCode.StartsWith('6')).Sum(row => row.Debit - row.Credit), 2);

        var incomeCurrent = Income(current);
        var incomeComparative = Income(comparative);
        var expenseCurrent = Expense(current);
        var expenseComparative = Expense(comparative);

        return
        [
            new DossierAmount("Rendimentos (classe 7)", incomeCurrent, incomeComparative),
            new DossierAmount("Gastos (classe 6)", expenseCurrent, expenseComparative),
            new DossierAmount(
                "Resultado líquido do período",
                decimal.Round(incomeCurrent - expenseCurrent, 2),
                decimal.Round(incomeComparative - expenseComparative, 2)),
        ];
    }

    private static IReadOnlyList<DossierAmount> BuildPositionSummary(TrialBalance current, TrialBalance comparative)
    {
        static decimal Assets(TrialBalance balance) => decimal.Round(
            balance.Rows
                .Where(row => row.AccountCode.Length > 0 &&
                    ("134".Contains(row.AccountCode[0]) || (row.AccountCode[0] == '2' && row.Balance > 0)))
                .Sum(row => row.Balance),
            2);
        static decimal Liabilities(TrialBalance balance) => decimal.Round(
            balance.Rows
                .Where(row => row.AccountCode.Length > 0 && row.AccountCode[0] == '2' && row.Balance < 0)
                .Sum(row => -row.Balance),
            2);
        static decimal Equity(TrialBalance balance)
        {
            var capital = balance.Rows
                .Where(row => row.AccountCode.StartsWith('5'))
                .Sum(row => -row.Balance);
            var netResult = -balance.Rows
                .Where(row => row.AccountCode.Length > 0 && (row.AccountCode[0] == '6' || row.AccountCode[0] == '7'))
                .Sum(row => row.Balance);
            return decimal.Round(capital + netResult, 2);
        }

        return
        [
            new DossierAmount("Ativo", Assets(current), Assets(comparative)),
            new DossierAmount("Passivo", Liabilities(current), Liabilities(comparative)),
            new DossierAmount("Capital próprio", Equity(current), Equity(comparative)),
        ];
    }

    private static IReadOnlyList<DossierRatio> BuildRatios(TrialBalance balance)
    {
        decimal Sum(Func<TrialBalanceRow, bool> predicate, Func<TrialBalanceRow, decimal> selector) =>
            decimal.Round(balance.Rows.Where(predicate).Sum(selector), 2);

        var income = Sum(row => row.AccountCode.StartsWith('7'), row => row.Credit - row.Debit);
        var expense = Sum(row => row.AccountCode.StartsWith('6'), row => row.Debit - row.Credit);
        var result = decimal.Round(income - expense, 2);
        var externalServices = Sum(row => row.AccountCode.StartsWith("62"), row => row.Debit - row.Credit);
        var staff = Sum(row => row.AccountCode.StartsWith("63"), row => row.Debit - row.Credit);
        var currentAssets = Sum(
            row => row.AccountCode.Length > 0 &&
                ("13".Contains(row.AccountCode[0]) || (row.AccountCode[0] == '2' && row.Balance > 0)),
            row => row.Balance);
        var currentLiabilities = Sum(
            row => row.AccountCode.Length > 0 && row.AccountCode[0] == '2' && row.Balance < 0,
            row => -row.Balance);
        var assets = Sum(
            row => row.AccountCode.Length > 0 &&
                ("134".Contains(row.AccountCode[0]) || (row.AccountCode[0] == '2' && row.Balance > 0)),
            row => row.Balance);
        var equity = decimal.Round(Sum(row => row.AccountCode.StartsWith('5'), row => -row.Balance) + result, 2);

        static decimal? Divide(decimal numerator, decimal denominator) =>
            denominator == 0m ? null : decimal.Round(numerator / denominator, 4);

        return
        [
            new DossierRatio("margem-liquida", "Margem líquida", Divide(result, income), "rácio",
                "resultado líquido ÷ rendimentos", ["6", "7"]),
            new DossierRatio("peso-fse", "Peso dos FSE nos rendimentos", Divide(externalServices, income), "rácio",
                "gastos da subclasse 62 ÷ rendimentos", ["62", "7"]),
            new DossierRatio("peso-pessoal", "Peso dos gastos com pessoal", Divide(staff, income), "rácio",
                "gastos da subclasse 63 ÷ rendimentos", ["63", "7"]),
            new DossierRatio("liquidez-geral", "Liquidez geral", Divide(currentAssets, currentLiabilities), "rácio",
                "ativo circulante ÷ passivo circulante", ["1", "2", "3"]),
            new DossierRatio("fundo-de-maneio", "Fundo de maneio",
                decimal.Round(currentAssets - currentLiabilities, 2), "euros",
                "ativo circulante − passivo circulante", ["1", "2", "3"]),
            new DossierRatio("autonomia-financeira", "Autonomia financeira", Divide(equity, assets), "rácio",
                "capital próprio ÷ ativo", ["1", "2", "3", "4", "5"]),
        ];
    }

    private async Task<IReadOnlyList<DossierMovement>> BuildTopMovementsAsync(
        Guid companyId, DateOnly from, DateOnly to, List<string> limits, CancellationToken cancellationToken)
    {
        var top = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to)
            .GroupBy(line => line.JournalEntryId)
            .Select(group => new
            {
                JournalEntryId = group.Key,
                Reference = group.Min(line => line.JournalEntry.Reference),
                EntryDate = group.Min(line => line.JournalEntry.EntryDate),
                Description = group.Min(line => line.JournalEntry.Description),
                Amount = group.Sum(line => line.Debit),
            })
            .OrderByDescending(item => item.Amount)
            .Take(ManagementReportDossier.MaxMovements)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = top.Select(item => item.JournalEntryId).ToList();
        var codesByEntry = (await context.JournalEntryLines
                .AsNoTracking()
                .Where(line => ids.Contains(line.JournalEntryId))
                .Select(line => new { line.JournalEntryId, line.Account.Code, Weight = line.Debit + line.Credit })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(item => item.JournalEntryId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderByDescending(item => item.Weight)
                    .Select(item => item.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Take(4)
                    .ToList());

        var movements = top
            .Select(item => new DossierMovement(
                item.Reference ?? string.Empty,
                item.EntryDate,
                item.Description ?? string.Empty,
                decimal.Round(item.Amount, 2),
                codesByEntry.GetValueOrDefault(item.JournalEntryId, [])))
            .ToList();

        if (movements.Count == ManagementReportDossier.MaxMovements)
        {
            limits.Add($"Movimentos: listados os {ManagementReportDossier.MaxMovements} de maior valor do período.");
        }

        return movements;
    }

    private async Task<DossierCrossCheck> BuildCrossCheckAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var report = await crossCheck.BuildAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return new DossierCrossCheck(false, 0m, 0m, 0m, 0, 0, true);
        }

        var purchases = report.Purchases;
        var available = purchases.EfaturaCount > 0 || purchases.AccountingEntryCount > 0;
        return new DossierCrossCheck(
            available,
            purchases.EfaturaTotal,
            purchases.AccountingTotal,
            purchases.TotalDelta,
            purchases.EfaturaWithoutAccounting.Count,
            purchases.AccountingWithoutEfatura.Count,
            report.Totals.Balanced);
    }

    private static string ClassOf(string accountCode) =>
        accountCode.Length > 0 ? accountCode[..1] : string.Empty;
}
