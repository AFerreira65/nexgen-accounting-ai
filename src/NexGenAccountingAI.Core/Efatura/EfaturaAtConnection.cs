using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NexGenAccountingAI.Core.Efatura;

public sealed class EfaturaAtConnection : IDisposable
{
    private EfaturaAtConnection(
        string username,
        string portalPassword,
        string customerTaxId,
        X509Certificate2 clientCertificate,
        RSA authenticationPublicKey,
        EfaturaAtEnvironment environment)
    {
        Username = username;
        PortalPassword = portalPassword;
        CustomerTaxId = customerTaxId;
        ClientCertificate = clientCertificate;
        AuthenticationPublicKey = authenticationPublicKey;
        Environment = environment;
    }

    public string Username { get; }

    public string PortalPassword { get; }

    public string CustomerTaxId { get; }

    public X509Certificate2 ClientCertificate { get; }

    public RSA AuthenticationPublicKey { get; }

    public EfaturaAtEnvironment Environment { get; }

    public Uri Endpoint => Environment == EfaturaAtEnvironment.Production
        ? new Uri("https://servicos.portaldasfinancas.gov.pt:425/fatshare/ws/fatshareFaturas")
        : new Uri("https://servicos.portaldasfinancas.gov.pt:725/fatshare/ws/fatshareFaturas");

    public static EfaturaAtConnection Load(
        string username,
        string portalPassword,
        string customerTaxId,
        string pfxPath,
        string pfxPassword,
        string authenticationCertificatePath,
        EfaturaAtEnvironment environment)
    {
        Validate(username, portalPassword, customerTaxId, pfxPath, authenticationCertificatePath);

        X509Certificate2 clientCertificate;
        try
        {
            clientCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                pfxPath,
                pfxPassword,
                X509KeyStorageFlags.UserKeySet);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException(
                "Não foi possível abrir o certificado PFX. Confirme a senha do certificado.",
                exception);
        }
        if (!clientCertificate.HasPrivateKey)
        {
            clientCertificate.Dispose();
            throw new InvalidOperationException("O certificado PFX não contém uma chave privada.");
        }

        using var authenticationCertificate = X509CertificateLoader.LoadCertificateFromFile(authenticationCertificatePath);
        var authenticationPublicKey = authenticationCertificate.GetRSAPublicKey()
            ?? throw new InvalidOperationException("O certificado de autenticação da AT não contém uma chave pública RSA.");

        return new EfaturaAtConnection(
            username.Trim(),
            portalPassword,
            customerTaxId.Trim(),
            clientCertificate,
            authenticationPublicKey,
            environment);
    }

    public void Dispose()
    {
        AuthenticationPublicKey.Dispose();
        ClientCertificate.Dispose();
    }

    private static void Validate(
        string username,
        string password,
        string customerTaxId,
        string pfxPath,
        string authenticationCertificatePath)
    {
        var normalizedUsername = username.Trim();
        var loginTaxId = normalizedUsername.Split('/', 2)[0];
        if ((normalizedUsername.Length != 9 && !normalizedUsername.Contains('/', StringComparison.Ordinal)) ||
            loginTaxId.Length != 9 || !loginTaxId.All(char.IsDigit))
        {
            throw new ArgumentException(
                "Indique o NIF ou o subutilizador no formato NIF/Utilizador.",
                nameof(username));
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Indique a senha do subutilizador.", nameof(password));
        }

        if (customerTaxId.Length != 9 || !customerTaxId.All(char.IsDigit))
        {
            throw new ArgumentException("O NIF do adquirente deve ter 9 algarismos.", nameof(customerTaxId));
        }

        if (!loginTaxId.Equals(customerTaxId, StringComparison.Ordinal))
        {
            throw new ArgumentException("O NIF do adquirente deve coincidir com o NIF do subutilizador.", nameof(customerTaxId));
        }

        if (!File.Exists(pfxPath))
        {
            throw new FileNotFoundException("O certificado PFX não foi encontrado.", pfxPath);
        }

        if (!File.Exists(authenticationCertificatePath))
        {
            throw new FileNotFoundException("A chave pública/certificado de autenticação da AT não foi encontrada.", authenticationCertificatePath);
        }
    }
}
