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
/// Cria um lançamento contabilístico completo para uma empresa: valida o diário e as
/// contas, gera uma referência sequencial e conclui o movimento pelo domínio
/// (débito igual a crédito, pelo menos duas linhas). Tudo numa transação.
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
        if (lines is null || lines.Count < 2)
        {
            throw new ArgumentException("Um lançamento precisa de pelo menos duas linhas.", nameof(lines));
        }

        var normalizedJournal = (journalCode ?? string.Empty).Trim().ToUpperInvariant();
        PostedMovement? result = null;

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var journal = await context.Journals
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Code == normalizedJournal,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("O diário indicado não existe na empresa.");
            if (!journal.IsActive)
            {
                throw new InvalidOperationException("O diário indicado está inativo.");
            }

            var codes = lines
                .Select(line => (line.AccountCode ?? string.Empty).Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
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

            var year = entryDate.Year;
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

            var entry = new JournalEntry(
                Guid.NewGuid(),
                companyId,
                journal.Id,
                reference,
                entryDate,
                documentReference,
                description);

            var codeById = new Dictionary<Guid, string>();
            foreach (var line in lines)
            {
                var code = (line.AccountCode ?? string.Empty).Trim().ToUpperInvariant();
                var account = accounts[code];
                codeById[account.Id] = code;
                entry.AddLine(Guid.NewGuid(), account.Id, line.Description, line.Debit, line.Credit);
            }

            entry.Complete(reviewed ? JournalEntryStatus.Reviewed : JournalEntryStatus.PendingReview);
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            result = new PostedMovement(
                entry.Reference,
                entry.EntryDate,
                journal.Code,
                journal.Name,
                entry.DocumentReference,
                entry.Description,
                entry.Status == JournalEntryStatus.Reviewed ? "Conferido" : "Por conferir",
                entry.TotalAmount,
                entry.Lines
                    .OrderBy(line => line.Sequence)
                    .Select(line =>
                    {
                        var account = accounts[codeById[line.AccountId]];
                        return new PostedMovementLine(account.Code, account.Name, line.Description, line.Debit, line.Credit);
                    })
                    .ToList());
        }).ConfigureAwait(false);

        return result!;
    }
}
