using Microsoft.EntityFrameworkCore;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>A configuração geral da AT como o ecrã a vê: nunca traz a senha, só se há uma.</summary>
/// <param name="CanEdit">Se o utilizador da sessão a pode alterar; sem isso, os restantes campos vêm vazios.</param>
public sealed record AtGeneralSettingsView(
    bool CanEdit,
    string? CertificateDirectory,
    string? CertificateFileName,
    string? PublicKeyDirectory,
    string? PublicKeyFileName,
    bool PasswordConfigured,
    DateTimeOffset? UpdatedAtUtc);

/// <param name="Password">A senha nova; vazia ou <c>null</c> mantém a que está guardada.</param>
/// <param name="RemovePassword">Retira a senha guardada (um certificado sem senha). Ignorado se vier uma senha nova.</param>
public sealed record AtGeneralSettingsInput(
    string? CertificateDirectory,
    string? CertificateFileName,
    string? PublicKeyDirectory,
    string? PublicKeyFileName,
    string? Password,
    bool RemovePassword);

/// <summary>
/// A configuração geral da AT — onde estão o certificado e a chave pública, e a senha do
/// certificado, cifrada — comum a toda a instalação. Como vale para todas as empresas,
/// só quem é dono ou administrador de alguma empresa a vê e a altera (a mesma regra
/// de criar utilizadores, ADR-0014). Lê-se também de forma síncrona
/// (<see cref="IAtGeneralSettingsReader"/>) para a camada que fala com a AT.
/// </summary>
public sealed class AtGeneralSettingsService(
    AccountingDbContext context,
    IAtSecretProtector protector,
    IAtSettingsInvalidator? invalidator = null) : IAtGeneralSettingsReader
{
    public async Task<AtGeneralSettingsView> GetAsync(Guid sessionUserId, CancellationToken cancellationToken = default)
    {
        if (!await CanEditAsync(sessionUserId, cancellationToken).ConfigureAwait(false))
        {
            return new AtGeneralSettingsView(false, null, null, null, null, false, null);
        }

        var row = await context.AtGeneralSettings
            .AsNoTracking()
            .Select(item => new
            {
                item.CertificateDirectory,
                item.CertificateFileName,
                item.PublicKeyDirectory,
                item.PublicKeyFileName,
                item.EncryptedCertificatePassword,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(item, "ModifiedAtUtc"),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? new AtGeneralSettingsView(true, null, null, null, null, false, null)
            : new AtGeneralSettingsView(
                true, row.CertificateDirectory, row.CertificateFileName, row.PublicKeyDirectory, row.PublicKeyFileName,
                !string.IsNullOrEmpty(row.EncryptedCertificatePassword), row.ModifiedAtUtc ?? row.CreatedAtUtc);
    }

    /// <exception cref="OrganizationAccessDeniedException">O utilizador não é dono nem administrador de nenhuma empresa.</exception>
    /// <exception cref="ArgumentException">Locais, nomes ou tipos de ficheiro inválidos.</exception>
    public async Task<AtGeneralSettingsView> SaveAsync(
        Guid sessionUserId, AtGeneralSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanEditAsync(sessionUserId, cancellationToken).ConfigureAwait(false))
        {
            throw new OrganizationAccessDeniedException(
                "Só o dono ou um administrador de uma empresa pode alterar a configuração geral da AT, que vale para todas as empresas.");
        }

        var row = await context.AtGeneralSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var isNew = row is null;
        row ??= AtGeneralSettings.Create();
        row.SetLocations(input.CertificateDirectory, input.CertificateFileName, input.PublicKeyDirectory, input.PublicKeyFileName);

        if (!string.IsNullOrEmpty(input.Password))
        {
            row.SetEncryptedPassword(protector.IsProtected(input.Password) ? input.Password : protector.Protect(input.Password));
        }
        else if (input.RemovePassword)
        {
            row.SetEncryptedPassword(null);
        }

        if (isNew)
        {
            context.AtGeneralSettings.Add(row);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invalidator?.Invalidate();
        return await GetAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A leitura síncrona da configuração guardada, para a camada que fala com a AT.</summary>
    public AtGeneralSettingsSnapshot? Read() =>
        context.AtGeneralSettings
            .AsNoTracking()
            .Select(item => new AtGeneralSettingsSnapshot(
                item.CertificateDirectory,
                item.CertificateFileName,
                item.PublicKeyDirectory,
                item.PublicKeyFileName,
                item.EncryptedCertificatePassword))
            .SingleOrDefault();

    private Task<bool> CanEditAsync(Guid sessionUserId, CancellationToken cancellationToken) =>
        context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == sessionUserId &&
                    membership.IsActive &&
                    (membership.Role == CompanyUserRole.Owner || membership.Role == CompanyUserRole.Administrator) &&
                    context.Companies.Any(company => company.Id == membership.CompanyId && company.IsActive),
                cancellationToken);
}
