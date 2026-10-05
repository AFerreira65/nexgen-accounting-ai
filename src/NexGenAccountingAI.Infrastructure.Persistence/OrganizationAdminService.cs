using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record CompanyAdminData(
    Guid Id,
    Guid AccountingFirmId,
    string LegalName,
    string TaxRegistrationNumber,
    string TaxRegion,
    string BaseCurrency,
    string AccountingStandard,
    string? AccountingSetupVersion,
    bool IsActive,
    DateOnly? PostingLockDate,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

public sealed record UserAdminData(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsActive);

public sealed record MembershipData(
    Guid CompanyId,
    Guid UserId,
    string Role,
    bool IsActive);

/// <summary>Quem pede não pode fazer esta alteração no cadastro (HTTP 403).</summary>
public sealed class OrganizationAccessDeniedException(string message) : Exception(message);

/// <summary>
/// Manutenção do cadastro organizacional — empresas, utilizadores e associações.
/// A criação de empresa provisiona o plano standard da norma na mesma transação e
/// recusa duplicados de NIF.
/// <para>
/// Quem pode o quê: cada operação recebe o utilizador da sessão (<c>actingUserId</c>).
/// Só o dono ou um administrador ativo de uma empresa a altera e gere os acessos
/// a ela; só um dono dá ou tira a função de dono, e a empresa nunca fica sem um
/// dono ativo. Criar empresas num gabinete pede quem já administre uma empresa
/// desse gabinete; criar utilizadores, quem administre alguma empresa. Uma
/// empresa a que o utilizador não tem acesso responde como inexistente
/// (<see cref="KeyNotFoundException"/>); com acesso mas sem a função, recusa
/// (<see cref="OrganizationAccessDeniedException"/>).
/// </para>
/// </summary>
public sealed class OrganizationAdminService(
    CompanyAccountingProvisioner provisioner,
    AccountingDbContext context)
{
    private static readonly CompanyUserRole[] ManagerRoles = [CompanyUserRole.Owner, CompanyUserRole.Administrator];

    /// <summary>A função ativa do utilizador na empresa, ou null sem acesso.</summary>
    public Task<CompanyUserRole?> ActiveRoleAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default) =>
        context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.UserId == userId && item.IsActive && item.User.IsActive)
            .Select(item => (CompanyUserRole?)item.Role)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Se o utilizador é dono ou administrador ativo da empresa.</summary>
    public static bool IsManager(CompanyUserRole? role) => role is CompanyUserRole.Owner or CompanyUserRole.Administrator;

    private async Task<CompanyUserRole> RequireManagerAsync(Guid actingUserId, Guid companyId, CancellationToken cancellationToken)
    {
        var role = await ActiveRoleAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("A empresa não existe.");
        return IsManager(role)
            ? role
            : throw new OrganizationAccessDeniedException("Só o dono ou um administrador da empresa pode fazer esta alteração.");
    }

    private Task<bool> ManagesAnyCompanyAsync(Guid actingUserId, Guid? firmId, CancellationToken cancellationToken) =>
        context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(item => item.UserId == actingUserId && item.IsActive && item.User.IsActive &&
                ManagerRoles.Contains(item.Role) &&
                (firmId == null || item.Company.AccountingFirmId == firmId), cancellationToken);
    public async Task<CompanyAdminData> CreateCompanyAsync(
        Guid actingUserId,
        Guid accountingFirmId,
        string legalName,
        string taxRegistrationNumber,
        TaxRegion taxRegion,
        AccountingStandard accountingStandard,
        string baseCurrency,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaxNumber = (taxRegistrationNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedTaxNumber))
        {
            throw new ArgumentException("O NIF é obrigatório.", nameof(taxRegistrationNumber));
        }

        var firmExists = await context.AccountingFirms
            .AnyAsync(item => item.Id == accountingFirmId, cancellationToken)
            .ConfigureAwait(false);
        if (!firmExists)
        {
            throw new InvalidOperationException("O gabinete indicado não existe.");
        }

        if (!await ManagesAnyCompanyAsync(actingUserId, accountingFirmId, cancellationToken).ConfigureAwait(false))
        {
            throw new OrganizationAccessDeniedException("Só quem é dono ou administrador de uma empresa deste gabinete pode criar empresas nele.");
        }

        CompanyAdminData? result = null;
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var duplicate = await context.Companies
                .AnyAsync(item => item.TaxRegistrationNumber == normalizedTaxNumber, cancellationToken)
                .ConfigureAwait(false);
            if (duplicate)
            {
                throw new InvalidOperationException($"Já existe uma empresa com o NIF {normalizedTaxNumber}.");
            }

            var company = new Company(
                Guid.NewGuid(),
                accountingFirmId,
                legalName,
                normalizedTaxNumber,
                taxRegion,
                baseCurrency,
                accountingStandard);
            context.Companies.Add(company);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Quem cria fica dono: a empresa aparece logo no seletor e nunca fica sem dono.
            context.CompanyUsers.Add(new CompanyUser(company.Id, actingUserId, CompanyUserRole.Owner));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await provisioner.ProvisionAsync(company.Id, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            result = Project(company);
        }).ConfigureAwait(false);

        return result!;
    }

    public async Task<CompanyAdminData> UpdateCompanyAsync(
        Guid actingUserId,
        Guid companyId,
        string legalName,
        TaxRegion taxRegion,
        string baseCurrency,
        bool isActive,
        DateOnly? postingLockDate,
        CancellationToken cancellationToken = default,
        string? addressDetail = null,
        string? city = null,
        string? postalCode = null)
    {
        await RequireManagerAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var company = await context.Companies
            .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("A empresa não existe.");

        company.Rename(legalName);
        company.ChangeTaxRegion(taxRegion);
        company.ChangeBaseCurrency(baseCurrency);
        company.SetPostingLockDate(postingLockDate);
        // Um campo de morada omitido (`null`) mantém o que está; em branco, limpa.
        if (addressDetail is not null || city is not null || postalCode is not null)
        {
            company.SetAddress(addressDetail ?? company.AddressDetail, city ?? company.City, postalCode ?? company.PostalCode);
        }

        if (isActive)
        {
            company.Reactivate();
        }
        else
        {
            company.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Project(company);
    }

    public async Task<UserAdminData> CreateUserAsync(
        Guid actingUserId,
        string displayName,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (!await ManagesAnyCompanyAsync(actingUserId, null, cancellationToken).ConfigureAwait(false))
        {
            throw new OrganizationAccessDeniedException("Só quem é dono ou administrador de uma empresa pode criar utilizadores.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var inUse = await context.UserAccounts
            .AnyAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);
        if (inUse)
        {
            throw new InvalidOperationException("Já existe um utilizador com esse email.");
        }

        var user = new UserAccount(Guid.NewGuid(), displayName, email);
        context.UserAccounts.Add(user);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new UserAdminData(user.Id, user.DisplayName, user.Email, user.IsActive);
    }

    public async Task<UserAdminData> UpdateUserAsync(
        Guid actingUserId,
        Guid userId,
        string displayName,
        string email,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var user = await context.UserAccounts
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("O utilizador não existe.");

        if (userId == actingUserId)
        {
            if (!isActive)
            {
                throw new InvalidOperationException("Não pode desativar o seu próprio utilizador.");
            }
        }
        else
        {
            await RequireManagesUserAsync(actingUserId, userId, cancellationToken).ConfigureAwait(false);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var takenByAnother = await context.UserAccounts
            .AnyAsync(item => item.NormalizedEmail == normalizedEmail && item.Id != userId, cancellationToken)
            .ConfigureAwait(false);
        if (takenByAnother)
        {
            throw new InvalidOperationException("Já existe outro utilizador com esse email.");
        }

        user.Rename(displayName);
        user.ChangeEmail(email);
        if (isActive)
        {
            user.Reactivate();
        }
        else
        {
            user.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new UserAdminData(user.Id, user.DisplayName, user.Email, user.IsActive);
    }

    public async Task<MembershipData> SetMembershipAsync(
        Guid actingUserId,
        Guid companyId,
        Guid userId,
        CompanyUserRole role,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var actingRole = await RequireManagerAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var userExists = await context.UserAccounts
            .AnyAsync(item => item.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!userExists)
        {
            throw new InvalidOperationException("O utilizador não existe.");
        }

        var membership = await context.CompanyUsers
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        var wasOwner = membership is { Role: CompanyUserRole.Owner };
        if (actingRole != CompanyUserRole.Owner && (wasOwner || role == CompanyUserRole.Owner))
        {
            throw new OrganizationAccessDeniedException("Só um dono pode dar ou tirar a função de dono, ou alterar o acesso de um dono.");
        }

        if (membership is { Role: CompanyUserRole.Owner, IsActive: true } && (role != CompanyUserRole.Owner || !isActive))
        {
            var otherOwners = await context.CompanyUsers
                .CountAsync(item => item.CompanyId == companyId && item.UserId != userId &&
                    item.Role == CompanyUserRole.Owner && item.IsActive && item.User.IsActive, cancellationToken)
                .ConfigureAwait(false);
            if (otherOwners == 0)
            {
                throw new InvalidOperationException("A empresa ficaria sem nenhum dono ativo: dê primeiro a função de dono a outro utilizador.");
            }
        }
        if (membership is null)
        {
            membership = new CompanyUser(companyId, userId, role);
            context.CompanyUsers.Add(membership);
        }
        else
        {
            membership.ChangeRole(role);
        }

        if (isActive)
        {
            membership.Reactivate();
        }
        else
        {
            membership.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new MembershipData(companyId, userId, membership.Role.ToString(), membership.IsActive);
    }

    private static CompanyAdminData Project(Company company) => new(
        company.Id,
        company.AccountingFirmId,
        company.LegalName,
        company.TaxRegistrationNumber,
        company.TaxRegion.ToString(),
        company.BaseCurrency,
        company.AccountingStandard.ToString(),
        company.AccountingSetupVersion,
        company.IsActive,
        company.PostingLockDate,
        company.AddressDetail,
        company.City,
        company.PostalCode);

    /// <summary>
    /// Para alterar outro utilizador — o nome, o email ou desativá-lo, o que vale
    /// em todas as empresas dele — quem pede tem de administrar todas as empresas
    /// onde ele tem acesso ativo, e ser dono naquelas onde ele é dono. Um
    /// utilizador ainda sem acessos pode ser alterado por quem administre alguma empresa.
    /// </summary>
    private async Task RequireManagesUserAsync(Guid actingUserId, Guid userId, CancellationToken cancellationToken)
    {
        var targetRoles = await context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Select(item => new { item.CompanyId, item.Role })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (targetRoles.Count == 0)
        {
            if (!await ManagesAnyCompanyAsync(actingUserId, null, cancellationToken).ConfigureAwait(false))
            {
                throw new OrganizationAccessDeniedException("Só quem é dono ou administrador de uma empresa pode alterar utilizadores.");
            }

            return;
        }

        var companyIds = targetRoles.Select(item => item.CompanyId).ToList();
        var actingRoles = await context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.UserId == actingUserId && item.IsActive && item.User.IsActive && companyIds.Contains(item.CompanyId))
            .ToDictionaryAsync(item => item.CompanyId, item => item.Role, cancellationToken)
            .ConfigureAwait(false);
        if (actingRoles.Count == 0)
        {
            throw new KeyNotFoundException("O utilizador não existe.");
        }

        foreach (var target in targetRoles)
        {
            var allowed = actingRoles.TryGetValue(target.CompanyId, out var acting) &&
                (acting == CompanyUserRole.Owner || (acting == CompanyUserRole.Administrator && target.Role != CompanyUserRole.Owner));
            if (!allowed)
            {
                throw new OrganizationAccessDeniedException("Este utilizador tem acesso a empresas que não administra, ou é dono onde não é: só quem as administra o pode alterar.");
            }
        }
    }
}
