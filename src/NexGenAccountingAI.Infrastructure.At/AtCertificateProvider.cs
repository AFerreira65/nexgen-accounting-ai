using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NexGenAccountingAI.Application.At;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// Carrega o certificado de transporte da AT do local configurado (ADR-0008) e
/// descreve-o sem expor a senha nem a chave. É a única classe que toca no
/// ficheiro e na senha.
/// </summary>
public sealed class AtCertificateProvider(IOptions<AtOptions> options, AtSettingsResolver settings) : IAtCertificateProvider
{
    private readonly AtOptions options = options.Value;

    public AtCertificateStatus Describe()
    {
        var environment = NormalizeEnvironment(options.Environment);
        var effective = settings.Resolve();
        var path = effective.CertificatePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return AtCertificateStatus.NotConfigured(environment);
        }

        if (!File.Exists(path))
        {
            return AtCertificateStatus.Failed(environment, $"O ficheiro do certificado não existe em «{path}».") with { Source = effective.CertificateSource };
        }

        try
        {
            using var certificate = Load(path, effective.CertificatePassword);
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

    /// <summary>
    /// Devolve o certificado carregado, para o cliente SOAP. O chamador é dono do
    /// <see cref="X509Certificate2"/> e deve libertá-lo.
    /// </summary>
    internal bool TryGetCertificate([NotNullWhen(true)] out X509Certificate2? certificate)
    {
        certificate = null;
        var effective = settings.Resolve();
        var path = effective.CertificatePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            certificate = Load(path, effective.CertificatePassword);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>O caminho do ficheiro, decifrando os valores <c>enc:v1:…</c> se os houver.</summary>
    public AtPublicKeyStatus DescribePublicKey()
    {
        var effective = settings.Resolve();
        var path = effective.PublicKeyPath;
        var source = effective.PublicKeySource;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new AtPublicKeyStatus(false, false, null, null, null, null, source, "Não há chave pública da AT configurada: indique-a na configuração geral da AT.");
        }

        if (!File.Exists(path))
        {
            return new AtPublicKeyStatus(true, false, path, null, null, null, source, $"O ficheiro da chave pública não existe em «{path}».");
        }

        try
        {
            using var key = X509CertificateLoader.LoadCertificateFromFile(path);
            using var rsa = key.GetRSAPublicKey();
            var now = DateTimeOffset.UtcNow;
            var notBefore = new DateTimeOffset(key.NotBefore.ToUniversalTime());
            var notAfter = new DateTimeOffset(key.NotAfter.ToUniversalTime());
            var problem = rsa is null ? "O ficheiro não tem uma chave pública RSA."
                : now < notBefore || now > notAfter ? "A chave pública está fora do período de validade."
                : null;
            return new AtPublicKeyStatus(true, problem is null, path, key.Subject, notBefore, notAfter, source, problem);
        }
        catch (CryptographicException)
        {
            return new AtPublicKeyStatus(true, false, path, null, null, null, source, "Não foi possível ler a chave pública. Confirme o formato do ficheiro (CER).");
        }
    }

    private static X509Certificate2 Load(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);

    private static string NormalizeEnvironment(string? environment) =>
        string.Equals(environment?.Trim(), "Producao", StringComparison.OrdinalIgnoreCase) ? "Producao" : "Testes";
}
