using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record TrialBalanceRow(
    string AccountCode,
    string AccountName,
    decimal OpeningBalance,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record TrialBalance(
    decimal TotalOpeningBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<TrialBalanceRow> Rows);

public sealed record AccountLedgerEntry(
    string Reference,
    DateOnly EntryDate,
    string JournalCode,
    string AccountCode,
    string Description,
    string? Notes,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance,
    EntrySource Source);

public sealed record AccountLedger(
    string AccountCode,
    string AccountName,
    bool IsAggregate,
    decimal OpeningBalance,
    decimal Balance,
    IReadOnlyList<AccountLedgerEntry> Entries);

/// <summary>
/// Agregações contabilísticas de leitura — balancete e razão de conta — sempre
/// filtradas por empresa e excluindo os lançamentos anulados. Aceitam um intervalo
/// de datas opcional; com data inicial, o balancete e a razão devolvem também o
/// saldo de abertura de cada conta (movimentos anteriores a essa data). A razão
/// aceita ainda um filtro de origem (<c>source</c>) que só restringe as linhas
/// mostradas — o saldo de abertura e o saldo acumulado de cada linha continuam a
/// somar todas as origens.
/// </summary>
public sealed class AccountLedgerService(AccountingDbContext context)
{
    public async Task<TrialBalance> TrialBalanceAsync(
        Guid companyId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var baseLines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided);

        var periodLines = baseLines;
        if (fromDate is not null)
        {
            periodLines = periodLines.Where(line => line.JournalEntry.EntryDate >= fromDate.Value);
        }

        if (toDate is not null)
        {
            periodLines = periodLines.Where(line => line.JournalEntry.EntryDate <= toDate.Value);
        }

        var grouped = await periodLines
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new
            {
                group.Key.Code,
                group.Key.Name,
                Debit = group.Sum(line => line.Debit),
                Credit = group.Sum(line => line.Credit)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var opening = new Dictionary<string, (string Name, decimal Balance)>(StringComparer.Ordinal);
        if (fromDate is not null)
        {
            var openingGroups = await baseLines
                .Where(line => line.JournalEntry.EntryDate < fromDate.Value)
                .GroupBy(line => new { line.Account.Code, line.Account.Name })
                .Select(group => new
                {
                    group.Key.Code,
                    group.Key.Name,
                    Balance = group.Sum(line => line.Debit - line.Credit)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var group in openingGroups)
            {
                opening[group.Code] = (group.Name, group.Balance);
            }
        }

        var movementByCode = grouped.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var codes = new SortedSet<string>(movementByCode.Keys, StringComparer.Ordinal);
        codes.UnionWith(opening.Keys);

        var rows = new List<TrialBalanceRow>(codes.Count);
        foreach (var code in codes)
        {
            var openingBalance = opening.TryGetValue(code, out var openingEntry) ? openingEntry.Balance : 0m;
            movementByCode.TryGetValue(code, out var movement);
            var debit = movement?.Debit ?? 0m;
            var credit = movement?.Credit ?? 0m;
            if (openingBalance == 0m && debit == 0m && credit == 0m)
            {
                continue;
            }

            var name = movement?.Name ?? (opening.TryGetValue(code, out var named) ? named.Name : code);
            rows.Add(new TrialBalanceRow(code, name, openingBalance, debit, credit, openingBalance + debit - credit));
        }

        return new TrialBalance(
            rows.Sum(row => row.OpeningBalance),
            rows.Sum(row => row.Debit),
            rows.Sum(row => row.Credit),
            rows);
    }

    public async Task<AccountLedger?> AccountLedgerAsync(
        Guid companyId,
        string accountCode,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default,
        string? source = null)
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

        // Uma conta de integração (agregadora) não tem movimentos próprios: a razão
        // soma os das contas de movimento com o mesmo prefixo de código.
        var isAggregate = !account.AllowsPosting;
        var accountIds = isAggregate
            ? await context.Accounts.AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.Code.StartsWith(normalizedCode))
                .Select(item => item.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [account.Id];

        var accountLines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                accountIds.Contains(line.AccountId) &&
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
                AccountCode = line.Account.Code,
                line.Description,
                line.JournalEntry.Notes,
                line.Debit,
                line.Credit,
                line.JournalEntry.Source
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // O saldo acumulado é sempre calculado sobre todas as origens; o filtro de
        // origem só restringe as linhas mostradas, para o saldo continuar a bater
        // certo com a conta mesmo a ver só uma parte dos movimentos.
        var entries = new List<AccountLedgerEntry>(lines.Count);
        decimal running = openingBalance;
        foreach (var line in lines)
        {
            running += line.Debit - line.Credit;
            entries.Add(new AccountLedgerEntry(
                line.Reference, line.EntryDate, line.JournalCode, line.AccountCode, line.Description, line.Notes, line.Debit, line.Credit, running, line.Source));
        }

        if (!string.IsNullOrWhiteSpace(source) && Enum.TryParse<EntrySource>(source.Trim(), ignoreCase: true, out var entrySource))
        {
            entries = entries.Where(entry => entry.Source == entrySource).ToList();
        }

        return new AccountLedger(account.Code, account.Name, isAggregate, openingBalance, running, entries);
    }
}
