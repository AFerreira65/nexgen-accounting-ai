using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AccountingAI.Application.At;
using Microsoft.Extensions.Options;

namespace AccountingAI.Infrastructure.At;

/// <summary>
/// Carrega o certificado de transporte da AT do local configurado (ADR-0008) e
/// descreve-o sem expor a senha nem a chave. É a única classe que toca no
/// ficheiro e na senha.
/// </summary>
public sealed class AtCertificateProvider(IOptions<AtOptions> options, AtSecretProtector protector) : IAtCertificateProvider
{
    private readonly AtOptions options = options.Value;

    public AtCertificateStatus Describe()
    {
        var environment = NormalizeEnvironment(options.Environment);
        var path = ResolvePath();

        if (string.IsNullOrWhiteSpace(path))
        {
            return AtCertificateStatus.NotConfigured(environment);
        }

        if (!File.Exists(path))
        {
            return AtCertificateStatus.Failed(environment, $"O ficheiro do certificado não existe em «{path}».");
        }

        try
        {
            using var certificate = Load(path, protector.Unprotect(options.Certificate.Password));
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
                Problem: valid ? null : "O certificado está fora do período de validade.");
        }
        catch (CryptographicException)
        {
            return AtCertificateStatus.Failed(
                environment,
                "Não foi possível abrir o certificado. Confirme a senha e o formato do ficheiro (PFX/P12).");
        }
    }

    /// <summary>
    /// Devolve o certificado carregado, para o cliente SOAP. O chamador é dono do
    /// <see cref="X509Certificate2"/> e deve libertá-lo.
    /// </summary>
    internal bool TryGetCertificate([NotNullWhen(true)] out X509Certificate2? certificate)
    {
        certificate = null;
        var path = ResolvePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            certificate = Load(path, protector.Unprotect(options.Certificate.Password));
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>O caminho do ficheiro, decifrando os valores <c>enc:v1:…</c> se os houver.</summary>
    private string? ResolvePath()
    {
        var explicitPath = protector.Unprotect(options.Certificate.Path);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var directory = protector.Unprotect(options.Certificate.Directory);
        var fileName = protector.Unprotect(options.Certificate.FileName);
        return !string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName)
            ? Path.Combine(directory, fileName)
            : null;
    }

    private static X509Certificate2 Load(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);

    private static string NormalizeEnvironment(string? environment) =>
        string.Equals(environment?.Trim(), "Producao", StringComparison.OrdinalIgnoreCase) ? "Producao" : "Testes";
}
