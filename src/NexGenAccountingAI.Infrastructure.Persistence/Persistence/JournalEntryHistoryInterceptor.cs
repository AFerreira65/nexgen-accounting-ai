using System.Text.Json;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence;

/// <summary>O estado de um lançamento antes de uma alteração, tal como fica guardado no histórico.</summary>
public sealed record JournalEntrySnapshot(
    Guid JournalId,
    Guid? DocumentTypeId,
    DateOnly EntryDate,
    string DocumentReference,
    DateOnly? DocumentDate,
    string Description,
    string? Notes,
    JournalEntryStatus Status,
    EntrySource Source,
    decimal TotalAmount,
    DateTimeOffset? CreatedAtUtc,
    Guid? CreatedBy,
    IReadOnlyList<JournalEntrySnapshotLine> Lines);

public sealed record JournalEntrySnapshotLine(
    int Sequence, Guid AccountId, string Description, decimal Debit, decimal Credit, Guid? PartyId, Guid? VatCodeId, Guid? CostCenterId);

/// <summary>
/// Guarda o histórico de alterações dos lançamentos (F4-03). Em cada gravação, por
/// cada lançamento <b>já existente</b> que vai ser removido ou alterado, acrescenta um
/// <see cref="JournalEntryChange"/> com o estado anterior. Vale para todos os caminhos
/// (correção, conferência, reabertura, anulação, reabertura do apuramento…), porque
/// atua na gravação e não em cada serviço. Os lançamentos novos não geram histórico
/// (a criação já fica nos carimbos de auditoria), nem as alterações em massa feitas
/// por SQL direto, que não mexem em valores.
/// </summary>
public sealed class JournalEntryHistoryInterceptor(ICurrentUserAccessor currentUser) : SaveChangesInterceptor
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            foreach (var pending in Pending(context))
            {
                var lines = pending.LoadedLines ?? context.Set<JournalEntryLine>().AsNoTracking()
                    .Where(line => line.JournalEntryId == pending.EntryId)
                    .OrderBy(line => line.Sequence)
                    .Select(line => new JournalEntrySnapshotLine(line.Sequence, line.AccountId, line.Description, line.Debit, line.Credit, line.PartyId, line.VatCodeId, line.CostCenterId))
                    .ToList();
                context.Add(pending.ToChange(lines, currentUser.UserId));
            }
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            foreach (var pending in Pending(context))
            {
                var lines = pending.LoadedLines ?? await context.Set<JournalEntryLine>().AsNoTracking()
                    .Where(line => line.JournalEntryId == pending.EntryId)
                    .OrderBy(line => line.Sequence)
                    .Select(line => new JournalEntrySnapshotLine(line.Sequence, line.AccountId, line.Description, line.Debit, line.Credit, line.PartyId, line.VatCodeId, line.CostCenterId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                context.Add(pending.ToChange(lines, currentUser.UserId));
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private static List<PendingChange> Pending(DbContext context)
    {
        var pending = new List<PendingChange>();
        foreach (var entry in context.ChangeTracker.Entries<JournalEntry>().ToList())
        {
            if (entry.State is not (EntityState.Deleted or EntityState.Modified))
            {
                continue;
            }

            // Só conta uma alteração real: os carimbos de auditoria e a versão da linha não são conteúdo.
            if (entry.State == EntityState.Modified && !entry.Properties.Any(property =>
                    property.IsModified && !property.Metadata.IsShadowProperty() && property.Metadata.Name != nameof(JournalEntry.RowVersion)))
            {
                continue;
            }

            T Original<T>(string name) => (T)entry.OriginalValues[name]!;
            var statusBefore = Original<JournalEntryStatus>(nameof(JournalEntry.Status));
            var action = entry.State == EntityState.Deleted
                ? JournalEntryChangeAction.Removed
                : (statusBefore, entry.Entity.Status) switch
                {
                    (not JournalEntryStatus.Voided, JournalEntryStatus.Voided) => JournalEntryChangeAction.Voided,
                    (not JournalEntryStatus.Reviewed, JournalEntryStatus.Reviewed) => JournalEntryChangeAction.Reviewed,
                    (JournalEntryStatus.Reviewed, JournalEntryStatus.PendingReview) => JournalEntryChangeAction.Reopened,
                    _ => JournalEntryChangeAction.Changed,
                };

            pending.Add(new PendingChange(
                entry.Entity.Id,
                Original<Guid>(nameof(JournalEntry.CompanyId)),
                Original<string>(nameof(JournalEntry.Reference)),
                action,
                new JournalEntrySnapshot(
                    Original<Guid>(nameof(JournalEntry.JournalId)),
                    (Guid?)entry.OriginalValues[nameof(JournalEntry.DocumentTypeId)],
                    Original<DateOnly>(nameof(JournalEntry.EntryDate)),
                    Original<string>(nameof(JournalEntry.DocumentReference)),
                    (DateOnly?)entry.OriginalValues[nameof(JournalEntry.DocumentDate)],
                    Original<string>(nameof(JournalEntry.Description)),
                    (string?)entry.OriginalValues[nameof(JournalEntry.Notes)],
                    statusBefore,
                    Original<EntrySource>(nameof(JournalEntry.Source)),
                    Original<decimal>(nameof(JournalEntry.TotalAmount)),
                    Shadow<DateTimeOffset?>(entry, "CreatedAtUtc"),
                    Shadow<Guid?>(entry, "CreatedBy"),
                    []),
                LoadedLines(context, entry)));
        }

        return pending;
    }

    private static T? Shadow<T>(EntityEntry entry, string name) =>
        entry.Metadata.FindProperty(name) is null ? default : (T?)entry.OriginalValues[name];

    /// <summary>As linhas, com os valores de antes, se o lançamento as tem carregadas; senão nulo (lêem-se da base).</summary>
    private static List<JournalEntrySnapshotLine>? LoadedLines(DbContext context, EntityEntry<JournalEntry> entry)
    {
        if (!entry.Collection(item => item.Lines).IsLoaded)
        {
            return null;
        }

        return entry.Entity.Lines
            .Select(line => context.Entry(line))
            // Uma linha acrescentada nesta gravação não fazia parte do estado anterior.
            .Where(line => line.State != EntityState.Added)
            .Select(line => new JournalEntrySnapshotLine(
                (int)line.OriginalValues[nameof(JournalEntryLine.Sequence)]!,
                (Guid)line.OriginalValues[nameof(JournalEntryLine.AccountId)]!,
                (string)line.OriginalValues[nameof(JournalEntryLine.Description)]!,
                (decimal)line.OriginalValues[nameof(JournalEntryLine.Debit)]!,
                (decimal)line.OriginalValues[nameof(JournalEntryLine.Credit)]!,
                (Guid?)line.OriginalValues[nameof(JournalEntryLine.PartyId)],
                (Guid?)line.OriginalValues[nameof(JournalEntryLine.VatCodeId)],
                (Guid?)line.OriginalValues[nameof(JournalEntryLine.CostCenterId)]))
            .OrderBy(line => line.Sequence)
            .ToList();
    }

    private sealed record PendingChange(
        Guid EntryId, Guid CompanyId, string Reference, JournalEntryChangeAction Action, JournalEntrySnapshot Header,
        List<JournalEntrySnapshotLine>? LoadedLines)
    {
        public JournalEntryChange ToChange(IReadOnlyList<JournalEntrySnapshotLine> lines, Guid? userId) => new(
            Guid.NewGuid(), CompanyId, Reference, Action, DateTimeOffset.UtcNow, userId,
            JsonSerializer.Serialize(Header with { Lines = lines }, JsonOptions));
    }
}
