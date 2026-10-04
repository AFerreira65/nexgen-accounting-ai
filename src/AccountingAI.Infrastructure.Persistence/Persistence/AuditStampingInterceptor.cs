using AccountingAI.Application.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AccountingAI.Infrastructure.Persistence.Persistence;

/// <summary>
/// Carimba as colunas-sombra de auditoria ao gravar: <c>CreatedAtUtc</c>/
/// <c>CreatedBy</c> nas inserções, <c>ModifiedAtUtc</c>/<c>ModifiedBy</c> nas
/// alterações. O «quem» vem do <see cref="ICurrentUserAccessor"/>; sem
/// utilizador conhecido, só o «quando» é gravado. Não sobrescreve o carimbo de
/// criação numa alteração.
/// </summary>
public sealed class AuditStampingInterceptor(ICurrentUserAccessor currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!AuditShadowProperties.IsAudited(entry.Metadata))
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property("CreatedAtUtc").CurrentValue = now;
                if (userId is { } creator)
                {
                    entry.Property("CreatedBy").CurrentValue = creator;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property("ModifiedAtUtc").CurrentValue = now;
                if (userId is { } editor)
                {
                    entry.Property("ModifiedBy").CurrentValue = editor;
                }

                entry.Property("CreatedAtUtc").IsModified = false;
                entry.Property("CreatedBy").IsModified = false;
            }
        }
    }
}
