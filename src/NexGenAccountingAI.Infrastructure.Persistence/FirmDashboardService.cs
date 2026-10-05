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
    int AutomaticEntriesLast12Months)
{
    public decimal AutomaticShare => EntriesLast12Months == 0 ? 0m : decimal.Round((decimal)AutomaticEntriesLast12Months / EntriesLast12Months, 2);

    public int Attention => (OverdueObligations * 10) + DueSoonObligations + PendingReview + PurchasesToPost + UnreconciledBankLines;
}

public sealed record FirmDashboard(DateOnly Date, int DueSoonDays, IReadOnlyList<FirmDashboardCompany> Companies);

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
                companySources.Where(item => item.Source != EntrySource.Manual).Sum(item => item.Count)));
        }

        return new FirmDashboard(
            date,
            DueSoonDays,
            result.OrderByDescending(item => item.OverdueObligations).ThenByDescending(item => item.Attention).ThenBy(item => item.LegalName, StringComparer.CurrentCulture).ToList());
    }
}
