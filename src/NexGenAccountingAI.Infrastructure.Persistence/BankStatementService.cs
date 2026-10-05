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

/// <summary>
/// Um movimento do extrato, com a conciliação a que pertence: os lançamentos que
/// o explicam (com os outros movimentos do mesmo grupo, se houver) e se foi
/// proposta pela aplicação.
/// </summary>
public sealed record BankStatementLineRow(
    Guid Id,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    string Description,
    string? Reference,
    decimal Amount,
    decimal? Balance,
    Guid? ReconciliationId,
    IReadOnlyList<BankBookEntry> ReconciledEntries,
    int ReconciledLineCount,
    bool ReconciledAutomatically)
{
    public bool Reconciled => ReconciledEntries.Count > 0;
}

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
/// Extratos bancários e conciliação (F3-19A, F3-19C). Importa um extrato
/// (camt.053 ou CSV) para uma conta de depósitos, sem duplicar; lista os
/// movimentos com a conciliação de cada um; concilia automaticamente pelo valor
/// e pela data (até <see cref="DateToleranceDays"/> dias, o mais próximo
/// primeiro, um para um) ou à mão — um ou mais movimentos com um ou mais
/// lançamentos, desde que os totais sejam iguais; e monta o mapa de
/// reconciliação. Uma conciliação deixa de contar quando um dos lançamentos é
/// anulado ou deixa de dar o mesmo total (foi corrigido).
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
                Unreconciled = group.Count(line => line.ReconciliationId == null),
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
        var groups = await GroupsAsync(companyId, account.Id, cancellationToken).ConfigureAwait(false);
        return lines.Select(line => ToRow(line, groups)).ToList();
    }

    /// <summary>
    /// Concilia os movimentos por conciliar do período com os lançamentos da conta
    /// ainda não conciliados que tenham o mesmo valor, a até
    /// <see cref="DateToleranceDays"/> dias — o mais próximo na data primeiro, um
    /// para um. Antes, desfaz as conciliações que deixaram de valer. Devolve
    /// quantos movimentos ficaram conciliados.
    /// </summary>
    public async Task<int?> AutoReconcileAsync(
        Guid userId, Guid companyId, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var account = await ResolveAccountAsync(companyId, accountCode, cancellationToken).ConfigureAwait(false);
        var groups = await ReleaseDeadGroupsAsync(companyId, account.Id, cancellationToken).ConfigureAwait(false);
        var lines = await context.BankStatementLines
            .Where(line => line.CompanyId == companyId && line.AccountId == account.Id &&
                line.BookingDate >= from && line.BookingDate <= to && line.ReconciliationId == null)
            .OrderBy(line => line.BookingDate)
            .ThenBy(line => line.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var used = groups.Values.SelectMany(group => group.Reconciliation.EntryReferences).ToHashSet(StringComparer.Ordinal);
        var candidates = (await BookEntriesAsync(
                companyId, account.Id, from.AddDays(-DateToleranceDays), to.AddDays(DateToleranceDays), cancellationToken)
            .ConfigureAwait(false))
            .Where(entry => !used.Contains(entry.Reference))
            .ToList();

        var matched = 0;
        foreach (var line in lines)
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

            var reconciliation = new BankReconciliation(Guid.NewGuid(), companyId, account.Id, [best.Reference], automatic: true);
            context.BankReconciliations.Add(reconciliation);
            line.AttachTo(reconciliation.Id);
            candidates.Remove(best);
            matched++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return matched;
    }

    /// <summary>Concilia um movimento com um lançamento (o caso simples de <see cref="ReconcileGroupAsync"/>).</summary>
    public async Task<BankStatementLineRow?> ReconcileAsync(
        Guid userId, Guid companyId, Guid lineId, string entryReference, CancellationToken cancellationToken = default)
    {
        var rows = await ReconcileGroupAsync(userId, companyId, [lineId], [entryReference], cancellationToken).ConfigureAwait(false);
        return rows?.Single();
    }

    /// <summary>
    /// Concilia um ou mais movimentos com um ou mais lançamentos escolhidos. Os
    /// movimentos têm de ser da mesma conta e estar por conciliar; os lançamentos
    /// têm de movimentar essa conta, não estar anulados nem já conciliados; e o
    /// total dos movimentos tem de ser igual ao efeito dos lançamentos na conta.
    /// </summary>
    public async Task<IReadOnlyList<BankStatementLineRow>?> ReconcileGroupAsync(
        Guid userId, Guid companyId, IReadOnlyCollection<Guid> lineIds, IReadOnlyCollection<string> entryReferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        ArgumentNullException.ThrowIfNull(entryReferences);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var ids = lineIds.Distinct().ToList();
        var references = entryReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0 || references.Count == 0)
        {
            throw new ArgumentException("Indique pelo menos um movimento e um lançamento.");
        }

        var lines = await context.BankStatementLines
            .Where(line => line.CompanyId == companyId && ids.Contains(line.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (lines.Count != ids.Count)
        {
            return null;
        }

        var accountId = lines[0].AccountId;
        if (lines.Any(line => line.AccountId != accountId))
        {
            throw new ArgumentException("Os movimentos têm de ser da mesma conta.", nameof(lineIds));
        }

        var groups = await ReleaseDeadGroupsAsync(companyId, accountId, cancellationToken).ConfigureAwait(false);
        if (lines.Any(line => line.ReconciliationId is not null))
        {
            throw new InvalidOperationException("Um dos movimentos já está conciliado: desfaça essa conciliação primeiro.");
        }

        var entries = await BookEntriesByReferenceAsync(companyId, accountId, references, cancellationToken).ConfigureAwait(false);
        var missing = references.Where(reference => !entries.TryGetValue(reference, out var entry) || entry.Status == JournalEntryStatus.Voided.ToString() || entry.Amount == 0m).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"{(missing.Count == 1 ? "O lançamento" : "Os lançamentos")} {string.Join(", ", missing)} não {(missing.Count == 1 ? "existe, está anulado ou não movimenta" : "existem, estão anulados ou não movimentam")} esta conta.", nameof(entryReferences));
        }

        var taken = references.Where(reference => groups.Values.Any(group => group.Reconciliation.EntryReferences.Contains(reference))).ToList();
        if (taken.Count > 0)
        {
            throw new InvalidOperationException($"O lançamento {string.Join(", ", taken)} já está conciliado com outro movimento do extrato.");
        }

        var linesTotal = lines.Sum(line => line.Amount);
        var entriesTotal = references.Sum(reference => entries[reference].Amount);
        if (linesTotal != entriesTotal)
        {
            throw new ArgumentException(
                $"Os movimentos somam {linesTotal:0.00} e os lançamentos movimentam a conta por {entriesTotal:0.00}: os totais têm de ser iguais.", nameof(entryReferences));
        }

        var reconciliation = new BankReconciliation(Guid.NewGuid(), companyId, accountId, references, automatic: false);
        context.BankReconciliations.Add(reconciliation);
        foreach (var line in lines)
        {
            line.AttachTo(reconciliation.Id);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var refreshed = await GroupsAsync(companyId, accountId, cancellationToken).ConfigureAwait(false);
        return lines.Select(line => ToRow(line, refreshed)).ToList();
    }

    /// <summary>Desfaz a conciliação do movimento — e a de todos os movimentos do mesmo grupo.</summary>
    public async Task<bool?> UnreconcileAsync(Guid userId, Guid companyId, Guid lineId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var line = await context.BankStatementLines
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == lineId, cancellationToken)
            .ConfigureAwait(false);
        if (line is null)
        {
            return false;
        }

        if (line.ReconciliationId is not { } reconciliationId)
        {
            return true;
        }

        await RemoveGroupsAsync([reconciliationId], cancellationToken).ConfigureAwait(false);
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
        var groups = await GroupsAsync(companyId, account.Id, cancellationToken).ConfigureAwait(false);

        // Na data do mapa só conta uma conciliação válida cujos lançamentos são todos até essa data.
        bool LiveAt(Guid? reconciliationId) =>
            reconciliationId is { } id && groups.TryGetValue(id, out var group) && group.Live && group.Entries.All(entry => entry.EntryDate <= date);

        var statementOnly = lines.Where(line => !LiveAt(line.ReconciliationId)).Select(line => ToRow(line, groups)).ToList();
        var liveGroups = lines.Where(line => LiveAt(line.ReconciliationId)).Select(line => line.ReconciliationId!.Value).Distinct().ToList();
        var reconciledReferences = liveGroups.SelectMany(id => groups[id].Reconciliation.EntryReferences).ToHashSet(StringComparer.Ordinal);

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
            statementOnly, bookOnly, lines.Count(line => LiveAt(line.ReconciliationId)));
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

    /// <summary>
    /// As conciliações da conta, com os lançamentos de cada uma e se ainda valem:
    /// todos os lançamentos existem, não estão anulados e dão o total dos
    /// movimentos do grupo.
    /// </summary>
    private async Task<Dictionary<Guid, GroupInfo>> GroupsAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken)
    {
        var reconciliations = await context.BankReconciliations
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (reconciliations.Count == 0)
        {
            return [];
        }

        var entries = await BookEntriesByReferenceAsync(
            companyId, accountId, reconciliations.SelectMany(item => item.EntryReferences).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var totals = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == accountId && line.ReconciliationId != null)
            .GroupBy(line => line.ReconciliationId!.Value)
            .Select(group => new { group.Key, Total = group.Sum(line => line.Amount), Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, cancellationToken)
            .ConfigureAwait(false);

        return reconciliations.ToDictionary(item => item.Id, item =>
        {
            var found = item.EntryReferences.Where(entries.ContainsKey).Select(reference => entries[reference]).ToList();
            var total = totals.GetValueOrDefault(item.Id);
            var live = found.Count == item.EntryReferences.Count &&
                found.All(entry => entry.Status != JournalEntryStatus.Voided.ToString()) &&
                total is not null &&
                found.Sum(entry => entry.Amount) == total.Total;
            return new GroupInfo(item, found, total?.Count ?? 0, live);
        });
    }

    /// <summary>Desfaz as conciliações que deixaram de valer e devolve as que ficam.</summary>
    private async Task<Dictionary<Guid, GroupInfo>> ReleaseDeadGroupsAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken)
    {
        var groups = await GroupsAsync(companyId, accountId, cancellationToken).ConfigureAwait(false);
        var dead = groups.Values.Where(group => !group.Live).Select(group => group.Reconciliation.Id).ToList();
        if (dead.Count > 0)
        {
            await RemoveGroupsAsync(dead, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var id in dead)
            {
                groups.Remove(id);
            }
        }

        return groups;
    }

    private async Task RemoveGroupsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var lines = await context.BankStatementLines
            .Where(line => line.ReconciliationId != null && ids.Contains(line.ReconciliationId.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var line in lines)
        {
            line.Detach();
        }

        var reconciliations = await context.BankReconciliations
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.BankReconciliations.RemoveRange(reconciliations);
    }

    private static BankStatementLineRow ToRow(BankStatementLine line, IReadOnlyDictionary<Guid, GroupInfo> groups)
    {
        var group = line.ReconciliationId is { } id && groups.TryGetValue(id, out var found) && found.Live ? found : null;
        return new BankStatementLineRow(
            line.Id, line.BookingDate, line.ValueDate, line.Description, line.Reference, line.Amount, line.Balance,
            group?.Reconciliation.Id, group?.Entries ?? [], group?.LineCount ?? 0, group?.Reconciliation.Automatic ?? false);
    }

    private sealed record GroupInfo(BankReconciliation Reconciliation, IReadOnlyList<BankBookEntry> Entries, int LineCount, bool Live);

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
