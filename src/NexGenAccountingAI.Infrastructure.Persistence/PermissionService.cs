using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>As permissões de um utilizador numa empresa, já resolvidas (ADR-0018).</summary>
public sealed record EffectivePermissions(
    bool IsMember,
    CompanyUserRole Role,
    bool IsAccountant,
    Guid? PermissionGroupId,
    IReadOnlySet<string> Keys)
{
    public static EffectivePermissions None { get; } = new(false, CompanyUserRole.Viewer, false, null, new HashSet<string>());

    public bool Has(string key) => key == PermissionCatalog.AnyMember ? IsMember : Keys.Contains(key);
}

public sealed record PermissionGroupData(
    Guid Id,
    Guid FirmId,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int Members);

/// <summary>
/// Resolve e mantém as permissões. Um utilizador que é «Contabilista» tem todas; o dono e o
/// administrador têm todas menos as que exigem a senha do contabilista; os restantes têm as do
/// grupo atribuído na empresa, e, sem grupo, as do grupo de origem da sua função (ADR-0018).
/// </summary>
public sealed class PermissionService(AccountingDbContext context)
{
    public async Task<EffectivePermissions> ResolveAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var row = await context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.UserId == userId && item.IsActive && item.User.IsActive)
            .Select(item => new
            {
                item.Role,
                item.User.IsAccountant,
                item.PermissionGroupId,
                GroupPermissions = item.PermissionGroup == null ? null : item.PermissionGroup.Permissions,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return EffectivePermissions.None;
        }

        HashSet<string> keys;
        // O contabilista tem tudo; só a função «Consulta», um limite posto de propósito na empresa, o trava.
        if (row.IsAccountant && row.Role != CompanyUserRole.Viewer)
        {
            keys = [.. PermissionCatalog.AllKeys];
        }
        else
        {
            var source = OrganizationAdminService.IsManager(row.Role)
                ? PermissionCatalog.SystemGroupPermissions(PermissionCatalog.FullSystemKey)
                : row.GroupPermissions
                    ?? PermissionCatalog.SystemGroupPermissions(row.Role == CompanyUserRole.Viewer ? PermissionCatalog.ViewerSystemKey : PermissionCatalog.FullSystemKey);
            keys = source.Where(key => PermissionCatalog.Exists(key) && !PermissionCatalog.IsAccountantOnly(key)).ToHashSet(StringComparer.Ordinal);
        }

        return new EffectivePermissions(true, row.Role, row.IsAccountant && row.Role != CompanyUserRole.Viewer, row.PermissionGroupId, keys);
    }

    /// <summary>Os grupos do gabinete (cria e atualiza os de origem). Só quem administra uma empresa do gabinete.</summary>
    public async Task<IReadOnlyList<PermissionGroupData>> ListGroupsAsync(Guid actingUserId, Guid firmId, CancellationToken cancellationToken = default)
    {
        await RequireFirmManagerAsync(actingUserId, firmId, cancellationToken).ConfigureAwait(false);
        await EnsureSystemGroupsAsync(firmId, cancellationToken).ConfigureAwait(false);
        var groups = await context.PermissionGroups
            .AsNoTracking()
            .Where(item => item.FirmId == firmId)
            .OrderBy(item => item.SystemKey == null).ThenBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var counts = await context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.PermissionGroupId != null && item.Company.AccountingFirmId == firmId)
            .GroupBy(item => item.PermissionGroupId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken)
            .ConfigureAwait(false);
        return groups.Select(group => Project(group, counts.GetValueOrDefault(group.Id))).ToList();
    }

    public async Task<PermissionGroupData> CreateGroupAsync(
        Guid actingUserId, Guid firmId, string name, string? description, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        await RequireFirmManagerAsync(actingUserId, firmId, cancellationToken).ConfigureAwait(false);
        var group = new PermissionGroup(Guid.NewGuid(), firmId, name, description, permissions);
        await EnsureNameFreeAsync(firmId, group.Name, null, cancellationToken).ConfigureAwait(false);
        context.PermissionGroups.Add(group);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Project(group, 0);
    }

    public async Task<PermissionGroupData> UpdateGroupAsync(
        Guid actingUserId, Guid groupId, string name, string? description, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        var group = await FindGroupAsync(groupId, cancellationToken).ConfigureAwait(false);
        await RequireFirmManagerAsync(actingUserId, group.FirmId, cancellationToken).ConfigureAwait(false);
        if (group.IsSystem)
        {
            throw new InvalidOperationException("Os grupos de origem não se alteram: copie-o para criar um grupo próprio.");
        }

        group.Update(name, description, permissions);
        await EnsureNameFreeAsync(group.FirmId, group.Name, group.Id, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var members = await context.CompanyUsers.CountAsync(item => item.PermissionGroupId == group.Id, cancellationToken).ConfigureAwait(false);
        return Project(group, members);
    }

    public async Task DeleteGroupAsync(Guid actingUserId, Guid groupId, CancellationToken cancellationToken = default)
    {
        var group = await FindGroupAsync(groupId, cancellationToken).ConfigureAwait(false);
        await RequireFirmManagerAsync(actingUserId, group.FirmId, cancellationToken).ConfigureAwait(false);
        if (group.IsSystem)
        {
            throw new InvalidOperationException("Os grupos de origem não se apagam.");
        }

        if (await context.CompanyUsers.AnyAsync(item => item.PermissionGroupId == group.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("O grupo está atribuído a utilizadores: atribua-lhes outro grupo antes de o apagar.");
        }

        context.PermissionGroups.Remove(group);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>O grupo existe e é do gabinete da empresa (para a atribuição).</summary>
    public async Task<bool> GroupBelongsToCompanyFirmAsync(Guid groupId, Guid companyId, CancellationToken cancellationToken = default) =>
        await context.PermissionGroups.AnyAsync(
            group => group.Id == groupId && context.Companies.Any(company => company.Id == companyId && company.AccountingFirmId == group.FirmId),
            cancellationToken).ConfigureAwait(false);

    private async Task EnsureSystemGroupsAsync(Guid firmId, CancellationToken cancellationToken)
    {
        var existing = await context.PermissionGroups
            .Where(item => item.FirmId == firmId && item.SystemKey != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var changed = false;
        foreach (var (systemKey, name, description) in PermissionCatalog.SystemGroups)
        {
            var permissions = PermissionCatalog.SystemGroupPermissions(systemKey);
            var group = existing.SingleOrDefault(item => item.SystemKey == systemKey);
            if (group is null)
            {
                context.PermissionGroups.Add(new PermissionGroup(Guid.NewGuid(), firmId, name, description, permissions, systemKey));
                changed = true;
            }
            else if (!group.Permissions.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(permissions.OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                group.Update(group.Name, group.Description, permissions);
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Só quem é dono ou administrador de uma empresa do gabinete passa (404 se nem tem acesso).</summary>
    public async Task RequireFirmManagerAsync(Guid actingUserId, Guid firmId, CancellationToken cancellationToken)
    {
        var anyAccess = await context.CompanyUsers
            .AnyAsync(item => item.UserId == actingUserId && item.IsActive && item.Company.AccountingFirmId == firmId, cancellationToken)
            .ConfigureAwait(false);
        if (!anyAccess)
        {
            throw new KeyNotFoundException("O gabinete não existe.");
        }

        var manages = await context.CompanyUsers
            .AnyAsync(
                item => item.UserId == actingUserId && item.IsActive && item.Company.AccountingFirmId == firmId &&
                    (item.Role == CompanyUserRole.Owner || item.Role == CompanyUserRole.Administrator),
                cancellationToken)
            .ConfigureAwait(false);
        if (!manages)
        {
            throw new OrganizationAccessDeniedException("Só o dono ou um administrador de uma empresa do gabinete gere os grupos de permissões.");
        }
    }

    private async Task<PermissionGroup> FindGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await context.PermissionGroups.SingleOrDefaultAsync(item => item.Id == groupId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("O grupo não existe.");

    private async Task EnsureNameFreeAsync(Guid firmId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var taken = await context.PermissionGroups
            .AnyAsync(item => item.FirmId == firmId && item.Name == name && item.Id != exceptId, cancellationToken)
            .ConfigureAwait(false);
        if (taken)
        {
            throw new InvalidOperationException("Já existe um grupo com esse nome.");
        }
    }

    private static PermissionGroupData Project(PermissionGroup group, int members) =>
        new(group.Id, group.FirmId, group.Name, group.Description, group.IsSystem, group.Permissions, members);
}
