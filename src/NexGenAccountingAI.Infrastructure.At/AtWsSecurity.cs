using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// O cabeçalho de autenticação dos webservices da AT (WS-Security, versão «2», com
/// vários contribuintes), tal como o descrevem os manuais de integração (DP de IVA,
/// Modelo 22, IES, obrigações acessórias — a secção «SOAP:Header» é igual em todos):
/// <list type="bullet">
/// <item><c>Nonce</c>: uma chave AES de 128 bits, nova em cada autenticação, cifrada
/// com a chave pública do sistema de autenticação da AT (RSA) e em Base64.</item>
/// <item><c>Password</c>: a senha cifrada com essa chave (AES, ECB, PKCS5) e em Base64.</item>
/// <item>atributo <c>Digest</c>: SHA-1 de chave + data + senha, cifrado da mesma forma.</item>
/// <item><c>Created</c>: a data e hora UTC, em claro.</item>
/// </list>
/// Um contribuinte sem senha (o contabilista tem plenos poderes) leva só o utilizador.
/// </summary>
public static class AtWsSecurity
{
    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    public static readonly XNamespace Wss = "http://schemas.xmlsoap.org/ws/2002/12/secext";
    public static readonly XNamespace At = "http://at.pt/wsp/auth";

    public const string TaxpayerActor = "http://at.pt/actor/SPA";
    public const string CertifiedAccountantActor = "http://at.pt/actor/TOC";

    /// <summary>Um elemento <c>wss:Security</c> por credencial, para pôr no <c>S:Header</c>.</summary>
    public static IReadOnlyList<XElement> BuildHeaders(IReadOnlyList<AtCredential> credentials, RSA authenticationPublicKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(authenticationPublicKey);
        if (credentials.Count == 0)
        {
            throw new ArgumentException("É precisa pelo menos uma credencial.", nameof(credentials));
        }

        if (credentials.Select(credential => credential.Actor).Distinct().Count() != credentials.Count)
        {
            // A AT responde «51 - Actor não é único no Header».
            throw new ArgumentException("Só pode haver uma credencial por qualidade (sujeito passivo, contabilista).", nameof(credentials));
        }

        return credentials.Select(credential => Build(credential, authenticationPublicKey, now)).ToList();
    }

    private static XElement Build(AtCredential credential, RSA authenticationPublicKey, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential.Username);

        var token = new XElement(Wss + "UsernameToken", new XElement(Wss + "Username", credential.Username.Trim()));
        if (!string.IsNullOrEmpty(credential.Password))
        {
            // Milissegundos e «Z», como nos exemplos dos manuais.
            var created = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var key = RandomNumberGenerator.GetBytes(16);
            try
            {
                var password = Encoding.UTF8.GetBytes(credential.Password);
                var digest = SHA1.HashData([.. key, .. Encoding.UTF8.GetBytes(created), .. password]);
                token.Add(
                    new XElement(Wss + "Password",
                        new XAttribute("Digest", Convert.ToBase64String(Encrypt(key, digest))),
                        Convert.ToBase64String(Encrypt(key, password))),
                    new XElement(Wss + "Nonce", Convert.ToBase64String(authenticationPublicKey.Encrypt(key, RSAEncryptionPadding.Pkcs1))),
                    new XElement(Wss + "Created", created));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        return new XElement(Wss + "Security",
            new XAttribute(XNamespace.Xmlns + "wss", Wss.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "at", At.NamespaceName),
            new XAttribute(Soap + "actor", credential.Actor == AtActor.CertifiedAccountant ? CertifiedAccountantActor : TaxpayerActor),
            new XAttribute(At + "Version", "2"),
            token);
    }

    /// <summary>AES com a chave do pedido, modo ECB e enchimento PKCS5 (o PKCS7 do .NET, igual para blocos de 16 bytes).</summary>
    private static byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptEcb(data, PaddingMode.PKCS7);
    }
}
