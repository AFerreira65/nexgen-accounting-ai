using Microsoft.EntityFrameworkCore;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// A configuração geral da AT como o ecrã do produtor a vê. Nunca traz a senha, o valor
/// cifrado nem o conteúdo dos ficheiros: só os nomes e se há senha.
/// </summary>
public sealed record AtGeneralSettingsView(
    string? CertificateFileName,
    string? PublicKeyFileName,
    bool PasswordConfigured,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// A configuração geral da AT — o certificado de transporte, a chave pública da AT e a senha
/// do certificado — comum a toda a instalação e guardada na própria base de dados, para
/// qualquer posto a usar sem instalar nada. Só o utilizador produtor (ADR-0017) a vê e a
/// altera; a senha guarda-se cifrada com a cifra portátil. Lê-se também de forma síncrona
/// (<see cref="IAtGeneralSettingsReader"/>) para a camada que fala com a AT.
/// </summary>
public sealed class AtGeneralSettingsService(
    AccountingDbContext context,
    IAtSecretProtector protector,
    IAtFileValidator validator,
    IAtSettingsInvalidator? invalidator = null) : IAtGeneralSettingsReader
{
    public async Task<AtGeneralSettingsView> GetAsync(Guid sessionUserId, CancellationToken cancellationToken = default)
    {
        await EnsureProducerAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
        return await ViewAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Guarda o certificado e a senha, depois de confirmar que abrem em conjunto e trazem chave privada.</summary>
    /// <exception cref="OrganizationAccessDeniedException">O utilizador não é o produtor.</exception>
    /// <exception cref="ArgumentException">Ficheiro ou senha inválidos.</exception>
    public async Task<AtGeneralSettingsView> SetCertificateAsync(
        Guid sessionUserId, string? fileName, byte[] data, string? password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await EnsureProducerAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
        var plain = password ?? string.Empty;
        validator.ValidateCertificate(data, plain);

        var (row, isNew) = await RowAsync(cancellationToken).ConfigureAwait(false);
        row.SetCertificate(fileName, data, protector.Protect(plain));
        return await SaveAsync(row, isNew, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Troca a senha do certificado já guardado, depois de confirmar que ela o abre.</summary>
    public async Task<AtGeneralSettingsView> ChangePasswordAsync(
        Guid sessionUserId, string? password, CancellationToken cancellationToken = default)
    {
        await EnsureProducerAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
        var (row, isNew) = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (isNew || row.CertificateData is null)
        {
            throw new ArgumentException("Carregue primeiro o certificado.");
        }

        var plain = password ?? string.Empty;
        validator.ValidateCertificate(row.CertificateData, plain);
        row.SetEncryptedPassword(protector.Protect(plain));
        return await SaveAsync(row, isNew: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtGeneralSettingsView> RemoveCertificateAsync(Guid sessionUserId, CancellationToken cancellationToken = default)
    {
        await EnsureProducerAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
        var (row, isNew) = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (isNew)
        {
            return await ViewAsync(cancellationToken).ConfigureAwait(false);
        }

        row.ClearCertificate();
        return await SaveAsync(row, isNew: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtGeneralSettingsView> SetPublicKeyAsync(
        Guid sessionUserId, string? fileName, byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await EnsureProducerAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
        validator.ValidatePublicKey(data);

        var (row, isNew) = await RowAsync(cancellationToken).ConfigureAwait(false);
        row.SetPublicKey(fileName, data);
        return await SaveAsync(row, isNew, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtGeneralSettingsView> RemovePublicKeyAsync(Guid sessionUserId, CancellationToken cancellationToken = default)
    {
        await EnsureProducerAsync(sessionUserId, cancellationToken).ConfigureAwait(false);
        var (row, isNew) = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (isNew)
        {
            return await ViewAsync(cancellationToken).ConfigureAwait(false);
        }

        row.ClearPublicKey();
        return await SaveAsync(row, isNew: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A leitura síncrona da configuração guardada, para a camada que fala com a AT.</summary>
    public AtGeneralSettingsSnapshot? Read() =>
        context.AtGeneralSettings
            .AsNoTracking()
            .Select(item => new AtGeneralSettingsSnapshot(
                item.CertificateFileName,
                item.CertificateData,
                item.PublicKeyFileName,
                item.PublicKeyData,
                item.EncryptedCertificatePassword))
            .SingleOrDefault();

    private async Task<(AtGeneralSettings Row, bool IsNew)> RowAsync(CancellationToken cancellationToken)
    {
        var row = await context.AtGeneralSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? (AtGeneralSettings.Create(), true) : (row, false);
    }

    private async Task<AtGeneralSettingsView> SaveAsync(AtGeneralSettings row, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            context.AtGeneralSettings.Add(row);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invalidator?.Invalidate();
        return await ViewAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AtGeneralSettingsView> ViewAsync(CancellationToken cancellationToken)
    {
        var row = await context.AtGeneralSettings
            .AsNoTracking()
            .Select(item => new
            {
                item.CertificateFileName,
                item.PublicKeyFileName,
                item.EncryptedCertificatePassword,
                HasCertificate = item.CertificateData != null,
                HasPublicKey = item.PublicKeyData != null,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(item, "ModifiedAtUtc"),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? new AtGeneralSettingsView(null, null, false, null)
            : new AtGeneralSettingsView(
                row.HasCertificate ? row.CertificateFileName : null,
                row.HasPublicKey ? row.PublicKeyFileName : null,
                row.HasCertificate && !string.IsNullOrEmpty(row.EncryptedCertificatePassword),
                row.ModifiedAtUtc ?? row.CreatedAtUtc);
    }

    private async Task EnsureProducerAsync(Guid sessionUserId, CancellationToken cancellationToken)
    {
        var isProducer = await context.UserAccounts
            .AsNoTracking()
            .AnyAsync(user => user.Id == sessionUserId && user.IsActive && user.IsProducer, cancellationToken)
            .ConfigureAwait(false);
        if (!isProducer)
        {
            throw new OrganizationAccessDeniedException("A configuração geral da AT é só do produtor da aplicação.");
        }
    }
}
