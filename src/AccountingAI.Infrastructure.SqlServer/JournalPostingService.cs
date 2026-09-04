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
    string? DocumentTypeCode,
    string? DocumentTypeName,
    string DocumentReference,
    string Description,
    string? Notes,
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
        CancellationToken cancellationToken = default,
        string? documentTypeCode = null,
        string? notes = null)
    {
        RequireLines(lines);
        PostedMovement? result = null;

        await InTransactionAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await EnsurePeriodIsOpenAsync(companyId, entryDate, documentTypeCode, cancellationToken).ConfigureAwait(false);
            var (journal, prefix, documentType) = await ResolveJournalAndPrefixAsync(companyId, journalCode, documentTypeCode, cancellationToken).ConfigureAwait(false);
            var accounts = await ResolveAccountsAsync(companyId, lines, cancellationToken).ConfigureAwait(false);
            var reference = await NextReferenceAsync(companyId, prefix, entryDate.Year, cancellationToken).ConfigureAwait(false);

            var entry = new JournalEntry(
                Guid.NewGuid(), companyId, journal.Id, reference, entryDate, documentReference, description, documentType?.Id, notes);
            foreach (var line in lines)
            {
                entry.AddLine(Guid.NewGuid(), accounts[Normalize(line.AccountCode)].Id, line.Description, line.Debit, line.Credit);
            }

            entry.Complete(reviewed ? JournalEntryStatus.Reviewed : JournalEntryStatus.PendingReview);
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(entry, journal, accounts, documentType);
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
        CancellationToken cancellationToken = default,
        string? documentTypeCode = null,
        string? notes = null)
    {
        RequireLines(lines);
        var normalizedReference = Normalize(reference);
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

            await EnsurePeriodIsOpenAsync(companyId, existing.EntryDate, documentTypeCode, cancellationToken).ConfigureAwait(false);
            await EnsurePeriodIsOpenAsync(companyId, entryDate, documentTypeCode, cancellationToken).ConfigureAwait(false);

            var (journal, _, documentType) = await ResolveJournalAndPrefixAsync(companyId, journalCode, documentTypeCode, cancellationToken).ConfigureAwait(false);
            var accounts = await ResolveAccountsAsync(companyId, lines, cancellationToken).ConfigureAwait(false);

            // Corrigir é substituir: remove o lançamento e recria-o com a mesma referência.
            context.JournalEntries.Remove(existing);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var entry = new JournalEntry(
                Guid.NewGuid(), companyId, journal.Id, normalizedReference, entryDate, documentReference, description, documentType?.Id, notes);
            foreach (var line in lines)
            {
                entry.AddLine(Guid.NewGuid(), accounts[Normalize(line.AccountCode)].Id, line.Description, line.Debit, line.Credit);
            }

            entry.Complete(reviewed ? JournalEntryStatus.Reviewed : JournalEntryStatus.PendingReview);
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(entry, journal, accounts, documentType);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public Task<PostedMovement> MarkReviewedAsync(Guid companyId, string reference, CancellationToken cancellationToken = default) =>
        TransitionAsync(companyId, reference, entry => entry.MarkReviewed(), enforceOpenYear: false, cancellationToken);

    public Task<PostedMovement> VoidAsync(Guid companyId, string reference, CancellationToken cancellationToken = default) =>
        TransitionAsync(companyId, reference, entry => entry.Void(), enforceOpenYear: true, cancellationToken);

    /// <summary>
    /// Reabre um lançamento conferido (volta a "por conferir") para poder ser
    /// corrigido. Recusado num exercício apurado ou período bloqueado e para os
    /// lançamentos de apuramento/abertura (têm ecrãs próprios).
    /// </summary>
    public Task<PostedMovement> ReopenAsync(Guid companyId, string reference, CancellationToken cancellationToken = default) =>
        TransitionAsync(companyId, reference, entry => entry.Reopen(), enforceOpenYear: true, cancellationToken,
            guard: entry => entry.DocumentType?.Code is "AP" or "AB"
                ? "Os lançamentos de apuramento e abertura reabrem-se pelos respetivos ecrãs."
                : null);

    private async Task<PostedMovement> TransitionAsync(
        Guid companyId,
        string reference,
        Action<JournalEntry> transition,
        bool enforceOpenYear,
        CancellationToken cancellationToken,
        Func<JournalEntry, string?>? guard = null)
    {
        var normalizedReference = Normalize(reference);
        PostedMovement? result = null;

        await InTransactionAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var entry = await context.JournalEntries
                .Include(item => item.Journal)
                .Include(item => item.DocumentType)
                .Include(item => item.Lines)
                    .ThenInclude(item => item.Account)
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Reference == normalizedReference,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("O lançamento não existe.");

            if (guard?.Invoke(entry) is { } message)
            {
                throw new InvalidOperationException(message);
            }

            if (enforceOpenYear)
            {
                await EnsurePeriodIsOpenAsync(companyId, entry.EntryDate, entry.DocumentType?.Code, cancellationToken).ConfigureAwait(false);
            }

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

    /// <summary>
    /// Resolve o diário e o prefixo da referência. Com tipo de documento, a numeração
    /// é por tipo de documento e o diário é o predefinido do tipo (salvo se for
    /// indicado outro explicitamente).
    /// </summary>
    private async Task<(AccountingJournal Journal, string Prefix, AccountingDocumentType? DocumentType)> ResolveJournalAndPrefixAsync(
        Guid companyId,
        string? journalCode,
        string? documentTypeCode,
        CancellationToken cancellationToken)
    {
        var normalizedJournal = Normalize(journalCode);
        var normalizedDocumentType = Normalize(documentTypeCode);

        if (normalizedDocumentType.Length == 0)
        {
            return (await ResolveJournalAsync(companyId, normalizedJournal, cancellationToken).ConfigureAwait(false), normalizedJournal, null);
        }

        var documentType = await context.AccountingDocumentTypes
            .Include(item => item.DefaultJournal)
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.Code == normalizedDocumentType,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("O tipo de documento indicado não existe na empresa.");
        if (!documentType.IsActive)
        {
            throw new InvalidOperationException("O tipo de documento indicado está inativo.");
        }

        var journal = normalizedJournal.Length == 0
            ? documentType.DefaultJournal
            : await ResolveJournalAsync(companyId, normalizedJournal, cancellationToken).ConfigureAwait(false);
        if (!journal.IsActive)
        {
            throw new InvalidOperationException("O diário do tipo de documento está inativo.");
        }

        return (journal, documentType.Code, documentType);
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

    /// <summary>
    /// Recusa lançar, corrigir ou anular movimentos num exercício já apurado ou
    /// num período bloqueado pela data de bloqueio da empresa. As próprias
    /// operações de apuramento e abertura (<c>AP</c>/<c>AB</c>) ficam de fora.
    /// </summary>
    private async Task EnsurePeriodIsOpenAsync(Guid companyId, DateOnly entryDate, string? documentTypeCode, CancellationToken cancellationToken)
    {
        var normalizedDocumentType = Normalize(documentTypeCode);
        if (normalizedDocumentType is "AP" or "AB")
        {
            return;
        }

        var lockDate = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.PostingLockDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (lockDate is { } limit && entryDate <= limit)
        {
            throw new InvalidOperationException(
                $"O período até {limit:yyyy-MM-dd} está bloqueado para lançamentos.");
        }

        var closed = await context.JournalEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate.Year == entryDate.Year &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == "AP", cancellationToken)
            .ConfigureAwait(false);
        if (closed)
        {
            throw new InvalidOperationException(
                $"O exercício de {entryDate.Year} está apurado. Reabra-o para lançar movimentos com esta data.");
        }
    }

    /// <summary>
    /// Reserva o próximo número da série <paramref name="prefix"/> no exercício
    /// <paramref name="year"/>. A numeração vem de um contador persistido
    /// (<see cref="DocumentNumberSeries"/>); na primeira utilização, o contador
    /// arranca a seguir ao maior número já existente para essa série e exercício,
    /// para não colidir com lançamentos anteriores.
    /// </summary>
    private async Task<string> NextReferenceAsync(Guid companyId, string prefix, int year, CancellationToken cancellationToken)
    {
        await SqlServerApplicationLock.AcquireAsync(
            context,
            $"AccountingAI:NumberSeries:{companyId:N}:{prefix}:{year}",
            cancellationToken).ConfigureAwait(false);

        var series = await context.DocumentNumberSeries
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.Series == prefix && item.Year == year,
                cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            var start = await HighestExistingNumberAsync(companyId, prefix, year, cancellationToken).ConfigureAwait(false) + 1;
            series = new DocumentNumberSeries(Guid.NewGuid(), companyId, prefix, year, start);
            context.DocumentNumberSeries.Add(series);
        }

        var number = series.Take();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return series.Format(number);
    }

    private async Task<int> HighestExistingNumberAsync(Guid companyId, string prefix, int year, CancellationToken cancellationToken)
    {
        var referenceBase = $"{prefix}-{year}-";
        var references = await context.JournalEntries
            .Where(item => item.CompanyId == companyId && item.Reference.StartsWith(referenceBase))
            .Select(item => item.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var highest = 0;
        foreach (var reference in references)
        {
            if (int.TryParse(reference[referenceBase.Length..], out var value) && value > highest)
            {
                highest = value;
            }
        }

        return highest;
    }

    private static PostedMovement Project(
        JournalEntry entry,
        AccountingJournal journal,
        IReadOnlyDictionary<string, LedgerAccount> accountsByCode,
        AccountingDocumentType? documentType = null)
    {
        var byId = accountsByCode.Values.ToDictionary(account => account.Id);
        var type = documentType ?? entry.DocumentType;
        return new PostedMovement(
            entry.Reference,
            entry.EntryDate,
            journal.Code,
            journal.Name,
            type?.Code,
            type?.Name,
            entry.DocumentReference,
            entry.Description,
            entry.Notes,
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
