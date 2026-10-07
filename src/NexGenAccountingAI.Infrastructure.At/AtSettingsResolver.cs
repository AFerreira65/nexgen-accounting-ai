using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// O que a ligação à AT usa, já resolvido: o certificado e a chave pública (os bytes que o
/// produtor carregou, ou, na alternativa do servidor, o caminho do ficheiro), a senha do
/// certificado em claro (só em memória) e de onde veio cada coisa.
/// </summary>
/// <param name="CertificateSource"><c>geral</c>, <c>ficheiro</c> ou <c>nenhuma</c>.</param>
/// <param name="CertificateName">O nome do ficheiro carregado, ou o caminho no caso do ficheiro de configuração.</param>
public sealed record AtEffectiveSettings(
    byte[]? CertificateData,
    string? CertificatePath,
    string? CertificateName,
    string? CertificatePassword,
    string CertificateSource,
    byte[]? PublicKeyData,
    string? PublicKeyPath,
    string? PublicKeyName,
    string PublicKeySource)
{
    public bool HasCertificate => CertificateData is not null || !string.IsNullOrWhiteSpace(CertificatePath);

    public bool HasPublicKey => PublicKeyData is not null || !string.IsNullOrWhiteSpace(PublicKeyPath);
}

/// <summary>
/// Junta a configuração geral da AT, guardada na base de dados pelo produtor, com a secção
/// <c>At</c> do ficheiro de configuração do servidor, que fica como alternativa. O certificado
/// e a sua senha valem em conjunto: havendo certificado na configuração geral, usa-se a senha
/// dela (ou nenhuma), nunca a do ficheiro. A chave pública resolve-se à parte. A leitura da
/// base fica em memória por pouco tempo; <see cref="Invalidate"/> refaz-a à próxima utilização,
/// para uma alteração valer logo neste posto — noutros postos vale no máximo ao fim da
/// duração da memória (20 s).
/// </summary>
public sealed class AtSettingsResolver(
    IOptions<AtOptions> options,
    AtSecretProtector protector,
    TimeProvider time,
    IServiceScopeFactory? scopes = null) : IAtSettingsInvalidator
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan CacheFailureFor = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private AtGeneralSettingsSnapshot? stored;
    private DateTimeOffset validUntil = DateTimeOffset.MinValue;

    public void Invalidate()
    {
        lock (gate)
        {
            validUntil = DateTimeOffset.MinValue;
        }
    }

    public AtEffectiveSettings Resolve()
    {
        var snapshot = Stored();
        var fileOptions = options.Value;

        byte[]? certificateData = null;
        string? certificatePath = null;
        string? certificateName = null;
        string? password;
        string certificateSource;
        if (snapshot?.CertificateData is { Length: > 0 } uploaded)
        {
            certificateData = uploaded;
            certificateName = snapshot.CertificateFileName;
            password = string.IsNullOrEmpty(snapshot.EncryptedCertificatePassword) ? null : protector.Unprotect(snapshot.EncryptedCertificatePassword);
            certificateSource = "geral";
        }
        else
        {
            certificatePath = FileCertificatePath(fileOptions);
            certificateName = certificatePath;
            password = protector.Unprotect(fileOptions.Certificate.Password);
            certificateSource = certificatePath is null ? "nenhuma" : "ficheiro";
        }

        byte[]? publicKeyData = null;
        string? publicKeyPath = null;
        string? publicKeyName = null;
        string publicKeySource;
        if (snapshot?.PublicKeyData is { Length: > 0 } uploadedKey)
        {
            publicKeyData = uploadedKey;
            publicKeyName = snapshot.PublicKeyFileName;
            publicKeySource = "geral";
        }
        else
        {
            var fileKey = protector.Unprotect(fileOptions.PublicKey.Path);
            publicKeyPath = string.IsNullOrWhiteSpace(fileKey) ? null : fileKey;
            publicKeyName = publicKeyPath;
            publicKeySource = publicKeyPath is null ? "nenhuma" : "ficheiro";
        }

        return new AtEffectiveSettings(
            certificateData, certificatePath, certificateName, string.IsNullOrEmpty(password) ? null : password, certificateSource,
            publicKeyData, publicKeyPath, publicKeyName, publicKeySource);
    }

    private string? FileCertificatePath(AtOptions fileOptions)
    {
        var explicitPath = protector.Unprotect(fileOptions.Certificate.Path);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var directory = protector.Unprotect(fileOptions.Certificate.Directory);
        var fileName = protector.Unprotect(fileOptions.Certificate.FileName);
        return !string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName) ? Path.Combine(directory, fileName) : null;
    }

    private AtGeneralSettingsSnapshot? Stored()
    {
        if (scopes is null)
        {
            return null;
        }

        lock (gate)
        {
            if (time.GetUtcNow() < validUntil)
            {
                return stored;
            }

            try
            {
                using var scope = scopes.CreateScope();
                stored = scope.ServiceProvider.GetService<IAtGeneralSettingsReader>()?.Read();
                validUntil = time.GetUtcNow() + CacheFor;
            }
            catch (Exception)
            {
                // Base indisponível ou ainda sem a tabela: vale a secção At, e volta-se a tentar daqui a pouco.
                stored = null;
                validUntil = time.GetUtcNow() + CacheFailureFor;
            }

            return stored;
        }
    }
}
