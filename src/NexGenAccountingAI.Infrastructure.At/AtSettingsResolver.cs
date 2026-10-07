using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// O que a ligação à AT usa, já resolvido: o certificado, a senha (em claro, só em
/// memória), a chave pública, e de onde veio cada um.
/// </summary>
/// <param name="CertificateSource"><c>geral</c>, <c>ficheiro</c> ou <c>nenhuma</c>.</param>
public sealed record AtEffectiveSettings(
    string? CertificatePath,
    string? CertificatePassword,
    string CertificateSource,
    string? PublicKeyPath,
    string PublicKeySource);

/// <summary>
/// Junta a configuração geral da AT, guardada na base de dados (ecrã «Comunicação com a
/// AT»), com a secção <c>At</c> do ficheiro de configuração, que fica como alternativa.
/// O certificado e a sua senha valem em conjunto: havendo certificado na configuração
/// geral, usa-se a senha dela (ou nenhuma), nunca a do ficheiro. A chave pública resolve-se
/// à parte. A leitura da base fica em memória por pouco tempo; <see cref="Invalidate"/>
/// refaz-a à próxima utilização, para uma alteração valer logo.
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

        string? certificatePath;
        string? password;
        string certificateSource;
        if (Combine(snapshot?.CertificateDirectory, snapshot?.CertificateFileName) is { } generalCertificate)
        {
            certificatePath = generalCertificate;
            password = string.IsNullOrEmpty(snapshot?.EncryptedCertificatePassword) ? null : protector.Unprotect(snapshot.EncryptedCertificatePassword);
            certificateSource = "geral";
        }
        else
        {
            certificatePath = FileCertificatePath(fileOptions);
            password = protector.Unprotect(fileOptions.Certificate.Password);
            certificateSource = certificatePath is null ? "nenhuma" : "ficheiro";
        }

        string? publicKeyPath;
        string publicKeySource;
        if (Combine(snapshot?.PublicKeyDirectory, snapshot?.PublicKeyFileName) is { } generalKey)
        {
            publicKeyPath = generalKey;
            publicKeySource = "geral";
        }
        else
        {
            var fileKey = protector.Unprotect(fileOptions.PublicKey.Path);
            publicKeyPath = string.IsNullOrWhiteSpace(fileKey) ? null : fileKey;
            publicKeySource = publicKeyPath is null ? "nenhuma" : "ficheiro";
        }

        return new AtEffectiveSettings(certificatePath, string.IsNullOrEmpty(password) ? null : password, certificateSource, publicKeyPath, publicKeySource);
    }

    private string? FileCertificatePath(AtOptions fileOptions)
    {
        var explicitPath = protector.Unprotect(fileOptions.Certificate.Path);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return Combine(protector.Unprotect(fileOptions.Certificate.Directory), protector.Unprotect(fileOptions.Certificate.FileName));
    }

    private static string? Combine(string? directory, string? fileName) =>
        !string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName) ? Path.Combine(directory, fileName) : null;

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
