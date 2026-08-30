using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record TrialBalanceRow(
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record TrialBalance(
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<TrialBalanceRow> Rows);

public sealed record AccountLedgerEntry(
    string Reference,
    DateOnly EntryDate,
    string JournalCode,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

public sealed record AccountLedger(
    string AccountCode,
    string AccountName,
    decimal Balance,
    IReadOnlyList<AccountLedgerEntry> Entries);

/// <summary>
/// Agregações contabilísticas de leitura — balancete e razão de conta — sempre
/// filtradas por empresa e excluindo os lançamentos anulados.
/// </summary>
public sealed class AccountLedgerService(AccountingDbContext context)
{
    public async Task<TrialBalance> TrialBalanceAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var grouped = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided)
            .GroupBy(line => new { line.AccountId, line.Account.Code, line.Account.Name })
            .Select(group => new
            {
                group.Key.Code,
                group.Key.Name,
                Debit = group.Sum(line => line.Debit),
                Credit = group.Sum(line => line.Credit)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .Select(item => new TrialBalanceRow(item.Code, item.Name, item.Debit, item.Credit, item.Debit - item.Credit))
            .ToList();

        return new TrialBalance(
            rows.Sum(row => row.Debit),
            rows.Sum(row => row.Credit),
            rows);
    }

    public async Task<AccountLedger?> AccountLedgerAsync(
        Guid companyId,
        string accountCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = accountCode.Trim().ToUpperInvariant();
        var account = await context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalizedCode, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var lines = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.AccountId == account.Id &&
                line.JournalEntry.Status != JournalEntryStatus.Voided)
            .OrderBy(line => line.JournalEntry.EntryDate)
            .ThenBy(line => line.JournalEntry.Reference)
            .ThenBy(line => line.Sequence)
            .Select(line => new
            {
                line.JournalEntry.Reference,
                line.JournalEntry.EntryDate,
                JournalCode = line.JournalEntry.Journal.Code,
                line.Description,
                line.Debit,
                line.Credit
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<AccountLedgerEntry>(lines.Count);
        decimal running = 0;
        foreach (var line in lines)
        {
            running += line.Debit - line.Credit;
            entries.Add(new AccountLedgerEntry(
                line.Reference, line.EntryDate, line.JournalCode, line.Description, line.Debit, line.Credit, running));
        }

        return new AccountLedger(account.Code, account.Name, running, entries);
    }
}
