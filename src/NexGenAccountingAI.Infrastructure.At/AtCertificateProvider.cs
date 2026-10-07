using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// Dá o certificado de transporte e a chave pública da AT que estão em uso — os que o
/// produtor carregou na configuração geral (na base de dados, iguais em qualquer posto) ou, em
/// alternativa, os do ficheiro de configuração do servidor — e descreve o estado de ambos sem
/// nunca incluir a senha nem a chave privada (ADR-0008, ADR-0017).
/// </summary>
public sealed class AtCertificateProvider(IOptions<AtOptions> options, AtSettingsResolver settings) : IAtCertificateProvider, IAtFileValidator
{
    private readonly AtOptions options = options.Value;

    public AtCertificateStatus Describe()
    {
        var environment = NormalizeEnvironment(options.Environment);
        var effective = settings.Resolve();

        if (!effective.HasCertificate)
        {
            return AtCertificateStatus.NotConfigured(environment);
        }

        if (effective.CertificateData is null && !File.Exists(effective.CertificatePath))
        {
            return AtCertificateStatus.Failed(environment, $"O ficheiro do certificado não existe em «{effective.CertificatePath}».") with { Source = effective.CertificateSource };
        }

        try
        {
            using var certificate = LoadCertificate(effective);
            var now = DateTimeOffset.UtcNow;
            var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            var valid = now >= notBefore && now <= notAfter;

            return new AtCertificateStatus(
                Configured: true,
                Valid: valid,
                Subject: certificate.Subject,
                Issuer: certificate.Issuer,
                SerialNumber: certificate.SerialNumber,
                Thumbprint: certificate.Thumbprint,
                NotBeforeUtc: notBefore,
                NotAfterUtc: notAfter,
                HasPrivateKey: certificate.HasPrivateKey,
                Environment: environment,
                Problem: valid ? null : "O certificado está fora do período de validade.",
                Source: effective.CertificateSource);
        }
        catch (CryptographicException)
        {
            return AtCertificateStatus.Failed(
                environment,
                "Não foi possível abrir o certificado. Confirme a senha e o formato do ficheiro (PFX/P12).") with { Source = effective.CertificateSource };
        }
    }

    public AtPublicKeyStatus DescribePublicKey()
    {
        var effective = settings.Resolve();
        var source = effective.PublicKeySource;
        if (!effective.HasPublicKey)
        {
            return new AtPublicKeyStatus(false, false, null, null, null, null, source, "Não há chave pública da AT configurada.");
        }

        if (effective.PublicKeyData is null && !File.Exists(effective.PublicKeyPath))
        {
            return new AtPublicKeyStatus(true, false, effective.PublicKeyName, null, null, null, source, $"O ficheiro da chave pública não existe em «{effective.PublicKeyPath}».");
        }

        try
        {
            using var key = LoadPublicKeyCertificate(effective);
            using var rsa = key.GetRSAPublicKey();
            var now = DateTimeOffset.UtcNow;
            var notBefore = new DateTimeOffset(key.NotBefore.ToUniversalTime());
            var notAfter = new DateTimeOffset(key.NotAfter.ToUniversalTime());
            var problem = rsa is null ? "O ficheiro não tem uma chave pública RSA."
                : now < notBefore || now > notAfter ? "A chave pública está fora do período de validade."
                : null;
            return new AtPublicKeyStatus(true, problem is null, effective.PublicKeyName, key.Subject, notBefore, notAfter, source, problem);
        }
        catch (CryptographicException)
        {
            return new AtPublicKeyStatus(true, false, effective.PublicKeyName, null, null, null, source, "Não foi possível ler a chave pública. Confirme o formato do ficheiro (CER).");
        }
    }

    /// <summary>
    /// O certificado de transporte para uma ligação TLS: não se instala no repositório de certificados.
    /// Para a autenticação do cliente, o Windows não aceita uma chave só em memória (modo efémero), por
    /// isso a chave fica temporariamente no armazém de chaves do utilizador e sai dele quando o
    /// certificado é libertado — quem chama tem de o libertar (<c>Dispose</c>).
    /// </summary>
    /// <param name="forTls"><c>false</c> só para verificar que abre (não toca no armazém de chaves).</param>
    public bool TryGetCertificate([NotNullWhen(true)] out X509Certificate2? certificate, bool forTls = true)
    {
        certificate = null;
        var effective = settings.Resolve();
        if (!effective.HasCertificate || (effective.CertificateData is null && !File.Exists(effective.CertificatePath)))
        {
            return false;
        }

        try
        {
            certificate = LoadCertificate(effective, forTls ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>A chave pública da AT com que se cifra a senha enviada, ou <c>null</c> se não existe ou não abre.</summary>
    internal RSA? LoadPublicKey()
    {
        var effective = settings.Resolve();
        if (!effective.HasPublicKey || (effective.PublicKeyData is null && !File.Exists(effective.PublicKeyPath)))
        {
            return null;
        }

        try
        {
            using var certificate = LoadPublicKeyCertificate(effective);
            return certificate.GetRSAPublicKey();
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public AtCertificateInfo ValidateCertificate(byte[] pfx, string password)
    {
        ArgumentNullException.ThrowIfNull(pfx);
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(pfx, string.IsNullOrEmpty(password) ? null : password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            throw new ArgumentException("Não foi possível abrir o certificado com essa senha. Confirme a senha e o ficheiro (PFX/P12).");
        }

        using (certificate)
        {
            if (!certificate.HasPrivateKey)
            {
                throw new ArgumentException("O ficheiro não traz a chave privada: é preciso o PFX completo.");
            }

            var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            if (DateTimeOffset.UtcNow > notAfter)
            {
                throw new ArgumentException($"O certificado expirou em {notAfter:dd/MM/yyyy}.");
            }

            return new AtCertificateInfo(certificate.Subject, notBefore, notAfter);
        }
    }

    public AtCertificateInfo ValidatePublicKey(byte[] certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        X509Certificate2 key;
        try
        {
            key = X509CertificateLoader.LoadCertificate(certificate);
        }
        catch (CryptographicException)
        {
            throw new ArgumentException("Não foi possível ler a chave pública. Confirme o formato do ficheiro (CER).");
        }

        using (key)
        {
            using var rsa = key.GetRSAPublicKey();
            if (rsa is null)
            {
                throw new ArgumentException("O ficheiro não tem uma chave pública RSA.");
            }

            var notAfter = new DateTimeOffset(key.NotAfter.ToUniversalTime());
            if (DateTimeOffset.UtcNow > notAfter)
            {
                throw new ArgumentException($"A chave pública expirou em {notAfter:dd/MM/yyyy}.");
            }

            return new AtCertificateInfo(key.Subject, new DateTimeOffset(key.NotBefore.ToUniversalTime()), notAfter);
        }
    }

    private static X509Certificate2 LoadCertificate(AtEffectiveSettings effective, X509KeyStorageFlags flags = X509KeyStorageFlags.EphemeralKeySet) =>
        effective.CertificateData is { } data
            ? X509CertificateLoader.LoadPkcs12(data, effective.CertificatePassword, flags)
            : X509CertificateLoader.LoadPkcs12FromFile(effective.CertificatePath!, effective.CertificatePassword, flags);

    private static X509Certificate2 LoadPublicKeyCertificate(AtEffectiveSettings effective) =>
        effective.PublicKeyData is { } data
            ? X509CertificateLoader.LoadCertificate(data)
            : X509CertificateLoader.LoadCertificateFromFile(effective.PublicKeyPath!);

    private static string NormalizeEnvironment(string? environment) =>
        string.Equals(environment?.Trim(), "Producao", StringComparison.OrdinalIgnoreCase) ? "Producao" : "Testes";
}
