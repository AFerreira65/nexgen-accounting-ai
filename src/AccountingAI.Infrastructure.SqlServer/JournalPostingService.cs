using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record JournalEntryLineInput(string AccountCode, string Description, decimal Debit, decimal Credit);

public sealed record PostedMovementLine(
    string AccountCode,
    string AccountName,
    string Description,
    decimal Debit,
    decimal Credit);

public sealed record PostedMovement(
    string Reference,
    DateOnly EntryDate,
    string JournalCode,
    string JournalName,
    string DocumentReference,
    string Description,
    string Status,
    decimal Amount,
    IReadOnlyList<PostedMovementLine> Lines);

/// <summary>
/// Cria e mantém lançamentos contabilísticos de uma empresa: valida o diário e as
/// contas, gera uma referência sequencial e conclui o movimento pelo domínio
/// (um débito ou crédito por linha, pelo menos duas linhas, totais iguais).
/// Corrigir só é possível enquanto o lançamento está por conferir; a anulação
/// mantém o lançamento para rasto.
/// </summary>
public sealed class JournalPostingService(AccountingDbContext context)
{
    public async Task<PostedMovement> PostAsync(
        Guid companyId,
        string journalCode,
        DateOnly entryDate,
        string documentReference,
        string description,
        bool reviewed,
        IReadOnlyList<JournalEntryLineInput> lines,
        CancellationToken cancellationToken = default)
    {
        RequireLines(lines);
        var normalizedJournal = Normalize(journalCode);
        PostedMovement? result = null;

        await InTransactionAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var journal = await ResolveJournalAsync(companyId, normalizedJournal, cancellationToken).ConfigureAwait(false);
            var accounts = await ResolveAccountsAsync(companyId, lines, cancellationToken).ConfigureAwait(false);
            var reference = await NextReferenceAsync(companyId, journal, entryDate.Year, cancellationToken).ConfigureAwait(false);

            var entry = new JournalEntry(
                Guid.NewGuid(), companyId, journal.Id, reference, entryDate, documentReference, description);
            foreach (var line in lines)
            {
                entry.AddLine(Guid.NewGuid(), accounts[Normalize(line.AccountCode)].Id, line.Description, line.Debit, line.Credit);
            }

            entry.Complete(reviewed ? JournalEntryStatus.Reviewed : JournalEntryStatus.PendingReview);
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(entry, journal, accounts);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public async Task<PostedMovement> ReviseAsync(
        Guid companyId,
        string reference,
        string journalCode,
        DateOnly entryDate,
        string documentReference,
        string description,
        bool reviewed,
        IReadOnlyList<JournalEntryLineInput> lines,
        CancellationToken cancellationToken = default)
    {
        RequireLines(lines);
        var normalizedReference = Normalize(reference);
        var normalizedJournal = Normalize(journalCode);
        PostedMovement? result = null;

        await InTransactionAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var existing = await context.JournalEntries
                .Include(item => item.Lines)
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Reference == normalizedReference,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("O lançamento não existe.");
            if (existing.Status != JournalEntryStatus.PendingReview)
            {
                throw new InvalidOperationException("Só um lançamento por conferir pode ser corrigido.");
            }

            var journal = await ResolveJournalAsync(companyId, normalizedJournal, cancellationToken).ConfigureAwait(false);
            var accounts = await ResolveAccountsAsync(companyId, lines, cancellationToken).ConfigureAwait(false);

            // Corrigir é substituir: remove o lançamento e recria-o com a mesma referência.
            context.JournalEntries.Remove(existing);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var entry = new JournalEntry(
                Guid.NewGuid(), companyId, journal.Id, normalizedReference, entryDate, documentReference, description);
            foreach (var line in lines)
            {
                entry.AddLine(Guid.NewGuid(), accounts[Normalize(line.AccountCode)].Id, line.Description, line.Debit, line.Credit);
            }

            entry.Complete(reviewed ? JournalEntryStatus.Reviewed : JournalEntryStatus.PendingReview);
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(entry, journal, accounts);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public Task<PostedMovement> MarkReviewedAsync(Guid companyId, string reference, CancellationToken cancellationToken = default) =>
        TransitionAsync(companyId, reference, entry => entry.MarkReviewed(), cancellationToken);

    public Task<PostedMovement> VoidAsync(Guid companyId, string reference, CancellationToken cancellationToken = default) =>
        TransitionAsync(companyId, reference, entry => entry.Void(), cancellationToken);

    private async Task<PostedMovement> TransitionAsync(
        Guid companyId,
        string reference,
        Action<JournalEntry> transition,
        CancellationToken cancellationToken)
    {
        var normalizedReference = Normalize(reference);
        PostedMovement? result = null;

        await InTransactionAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var entry = await context.JournalEntries
                .Include(item => item.Journal)
                .Include(item => item.Lines)
                    .ThenInclude(item => item.Account)
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Reference == normalizedReference,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("O lançamento não existe.");

            transition(entry);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var accountsByCode = entry.Lines
                .Select(line => line.Account)
                .DistinctBy(account => account.Id)
                .ToDictionary(account => account.Code, StringComparer.Ordinal);
            result = Project(entry, entry.Journal, accountsByCode);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    private async Task InTransactionAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await body().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task<AccountingJournal> ResolveJournalAsync(Guid companyId, string journalCode, CancellationToken cancellationToken)
    {
        var journal = await context.Journals
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == journalCode, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("O diário indicado não existe na empresa.");
        if (!journal.IsActive)
        {
            throw new InvalidOperationException("O diário indicado está inativo.");
        }

        return journal;
    }

    private async Task<Dictionary<string, LedgerAccount>> ResolveAccountsAsync(
        Guid companyId,
        IReadOnlyList<JournalEntryLineInput> lines,
        CancellationToken cancellationToken)
    {
        var codes = lines.Select(line => Normalize(line.AccountCode)).Distinct(StringComparer.Ordinal).ToList();
        var accounts = await context.Accounts
            .Where(item => item.CompanyId == companyId && codes.Contains(item.Code))
            .ToDictionaryAsync(item => item.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var code in codes)
        {
            if (!accounts.TryGetValue(code, out var account))
            {
                throw new ArgumentException($"A conta {code} não existe na empresa.");
            }

            if (!account.IsActive)
            {
                throw new ArgumentException($"A conta {code} está inativa.");
            }

            if (!account.AllowsPosting)
            {
                throw new ArgumentException($"A conta {code} é agregadora e não aceita movimentos.");
            }
        }

        return accounts;
    }

    private async Task<string> NextReferenceAsync(Guid companyId, AccountingJournal journal, int year, CancellationToken cancellationToken)
    {
        var sequence = await context.JournalEntries
            .CountAsync(
                item => item.CompanyId == companyId && item.JournalId == journal.Id && item.EntryDate.Year == year,
                cancellationToken)
            .ConfigureAwait(false) + 1;
        var reference = $"{journal.Code}-{year}-{sequence:D5}";
        while (await context.JournalEntries
            .AnyAsync(item => item.CompanyId == companyId && item.Reference == reference, cancellationToken)
            .ConfigureAwait(false))
        {
            sequence++;
            reference = $"{journal.Code}-{year}-{sequence:D5}";
        }

        return reference;
    }

    private static PostedMovement Project(
        JournalEntry entry,
        AccountingJournal journal,
        IReadOnlyDictionary<string, LedgerAccount> accountsByCode)
    {
        var byId = accountsByCode.Values.ToDictionary(account => account.Id);
        return new PostedMovement(
            entry.Reference,
            entry.EntryDate,
            journal.Code,
            journal.Name,
            entry.DocumentReference,
            entry.Description,
            entry.Status switch
            {
                JournalEntryStatus.Reviewed => "Conferido",
                JournalEntryStatus.Voided => "Anulado",
                _ => "Por conferir"
            },
            entry.TotalAmount,
            entry.Lines
                .OrderBy(line => line.Sequence)
                .Select(line =>
                {
                    var account = byId[line.AccountId];
                    return new PostedMovementLine(account.Code, account.Name, line.Description, line.Debit, line.Credit);
                })
                .ToList());
    }

    private static void RequireLines(IReadOnlyList<JournalEntryLineInput> lines)
    {
        if (lines is null || lines.Count < 2)
        {
            throw new ArgumentException("Um lançamento precisa de pelo menos duas linhas.", nameof(lines));
        }
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
