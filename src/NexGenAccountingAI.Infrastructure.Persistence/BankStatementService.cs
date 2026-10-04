using NexGenAccountingAI.Application.Banking;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Banking;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma conta de depósitos com o resumo do que dela está importado.</summary>
public sealed record BankAccountRow(
    string AccountCode,
    string AccountName,
    int LineCount,
    int UnreconciledCount,
    DateOnly? LastMovementDate,
    string? Iban);

public sealed record BankStatementInvalidRow(int LineNumber, string Reason);

public sealed record BankStatementImportResult(
    Guid? ImportId,
    string Format,
    string? Iban,
    int Read,
    int Imported,
    int AlreadyExisted,
    int Invalid,
    IReadOnlyList<BankStatementInvalidRow> InvalidRows,
    DateOnly? From,
    DateOnly? To,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    IReadOnlyList<string> Warnings);

/// <summary>Um lançamento visto da conta bancária: o efeito líquido (débito menos crédito) nessa conta.</summary>
public sealed record BankBookEntry(
    string Reference,
    DateOnly EntryDate,
    string DocumentReference,
    string Description,
    decimal Amount,
    string Status);

public sealed record BankStatementLineRow(
    Guid Id,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    string Description,
    string? Reference,
    decimal Amount,
    decimal? Balance,
    BankBookEntry? ReconciledEntry,
    bool ReconciledAutomatically);

/// <summary>
/// O mapa de reconciliação bancária numa data: o saldo contabilístico, mais o
/// que está no banco e não na contabilidade, menos o que está na contabilidade e
/// ainda não passou no banco, dá o saldo do extrato. A diferença deve ser zero.
/// </summary>
public sealed record BankReconciliationReport(
    string AccountCode,
    string AccountName,
    DateOnly Date,
    decimal BookBalance,
    decimal? StatementBalance,
    DateOnly? StatementBalanceDate,
    IReadOnlyList<BankStatementLineRow> StatementOnly,
    IReadOnlyList<BankBookEntry> BookOnly,
    int ReconciledCount)
{
    public decimal StatementOnlyTotal => StatementOnly.Sum(line => line.Amount);

    public decimal BookOnlyTotal => BookOnly.Sum(entry => entry.Amount);

    /// <summary>O saldo do extrato que a contabilidade explica: contabilístico + só no banco − só na contabilidade.</summary>
    public decimal ExpectedStatementBalance => decimal.Round(BookBalance + StatementOnlyTotal - BookOnlyTotal, 2);

    public decimal? Difference => StatementBalance is { } balance ? decimal.Round(balance - ExpectedStatementBalance, 2) : null;
}

/// <summary>
/// Extratos bancários e conciliação (F3-19A). Importa um extrato (camt.053 ou
/// CSV) para uma conta de depósitos, sem duplicar; lista os movimentos com o
/// lançamento com que cada um está conciliado; concilia automaticamente pelo
/// valor e pela data (até <see cref="DateToleranceDays"/> dias, o mais próximo
/// primeiro, um para um) ou à mão; e monta o mapa de reconciliação. Uma
/// conciliação com um lançamento entretanto anulado deixa de contar.
/// </summary>
public sealed class BankStatementService(IBankStatementReader reader, AccountingDbContext context)
{
    public const int DateToleranceDays = 7;

    private const int InvalidRowLimit = 50;

    public async Task<IReadOnlyList<BankAccountRow>?> ListAccountsAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.AllowsPosting &&
                (account.Code.StartsWith("11") || account.Code.StartsWith("12") || account.Code.StartsWith("13")))
            .OrderBy(account => account.Code)
            .Select(account => new { account.Id, account.Code, account.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stats = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId)
            .GroupBy(line => line.AccountId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Unreconciled = group.Count(line => line.ReconciledEntryReference == null),
                Last = group.Max(line => (DateOnly?)line.BookingDate)
            })
            .ToDictionaryAsync(item => item.Key, cancellationToken)
            .ConfigureAwait(false);
        var ibans = (await context.BankStatementImports
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Iban != null)
            .OrderByDescending(item => item.ImportedAtUtc)
            .Select(item => new { item.AccountId, item.Iban })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(item => item.AccountId)
            .ToDictionary(group => group.Key, group => group.First().Iban);

        return accounts
            .Select(account =>
            {
                var stat = stats.GetValueOrDefault(account.Id);
                return new BankAccountRow(account.Code, account.Name, stat?.Count ?? 0, stat?.Unreconciled ?? 0, stat?.Last, ibans.GetValueOrDefault(account.Id));
            })
            .ToList();
    }

    public async Task<BankStatementImportResult?> ImportAsync(
        Guid userId, Guid companyId, string accountCode, Stream content, string fileName, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var account = await ResolveAccountAsync(companyId, accountCode, cancellationToken).ConfigureAwait(false);
        var file = await reader.ReadAsync(content, fileName, cancellationToken).ConfigureAwait(false);
        var valid = file.Rows.Where(row => row.IsValid && row.Amount != 0m).ToList();
        var invalid = file.Rows.Where(row => !row.IsValid).ToList();

        // A ordem entre movimentos iguais faz parte da chave: dois cafés iguais no mesmo dia são dois movimentos.
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = valid
            .Select(row =>
            {
                var baseKey = BankStatementLine.KeyOf(row.BookingDate!.Value, row.Amount, row.Description, row.Reference, 0);
                occurrences[baseKey] = occurrences.GetValueOrDefault(baseKey) + 1;
                return (Row: row, Key: BankStatementLine.KeyOf(row.BookingDate!.Value, row.Amount, row.Description, row.Reference, occurrences[baseKey]));
            })
            .ToList();
        var keys = keyed.Select(item => item.Key).ToList();
        var existing = (await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == account.Id && keys.Contains(line.LineKey))
            .Select(line => line.LineKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var fresh = keyed.Where(item => !existing.Contains(item.Key)).ToList();

        var warnings = file.Warnings.ToList();
        if (valid.Count < file.Rows.Count(row => row.IsValid))
        {
            warnings.Add("Os movimentos de montante zero foram ignorados.");
        }

        Guid? importId = null;
        if (!dryRun)
        {
            var import = new BankStatementImport(
                Guid.NewGuid(), companyId, account.Id, fileName, file.Format, file.Iban,
                file.OpeningBalance, file.ClosingBalance, file.ClosingDate,
                valid.Count == 0 ? null : valid.Min(row => row.BookingDate),
                valid.Count == 0 ? null : valid.Max(row => row.BookingDate),
                fresh.Count);
            context.BankStatementImports.Add(import);
            foreach (var (row, key) in fresh)
            {
                context.BankStatementLines.Add(BankStatementLine.Create(
                    companyId, account.Id, import.Id, key, row.BookingDate!.Value, row.ValueDate, row.Description, row.Amount, row.Balance, row.Reference));
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            importId = import.Id;
        }

        return new BankStatementImportResult(
            importId,
            file.Format,
            file.Iban,
            file.Rows.Count,
            fresh.Count,
            keyed.Count - fresh.Count,
            invalid.Count,
            invalid.Take(InvalidRowLimit).Select(row => new BankStatementInvalidRow(row.LineNumber, row.Error!)).ToList(),
            valid.Count == 0 ? null : valid.Min(row => row.BookingDate),
            valid.Count == 0 ? null : valid.Max(row => row.BookingDate),
            file.OpeningBalance,
            file.ClosingBalance,
            warnings);
    }

    public async Task<IReadOnlyList<BankStatementLineRow>?> ListLinesAsync(
        Guid userId, Guid companyId, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("A data final não pode ser anterior à inicial.", nameof(to));
        }

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var account = await ResolveAccountAsync(companyId, accountCode, cancellationToken).ConfigureAwait(false);
        var lines = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == account.Id && line.BookingDate >= from && line.BookingDate <= to)
            .OrderBy(line => line.BookingDate)
            .ThenBy(line => line.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = await BookEntriesByReferenceAsync(
            companyId, account.Id, lines.Where(line => line.ReconciledEntryReference is not null).Select(line => line.ReconciledEntryReference!).ToList(), cancellationToken)
            .ConfigureAwait(false);
        return lines.Select(line => ToRow(line, entries)).ToList();
    }

    /// <summary>
    /// Concilia os movimentos por conciliar do período com os lançamentos da conta
    /// ainda não conciliados que tenham o mesmo valor, a até
    /// <see cref="DateToleranceDays"/> dias — o mais próximo na data primeiro.
    /// Devolve quantos movimentos ficaram conciliados.
    /// </summary>
    public async Task<int?> AutoReconcileAsync(
        Guid userId, Guid companyId, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var account = await ResolveAccountAsync(companyId, accountCode, cancellationToken).ConfigureAwait(false);
        var lines = await context.BankStatementLines
            .Where(line => line.CompanyId == companyId && line.AccountId == account.Id && line.BookingDate >= from && line.BookingDate <= to)
            .OrderBy(line => line.BookingDate)
            .ThenBy(line => line.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        await ReleaseVoidedAsync(companyId, account.Id, lines, cancellationToken).ConfigureAwait(false);

        var used = await ReconciledReferencesAsync(companyId, account.Id, cancellationToken).ConfigureAwait(false);
        var candidates = (await BookEntriesAsync(
                companyId, account.Id, from.AddDays(-DateToleranceDays), to.AddDays(DateToleranceDays), cancellationToken)
            .ConfigureAwait(false))
            .Where(entry => !used.Contains(entry.Reference))
            .ToList();

        var matched = 0;
        foreach (var line in lines.Where(line => line.ReconciledEntryReference is null))
        {
            var best = candidates
                .Where(entry => entry.Amount == line.Amount && Math.Abs(entry.EntryDate.DayNumber - line.BookingDate.DayNumber) <= DateToleranceDays)
                .OrderBy(entry => Math.Abs(entry.EntryDate.DayNumber - line.BookingDate.DayNumber))
                .ThenBy(entry => entry.EntryDate)
                .ThenBy(entry => entry.Reference, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best is null)
            {
                continue;
            }

            line.Reconcile(best.Reference, automatically: true);
            candidates.Remove(best);
            matched++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return matched;
    }

    /// <summary>
    /// Concilia um movimento com um lançamento escolhido. O lançamento tem de
    /// movimentar a conta pelo mesmo valor e não estar já conciliado com outro
    /// movimento.
    /// </summary>
    public async Task<BankStatementLineRow?> ReconcileAsync(
        Guid userId, Guid companyId, Guid lineId, string entryReference, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var line = await context.BankStatementLines
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == lineId, cancellationToken)
            .ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        var reference = (entryReference ?? string.Empty).Trim().ToUpperInvariant();
        var entries = await BookEntriesByReferenceAsync(companyId, line.AccountId, [reference], cancellationToken).ConfigureAwait(false);
        if (!entries.TryGetValue(reference, out var entry) || entry.Status == JournalEntryStatus.Voided.ToString())
        {
            throw new ArgumentException($"O lançamento {entryReference} não existe, está anulado ou não movimenta esta conta.", nameof(entryReference));
        }

        if (entry.Amount != line.Amount)
        {
            throw new ArgumentException(
                $"O lançamento {entry.Reference} movimenta a conta por {entry.Amount:0.00} e o movimento do extrato é de {line.Amount:0.00}.", nameof(entryReference));
        }

        var taken = await context.BankStatementLines
            .AnyAsync(item => item.CompanyId == companyId && item.AccountId == line.AccountId && item.Id != line.Id && item.ReconciledEntryReference == entry.Reference, cancellationToken)
            .ConfigureAwait(false);
        if (taken)
        {
            throw new InvalidOperationException($"O lançamento {entry.Reference} já está conciliado com outro movimento do extrato.");
        }

        line.Reconcile(entry.Reference, automatically: false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRow(line, entries);
    }

    public async Task<bool?> UnreconcileAsync(Guid userId, Guid companyId, Guid lineId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var line = await context.BankStatementLines
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == lineId, cancellationToken)
            .ConfigureAwait(false);
        if (line is null)
        {
            return false;
        }

        line.Unreconcile();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<BankReconciliationReport?> ReconciliationAsync(
        Guid userId, Guid companyId, string accountCode, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var account = await ResolveAccountAsync(companyId, accountCode, cancellationToken).ConfigureAwait(false);
        var bookBalance = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.AccountId == account.Id &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate <= date)
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var lines = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == account.Id && line.BookingDate <= date)
            .OrderBy(line => line.BookingDate)
            .ThenBy(line => line.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var referenced = await BookEntriesByReferenceAsync(
            companyId, account.Id, lines.Where(line => line.ReconciledEntryReference is not null).Select(line => line.ReconciledEntryReference!).ToList(), cancellationToken)
            .ConfigureAwait(false);
        bool Live(BankStatementLine line) =>
            line.ReconciledEntryReference is { } reference &&
            referenced.TryGetValue(reference, out var entry) &&
            entry.Status != JournalEntryStatus.Voided.ToString() &&
            entry.EntryDate <= date;

        var statementOnly = lines.Where(line => !Live(line)).Select(line => ToRow(line, referenced)).ToList();
        var reconciledReferences = lines.Where(Live).Select(line => line.ReconciledEntryReference!).ToHashSet(StringComparer.Ordinal);

        // Só na contabilidade: os lançamentos da conta desde o primeiro extrato importado, ainda não conciliados.
        var firstStatementDate = lines.Count == 0 ? (DateOnly?)null : lines.Min(line => line.BookingDate);
        var bookOnly = firstStatementDate is { } start
            ? (await BookEntriesAsync(companyId, account.Id, start, date, cancellationToken).ConfigureAwait(false))
                .Where(entry => !reconciledReferences.Contains(entry.Reference))
                .ToList()
            : [];

        var lastWithBalance = lines.LastOrDefault(line => line.Balance is not null);
        var import = await context.BankStatementImports
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.AccountId == account.Id && item.ClosingBalance != null && item.ClosingDate != null && item.ClosingDate <= date)
            .OrderByDescending(item => item.ClosingDate)
            .ThenByDescending(item => item.ImportedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        decimal? statementBalance = null;
        DateOnly? statementBalanceDate = null;
        if (lastWithBalance is not null && (import?.ClosingDate is null || lastWithBalance.BookingDate >= import.ClosingDate))
        {
            statementBalance = lastWithBalance.Balance;
            statementBalanceDate = lastWithBalance.BookingDate;
        }
        else if (import is not null)
        {
            statementBalance = import.ClosingBalance;
            statementBalanceDate = import.ClosingDate;
        }

        // O saldo do extrato só explica os movimentos até à sua data: os posteriores ficam de fora da comparação.
        if (statementBalanceDate is { } balanceDate && balanceDate < date)
        {
            statementOnly = statementOnly.Where(line => line.BookingDate <= balanceDate).ToList();
        }

        return new BankReconciliationReport(
            account.Code, account.Name, date, decimal.Round(bookBalance, 2), statementBalance, statementBalanceDate,
            statementOnly, bookOnly, reconciledReferences.Count);
    }

    /// <summary>Os lançamentos (não anulados) que movimentam a conta no período, com o valor líquido nela.</summary>
    private async Task<List<BankBookEntry>> BookEntriesAsync(
        Guid companyId, Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.AccountId == accountId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to)
            .GroupBy(line => new
            {
                line.JournalEntry.Reference,
                line.JournalEntry.EntryDate,
                line.JournalEntry.DocumentReference,
                line.JournalEntry.Description,
                line.JournalEntry.Status
            })
            .Select(group => new { group.Key, Amount = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(row => row.Amount != 0m)
            .Select(row => new BankBookEntry(row.Key.Reference, row.Key.EntryDate, row.Key.DocumentReference, row.Key.Description, row.Amount, row.Key.Status.ToString()))
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Reference, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<Dictionary<string, BankBookEntry>> BookEntriesByReferenceAsync(
        Guid companyId, Guid accountId, IReadOnlyCollection<string> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return new Dictionary<string, BankBookEntry>(StringComparer.Ordinal);
        }

        var list = references.Distinct().ToList();
        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == accountId && list.Contains(line.JournalEntry.Reference))
            .GroupBy(line => new
            {
                line.JournalEntry.Reference,
                line.JournalEntry.EntryDate,
                line.JournalEntry.DocumentReference,
                line.JournalEntry.Description,
                line.JournalEntry.Status
            })
            .Select(group => new { group.Key, Amount = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Key.Reference,
            row => new BankBookEntry(row.Key.Reference, row.Key.EntryDate, row.Key.DocumentReference, row.Key.Description, row.Amount, row.Key.Status.ToString()),
            StringComparer.Ordinal);
    }

    /// <summary>As referências já conciliadas nesta conta com lançamentos que não estão anulados.</summary>
    private async Task<HashSet<string>> ReconciledReferencesAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken)
    {
        var references = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == accountId && line.ReconciledEntryReference != null)
            .Select(line => line.ReconciledEntryReference!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return references.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Desfaz as conciliações com lançamentos que entretanto foram anulados.</summary>
    private async Task ReleaseVoidedAsync(Guid companyId, Guid accountId, List<BankStatementLine> lines, CancellationToken cancellationToken)
    {
        var references = lines.Where(line => line.ReconciledEntryReference is not null).Select(line => line.ReconciledEntryReference!).ToList();
        var entries = await BookEntriesByReferenceAsync(companyId, accountId, references, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines.Where(line => line.ReconciledEntryReference is not null))
        {
            if (!entries.TryGetValue(line.ReconciledEntryReference!, out var entry) || entry.Status == JournalEntryStatus.Voided.ToString())
            {
                line.Unreconcile();
            }
        }
    }

    private static BankStatementLineRow ToRow(BankStatementLine line, IReadOnlyDictionary<string, BankBookEntry> entries)
    {
        var entry = line.ReconciledEntryReference is { } reference && entries.TryGetValue(reference, out var found) &&
            found.Status != JournalEntryStatus.Voided.ToString()
                ? found
                : null;
        return new BankStatementLineRow(
            line.Id, line.BookingDate, line.ValueDate, line.Description, line.Reference, line.Amount, line.Balance,
            entry, entry is not null && line.ReconciledAutomatically);
    }

    private async Task<(Guid Id, string Code, string Name)> ResolveAccountAsync(Guid companyId, string accountCode, CancellationToken cancellationToken)
    {
        var code = (accountCode ?? string.Empty).Trim();
        var account = await context.Accounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Code == code)
            .Select(item => new { item.Id, item.Code, item.Name, item.AllowsPosting })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null || !account.AllowsPosting || !(code.StartsWith("11", StringComparison.Ordinal) || code.StartsWith("12", StringComparison.Ordinal) || code.StartsWith("13", StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A conta '{accountCode}' não é uma conta de depósitos (11, 12 ou 13) de movimento.", nameof(accountCode));
        }

        return (account.Id, account.Code, account.Name);
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
