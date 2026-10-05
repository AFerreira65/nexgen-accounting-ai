using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma empresa no painel do gabinete: o trabalho por fazer e o grau de automação.</summary>
public sealed record FirmDashboardCompany(
    Guid CompanyId,
    string LegalName,
    string TaxRegistrationNumber,
    int OverdueObligations,
    int DueSoonObligations,
    DateOnly? NextDueDate,
    string? NextObligation,
    int PendingReview,
    int PurchasesToPost,
    int UnreconciledBankLines,
    int EntriesLast12Months,
    int AutomaticEntriesLast12Months,
    decimal? AutomaticShareLast3Months = null,
    decimal? AutomaticSharePrevious3Months = null)
{
    public decimal AutomaticShare => EntriesLast12Months == 0 ? 0m : decimal.Round((decimal)AutomaticEntriesLast12Months / EntriesLast12Months, 2);

    public int Attention => (OverdueObligations * 10) + DueSoonObligations + PendingReview + PurchasesToPost + UnreconciledBankLines;
}

/// <summary>Os lançamentos de um mês, em todas as empresas do painel, pela origem (F3-28B).</summary>
public sealed record FirmAutomationMonth(DateOnly Month, int Manual, int SaftImport, int EFatura, int SaftBilling, int BankStatement)
{
    public int Entries => Manual + Automatic;

    public int Automatic => SaftImport + EFatura + SaftBilling + BankStatement;

    public decimal? AutomaticShare => Entries == 0 ? null : decimal.Round((decimal)Automatic / Entries, 2);
}

public sealed record FirmDashboard(DateOnly Date, int DueSoonDays, IReadOnlyList<FirmDashboardCompany> Companies, IReadOnlyList<FirmAutomationMonth>? Automation = null);

/// <summary>
/// O painel do gabinete (F3-28A): todas as empresas a que o utilizador tem
/// acesso, com as obrigações em atraso e a vencer (do calendário, F3-27A), os
/// lançamentos por conferir, as faturas do e-Fatura por lançar nos últimos três
/// meses (F3-18), os movimentos bancários por conciliar (F3-19) e a parte dos
/// lançamentos dos últimos doze meses criada a partir de importações e
/// sugestões. Ordena pelo que mais precisa de atenção.
/// </summary>
public sealed class FirmDashboardService(
    AccountingDbContext context, ObligationCalendarService obligations, SuggestedEntryService suggestions)
{
    public const int DueSoonDays = 15;

    /// <summary>Meses da evolução da automação, a acabar no mês da data do painel.</summary>
    public const int AutomationMonths = 12;

    public async Task<FirmDashboard> BuildAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var companies = await context.Companies
            .AsNoTracking()
            .Where(company => company.IsActive && company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .OrderBy(company => company.LegalName)
            .Select(company => new { company.Id, company.LegalName, company.TaxRegistrationNumber })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var ids = companies.Select(company => company.Id).ToList();

        var pending = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => ids.Contains(entry.CompanyId) && entry.Status == JournalEntryStatus.PendingReview)
            .GroupBy(entry => entry.CompanyId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken)
            .ConfigureAwait(false);
        var unreconciled = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => ids.Contains(line.CompanyId) && line.ReconciliationId == null)
            .GroupBy(line => line.CompanyId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken)
            .ConfigureAwait(false);
        var yearAgo = date.AddYears(-1);
        var sources = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => ids.Contains(entry.CompanyId) &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.Source != EntrySource.Opening &&
                entry.Source != EntrySource.Closing &&
                entry.EntryDate > yearAgo &&
                entry.EntryDate <= date)
            .GroupBy(entry => new { entry.CompanyId, entry.Source })
            .Select(group => new { group.Key.CompanyId, group.Key.Source, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A evolução da automação (F3-28B) sai dos próprios lançamentos, pela
        // origem e pelo mês: não precisa de nada guardado e tem história desde o início.
        var firstMonth = new DateOnly(date.Year, date.Month, 1).AddMonths(-(AutomationMonths - 1));
        var monthly = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => ids.Contains(entry.CompanyId) &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.Source != EntrySource.Opening &&
                entry.Source != EntrySource.Closing &&
                entry.EntryDate >= firstMonth &&
                entry.EntryDate <= date)
            .GroupBy(entry => new { entry.CompanyId, entry.EntryDate.Year, entry.EntryDate.Month, entry.Source })
            .Select(group => new { group.Key.CompanyId, group.Key.Year, group.Key.Month, group.Key.Source, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var automation = Enumerable.Range(0, AutomationMonths)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month =>
            {
                int Count(EntrySource source) => monthly.Where(item => item.Year == month.Year && item.Month == month.Month && item.Source == source).Sum(item => item.Count);
                return new FirmAutomationMonth(month, Count(EntrySource.Manual), Count(EntrySource.SaftImport), Count(EntrySource.EFatura), Count(EntrySource.SaftBilling), Count(EntrySource.BankStatement));
            })
            .ToList();
        var lastQuarter = firstMonth.AddMonths(AutomationMonths - 3);
        var previousQuarter = lastQuarter.AddMonths(-3);
        decimal? Share(Guid companyId, DateOnly from, DateOnly to)
        {
            var rows = monthly.Where(item => item.CompanyId == companyId && new DateOnly(item.Year, item.Month, 1) >= from && new DateOnly(item.Year, item.Month, 1) < to).ToList();
            var total = rows.Sum(item => item.Count);
            return total == 0 ? null : decimal.Round((decimal)rows.Where(item => item.Source != EntrySource.Manual).Sum(item => item.Count) / total, 2);
        }

        var result = new List<FirmDashboardCompany>();
        foreach (var company in companies)
        {
            var calendar = await obligations.CalendarAsync(userId, company.Id, date.AddDays(-90), date.AddDays(DueSoonDays), cancellationToken).ConfigureAwait(false);
            var open = calendar?.Rows.Where(row => row.State == "Pendente").ToList() ?? [];
            var overdue = open.Count(row => row.DueDate < date);
            var dueSoon = open.Where(row => row.DueDate >= date).OrderBy(row => row.DueDate).ToList();

            var purchases = await suggestions.ListAsync(userId, company.Id, date.AddMonths(-3), date, cancellationToken).ConfigureAwait(false);
            var companySources = sources.Where(item => item.CompanyId == company.Id).ToList();
            result.Add(new FirmDashboardCompany(
                company.Id,
                company.LegalName,
                company.TaxRegistrationNumber,
                overdue,
                dueSoon.Count,
                dueSoon.FirstOrDefault()?.DueDate,
                dueSoon.FirstOrDefault()?.Name,
                pending.GetValueOrDefault(company.Id),
                purchases?.Items.Count ?? 0,
                unreconciled.GetValueOrDefault(company.Id),
                companySources.Sum(item => item.Count),
                companySources.Where(item => item.Source != EntrySource.Manual).Sum(item => item.Count),
                Share(company.Id, lastQuarter, lastQuarter.AddMonths(3)),
                Share(company.Id, previousQuarter, lastQuarter)));
        }

        return new FirmDashboard(
            date,
            DueSoonDays,
            result.OrderByDescending(item => item.OverdueObligations).ThenByDescending(item => item.Attention).ThenBy(item => item.LegalName, StringComparer.CurrentCulture).ToList(),
            automation);
    }
}
