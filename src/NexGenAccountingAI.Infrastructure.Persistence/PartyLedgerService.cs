using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O movimento e o saldo de um terceiro até uma data.</summary>
public sealed record PartyBalanceRow(
    Guid PartyId,
    string CountryCode,
    string TaxRegistrationNumber,
    string Name,
    decimal Debit,
    decimal Credit,
    decimal Balance);

/// <summary>Uma página de saldos por terceiro; os totais são do conjunto filtrado inteiro.</summary>
public sealed record PartyBalancesPage(
    int Total,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal TotalBalance,
    IReadOnlyList<PartyBalanceRow> Items);

/// <summary>Uma linha do extrato de um terceiro, com o saldo acumulado até ela.</summary>
public sealed record PartyStatementLine(
    DateOnly Date,
    string Reference,
    string DocumentReference,
    string AccountCode,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);

/// <summary>
/// O extrato de um terceiro num período: o saldo anterior, as linhas da página
/// e os totais do período inteiro.
/// </summary>
public sealed record PartyStatement(
    Guid PartyId,
    string CountryCode,
    string TaxRegistrationNumber,
    string Name,
    DateOnly? From,
    DateOnly? To,
    decimal OpeningBalance,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingBalance,
    int Total,
    IReadOnlyList<PartyStatementLine> Lines);

/// <summary>
/// Saldos e extrato por terceiro, a partir das linhas de lançamento que o
/// indicam (<see cref="JournalEntryLine.PartyId"/>). Só lê; exclui os
/// lançamentos anulados. O saldo é débito menos crédito: positivo é a receber,
/// negativo é a pagar. As linhas sem terceiro — incluindo os lançamentos
/// importados antes de os terceiros existirem — não entram.
/// </summary>
public sealed class PartyLedgerService(AccountingDbContext context)
{
    public const int MaxPageSize = 200;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PartyBalancesPage?> BalancesAsync(
        Guid userId, Guid companyId, DateOnly? to, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var partyLines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.PartyId != null &&
                line.JournalEntry.Status != JournalEntryStatus.Voided);
        if (to is { } cutoff)
        {
            partyLines = partyLines.Where(line => line.JournalEntry.EntryDate <= cutoff);
        }

        var term = search?.Trim();
        var pattern = string.IsNullOrEmpty(term) ? null : TextSearch.ContainsPattern(term);
        var query =
            from total in partyLines
                .GroupBy(line => line.PartyId)
                .Select(lines => new
                {
                    PartyId = lines.Key,
                    Debit = lines.Sum(line => line.Debit),
                    Credit = lines.Sum(line => line.Credit),
                })
            join party in context.Parties.AsNoTracking() on total.PartyId equals party.Id
            where pattern == null || EF.Functions.ILike(party.Name, pattern) || EF.Functions.ILike(party.TaxRegistrationNumber, pattern)
            select new { party.Id, party.CountryCode, party.TaxRegistrationNumber, party.Name, total.Debit, total.Credit };

        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var totalDebit = await query.SumAsync(row => (decimal?)row.Debit, cancellationToken).ConfigureAwait(false) ?? 0m;
        var totalCredit = await query.SumAsync(row => (decimal?)row.Credit, cancellationToken).ConfigureAwait(false) ?? 0m;

        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var rows = await query
            .OrderBy(row => row.Name)
            .ThenBy(row => row.TaxRegistrationNumber)
            .Skip((Math.Max(page, 1) - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PartyBalancesPage(
            count,
            totalDebit,
            totalCredit,
            totalDebit - totalCredit,
            rows.Select(row => new PartyBalanceRow(
                row.Id, row.CountryCode, row.TaxRegistrationNumber, row.Name, row.Debit, row.Credit, row.Debit - row.Credit)).ToList());
    }

    /// <returns><c>null</c> sem acesso à empresa ou terceiro desconhecido.</returns>
    public async Task<PartyStatement?> StatementAsync(
        Guid userId, Guid companyId, Guid partyId, DateOnly? from, DateOnly? to, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var party = await context.Parties
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == partyId, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return null;
        }

        var all = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.PartyId == partyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided);

        var opening = 0m;
        if (from is { } start)
        {
            opening = await all
                .Where(line => line.JournalEntry.EntryDate < start)
                .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
                .ConfigureAwait(false) ?? 0m;
            all = all.Where(line => line.JournalEntry.EntryDate >= start);
        }

        if (to is { } end)
        {
            all = all.Where(line => line.JournalEntry.EntryDate <= end);
        }

        var total = await all.CountAsync(cancellationToken).ConfigureAwait(false);
        var periodDebit = await all.SumAsync(line => (decimal?)line.Debit, cancellationToken).ConfigureAwait(false) ?? 0m;
        var periodCredit = await all.SumAsync(line => (decimal?)line.Credit, cancellationToken).ConfigureAwait(false) ?? 0m;

        var ordered = all
            .OrderBy(line => line.JournalEntry.EntryDate)
            .ThenBy(line => line.JournalEntry.Reference)
            .ThenBy(line => line.Sequence);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (Math.Max(page, 1) - 1) * size;

        // O saldo acumulado de uma página parte do saldo anterior mais o
        // movimento das páginas que a precedem.
        var carried = skip == 0
            ? 0m
            : await ordered
                .Take(skip)
                .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
                .ConfigureAwait(false) ?? 0m;

        var rows = await ordered
            .Skip(skip)
            .Take(size)
            .Select(line => new
            {
                line.JournalEntry.EntryDate,
                line.JournalEntry.Reference,
                line.JournalEntry.DocumentReference,
                AccountCode = line.Account.Code,
                line.Description,
                line.Debit,
                line.Credit,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var running = opening + carried;
        var lines = rows
            .Select(row =>
            {
                running += row.Debit - row.Credit;
                return new PartyStatementLine(
                    row.EntryDate, row.Reference, row.DocumentReference, row.AccountCode, row.Description, row.Debit, row.Credit, running);
            })
            .ToList();

        return new PartyStatement(
            party.Id, party.CountryCode, party.TaxRegistrationNumber, party.Name, from, to,
            opening, periodDebit, periodCredit, opening + periodDebit - periodCredit, total, lines);
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
