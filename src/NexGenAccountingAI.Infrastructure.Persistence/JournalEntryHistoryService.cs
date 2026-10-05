using System.Text.Json;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record JournalEntryHistoryLine(
    int Sequence, string AccountCode, string AccountName, string Description, decimal Debit, decimal Credit,
    string? PartyName, string? VatCode, string? CostCenter);

/// <summary>O lançamento como estava antes de uma alteração, com os códigos e nomes já resolvidos.</summary>
public sealed record JournalEntryHistoryVersion(
    DateOnly EntryDate,
    string JournalCode,
    string? DocumentTypeCode,
    string DocumentReference,
    DateOnly? DocumentDate,
    string Description,
    string? Notes,
    string Status,
    decimal TotalAmount,
    DateTimeOffset? CreatedAtUtc,
    string? CreatedByName,
    IReadOnlyList<JournalEntryHistoryLine> Lines);

public sealed record JournalEntryHistoryRow(
    Guid Id, string Action, DateTimeOffset ChangedAtUtc, string? ChangedByName, JournalEntryHistoryVersion Before);

/// <summary>
/// Lê o histórico de alterações de um lançamento (F4-03), da mais recente para a mais
/// antiga. O histórico é gravado pelo <see cref="JournalEntryHistoryInterceptor"/>.
/// </summary>
public sealed class JournalEntryHistoryService(AccountingDbContext context)
{
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IReadOnlyList<JournalEntryHistoryRow>?> ListAsync(
        Guid userId, Guid companyId, string reference, CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.CompanyId == companyId && item.IsActive && item.Company.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var normalized = (reference ?? string.Empty).Trim().ToUpperInvariant();
        var changes = await context.JournalEntryChanges
            .AsNoTracking()
            .Where(change => change.CompanyId == companyId && change.Reference == normalized)
            .OrderByDescending(change => change.ChangedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (changes.Count == 0)
        {
            return [];
        }

        var snapshots = changes
            .Select(change => (Change: change, Snapshot: JsonSerializer.Deserialize<JournalEntrySnapshot>(change.BeforeJson, JournalEntryHistoryInterceptor.JsonOptions)!))
            .ToList();
        var lines = snapshots.SelectMany(item => item.Snapshot.Lines).ToList();

        async Task<Dictionary<Guid, T>> Lookup<T>(IQueryable<KeyValuePair<Guid, T>> query) =>
            (await query.ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(pair => pair.Key, pair => pair.Value);

        var accountIds = lines.Select(line => line.AccountId).Distinct().ToList();
        var accounts = await Lookup(context.Accounts.AsNoTracking()
            .Where(account => account.CompanyId == companyId && accountIds.Contains(account.Id))
            .Select(account => new KeyValuePair<Guid, (string Code, string Name)>(account.Id, new ValueTuple<string, string>(account.Code, account.Name)))).ConfigureAwait(false);
        var partyIds = lines.Where(line => line.PartyId is not null).Select(line => line.PartyId!.Value).Distinct().ToList();
        var parties = await Lookup(context.Parties.AsNoTracking()
            .Where(party => party.CompanyId == companyId && partyIds.Contains(party.Id))
            .Select(party => new KeyValuePair<Guid, string>(party.Id, party.Name))).ConfigureAwait(false);
        var vatIds = lines.Where(line => line.VatCodeId is not null).Select(line => line.VatCodeId!.Value).Distinct().ToList();
        var vatCodes = await Lookup(context.VatCodes.AsNoTracking()
            .Where(code => code.CompanyId == companyId && vatIds.Contains(code.Id))
            .Select(code => new KeyValuePair<Guid, string>(code.Id, code.Code))).ConfigureAwait(false);
        var centerIds = lines.Where(line => line.CostCenterId is not null).Select(line => line.CostCenterId!.Value).Distinct().ToList();
        var centers = await Lookup(context.CostCenters.AsNoTracking()
            .Where(center => center.CompanyId == companyId && centerIds.Contains(center.Id))
            .Select(center => new KeyValuePair<Guid, string>(center.Id, center.Code))).ConfigureAwait(false);
        var journalIds = snapshots.Select(item => item.Snapshot.JournalId).Distinct().ToList();
        var journals = await Lookup(context.Journals.AsNoTracking()
            .Where(journal => journal.CompanyId == companyId && journalIds.Contains(journal.Id))
            .Select(journal => new KeyValuePair<Guid, string>(journal.Id, journal.Code))).ConfigureAwait(false);
        var typeIds = snapshots.Where(item => item.Snapshot.DocumentTypeId is not null).Select(item => item.Snapshot.DocumentTypeId!.Value).Distinct().ToList();
        var documentTypes = await Lookup(context.AccountingDocumentTypes.AsNoTracking()
            .Where(type => type.CompanyId == companyId && typeIds.Contains(type.Id))
            .Select(type => new KeyValuePair<Guid, string>(type.Id, type.Code))).ConfigureAwait(false);
        var userIds = snapshots.SelectMany(item => new[] { item.Change.ChangedBy, item.Snapshot.CreatedBy })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var users = await Lookup(context.UserAccounts.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new KeyValuePair<Guid, string>(user.Id, user.DisplayName))).ConfigureAwait(false);

        string? UserName(Guid? id) => id is { } value ? users.GetValueOrDefault(value) : null;

        return snapshots.Select(item => new JournalEntryHistoryRow(
            item.Change.Id,
            item.Change.Action.ToString(),
            item.Change.ChangedAtUtc,
            UserName(item.Change.ChangedBy),
            new JournalEntryHistoryVersion(
                item.Snapshot.EntryDate,
                journals.GetValueOrDefault(item.Snapshot.JournalId) ?? "?",
                item.Snapshot.DocumentTypeId is { } typeId ? documentTypes.GetValueOrDefault(typeId) : null,
                item.Snapshot.DocumentReference,
                item.Snapshot.DocumentDate,
                item.Snapshot.Description,
                item.Snapshot.Notes,
                item.Snapshot.Status.ToString(),
                item.Snapshot.TotalAmount,
                item.Snapshot.CreatedAtUtc,
                UserName(item.Snapshot.CreatedBy),
                item.Snapshot.Lines.Select(line => new JournalEntryHistoryLine(
                    line.Sequence,
                    accounts.TryGetValue(line.AccountId, out var account) ? account.Code : "?",
                    accounts.TryGetValue(line.AccountId, out var named) ? named.Name : "Conta removida",
                    line.Description,
                    line.Debit,
                    line.Credit,
                    line.PartyId is { } partyId ? parties.GetValueOrDefault(partyId) : null,
                    line.VatCodeId is { } vatId ? vatCodes.GetValueOrDefault(vatId) : null,
                    line.CostCenterId is { } centerId ? centers.GetValueOrDefault(centerId) : null)).ToList()))).ToList();
    }
}
