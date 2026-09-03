using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

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
    DateOnly? PostingLockDate);

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

/// <summary>
/// Manutenção do cadastro organizacional em desenvolvimento — empresas, utilizadores
/// e associações. A criação de empresa provisiona o plano standard da norma na mesma
/// transação e recusa duplicados de NIF.
/// </summary>
public sealed class OrganizationAdminService(
    CompanyAccountingProvisioner provisioner,
    AccountingDbContext context)
{
    public async Task<CompanyAdminData> CreateCompanyAsync(
        Guid accountingFirmId,
        string legalName,
        string taxRegistrationNumber,
        TaxRegion taxRegion,
        AccountingStandard accountingStandard,
        string baseCurrency,
        CancellationToken cancellationToken = default,
        Guid? associateUserId = null)
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

            // Em desenvolvimento, associa o utilizador atual à empresa que acabou de
            // criar para que ela apareça de imediato no seletor de empresa.
            if (associateUserId is { } userId)
            {
                var userExists = await context.UserAccounts
                    .AnyAsync(item => item.Id == userId, cancellationToken)
                    .ConfigureAwait(false);
                if (userExists)
                {
                    context.CompanyUsers.Add(new CompanyUser(company.Id, userId, CompanyUserRole.Owner));
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await provisioner.ProvisionAsync(company.Id, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            result = Project(company);
        }).ConfigureAwait(false);

        return result!;
    }

    public async Task<CompanyAdminData> UpdateCompanyAsync(
        Guid companyId,
        string legalName,
        TaxRegion taxRegion,
        string baseCurrency,
        bool isActive,
        DateOnly? postingLockDate,
        CancellationToken cancellationToken = default)
    {
        var company = await context.Companies
            .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("A empresa não existe.");

        company.Rename(legalName);
        company.ChangeTaxRegion(taxRegion);
        company.ChangeBaseCurrency(baseCurrency);
        company.SetPostingLockDate(postingLockDate);
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
        string displayName,
        string email,
        CancellationToken cancellationToken = default)
    {
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
        Guid userId,
        string displayName,
        string email,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var user = await context.UserAccounts
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("O utilizador não existe.");

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
        Guid companyId,
        Guid userId,
        CompanyUserRole role,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await context.Companies
            .AnyAsync(item => item.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        var userExists = await context.UserAccounts
            .AnyAsync(item => item.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!companyExists || !userExists)
        {
            throw new InvalidOperationException("A empresa ou o utilizador não existem.");
        }

        var membership = await context.CompanyUsers
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);
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
        company.PostingLockDate);
}
