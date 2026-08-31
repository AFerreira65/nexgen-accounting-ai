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
    decimal OpeningBalance,
    decimal Balance,
    IReadOnlyList<AccountLedgerEntry> Entries);

/// <summary>
/// Agregações contabilísticas de leitura — balancete e razão de conta — sempre
/// filtradas por empresa e excluindo os lançamentos anulados. Aceitam um intervalo
/// de datas opcional; a razão devolve também o saldo de abertura desse intervalo.
/// </summary>
public sealed class AccountLedgerService(AccountingDbContext context)
{
    public async Task<TrialBalance> TrialBalanceAsync(
        Guid companyId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var lines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided);

        if (fromDate is not null)
        {
            lines = lines.Where(line => line.JournalEntry.EntryDate >= fromDate.Value);
        }

        if (toDate is not null)
        {
            lines = lines.Where(line => line.JournalEntry.EntryDate <= toDate.Value);
        }

        var grouped = await lines
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
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
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

        var accountLines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.AccountId == account.Id &&
                line.JournalEntry.Status != JournalEntryStatus.Voided);

        decimal openingBalance = 0;
        if (fromDate is not null)
        {
            openingBalance = await accountLines
                .Where(line => line.JournalEntry.EntryDate < fromDate.Value)
                .SumAsync(line => line.Debit - line.Credit, cancellationToken)
                .ConfigureAwait(false);
        }

        var periodLines = accountLines;
        if (fromDate is not null)
        {
            periodLines = periodLines.Where(line => line.JournalEntry.EntryDate >= fromDate.Value);
        }

        if (toDate is not null)
        {
            periodLines = periodLines.Where(line => line.JournalEntry.EntryDate <= toDate.Value);
        }

        var lines = await periodLines
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
        decimal running = openingBalance;
        foreach (var line in lines)
        {
            running += line.Debit - line.Credit;
            entries.Add(new AccountLedgerEntry(
                line.Reference, line.EntryDate, line.JournalCode, line.Description, line.Debit, line.Credit, running));
        }

        return new AccountLedger(account.Code, account.Name, openingBalance, running, entries);
    }
}
