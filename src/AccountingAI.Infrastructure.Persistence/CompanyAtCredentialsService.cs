using AccountingAI.Application.At;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>As credenciais e as definições da AT de uma empresa, sem a senha.</summary>
public sealed record CompanyAtCredentialsView(
    bool Configured,
    string? SubUser,
    string Periodicity,
    string Environment,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Guarda e lê as credenciais e as definições da AT por empresa (subutilizador,
/// senha, periodicidade da declaração periódica, ambiente da AT). A senha é
/// cifrada com o <see cref="IAtSecretProtector"/> antes de tocar na base de
/// dados e nunca é devolvida.
/// </summary>
public sealed class CompanyAtCredentialsService(AccountingDbContext context, IAtSecretProtector protector)
{
    public async Task<CompanyAtCredentialsView?> GetAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = await context.CompanyAtCredentials
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new
            {
                item.SubUser,
                item.Periodicity,
                item.Environment,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(item, "ModifiedAtUtc"),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? new CompanyAtCredentialsView(false, null, VatFilingPeriodicity.Mensal.ToString(), AtEnvironment.Testes.ToString(), null)
            : new CompanyAtCredentialsView(true, row.SubUser, row.Periodicity.ToString(), row.Environment.ToString(), row.ModifiedAtUtc ?? row.CreatedAtUtc);
    }

    public async Task<CompanyAtCredentialsView?> SaveAsync(
        Guid userId, Guid companyId, string subUser, string password,
        VatFilingPeriodicity periodicity, AtEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var encrypted = protector.IsProtected(password) ? password : protector.Protect(password);
        var row = await context.CompanyAtCredentials
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            context.CompanyAtCredentials.Add(CompanyAtCredentials.Create(companyId, subUser, encrypted, periodicity, environment));
        }
        else
        {
            row.Update(subUser, encrypted, periodicity, environment);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var row = await context.CompanyAtCredentials
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        context.CompanyAtCredentials.Remove(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
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
