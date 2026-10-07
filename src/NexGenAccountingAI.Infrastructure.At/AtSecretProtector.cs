using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// Cifra os segredos da AT que ficam guardados (a senha do certificado, a senha de cada
/// empresa na AT). Há dois formatos:
/// <list type="bullet">
/// <item><c>enc:v2:…</c> — <b>portátil</b>: AES-256-GCM com uma chave derivada (HKDF-SHA256)
/// de um segredo da aplicação e de um sal aleatório de cada valor. Qualquer posto com o
/// programa e a mesma base de dados o decifra, sem chaveiro nem instalação; é o que se
/// escreve. Protege a base de dados e as cópias de segurança contra leitura casual;
/// <b>não</b> protege de quem tenha a base de dados <em>e</em> o programa — o controlo real
/// é só o produtor poder ver ou alterar estes valores, e nenhuma resposta da API os devolver.</item>
/// <item><c>enc:v1:…</c> — o formato anterior (Data Protection do ASP.NET Core, chaveiro
/// protegido pelo DPAPI): só se decifra na máquina e conta que o cifrou. Continua a ler-se
/// para não perder valores já guardados; já não se escreve.</item>
/// </list>
/// </summary>
public sealed class AtSecretProtector : IAtSecretProtector
{
    private const string PrefixV1 = "enc:v1:";
    private const string PrefixV2 = "enc:v2:";
    // Não renomear com o produto: os segredos já cifrados ficariam ilegíveis.
    private const string Purpose = "AccountingAI.At.Certificate.v1";
    private const byte FormatVersion = 2;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // O segredo da aplicação é igual em todos os postos — é isso que torna a cifra portátil.
    // Não é um segredo de quem tem o código-fonte; ver o resumo desta classe.
    private static readonly byte[] ApplicationSecret =
        Encoding.UTF8.GetBytes("NexGen Accounting AI | segredos da AT | cifra portatil v2 | produtor RumaoAlgarismo");

    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("NexGenAccountingAI.At.Secret.v2");

    private readonly IDataProtector? legacyProtector;

    /// <param name="legacyProtector">Só para ler valores <c>enc:v1:</c>; <c>null</c> torna-os ilegíveis.</param>
    public AtSecretProtector(IDataProtector? legacyProtector = null)
    {
        this.legacyProtector = legacyProtector;
    }

    public static string DefaultKeyRingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AccountingAI", // nome anterior ao NexGen, mantido para não perder o chaveiro
        "dp-keys");

    public static AtSecretProtector Create(string? keyRingPath = null)
    {
        var directory = new DirectoryInfo(keyRingPath ?? DefaultKeyRingPath);
        directory.Create();
        var provider = DataProtectionProvider.Create(directory);
        return new AtSecretProtector(provider.CreateProtector(Purpose));
    }

    public bool IsProtected(string? value) =>
        value is not null &&
        (value.StartsWith(PrefixV2, StringComparison.Ordinal) || value.StartsWith(PrefixV1, StringComparison.Ordinal));

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(DeriveKey(salt), TagSize))
        {
            aes.Encrypt(nonce, data, cipher, tag);
        }

        var packed = new byte[1 + SaltSize + NonceSize + TagSize + cipher.Length];
        packed[0] = FormatVersion;
        salt.CopyTo(packed, 1);
        nonce.CopyTo(packed, 1 + SaltSize);
        tag.CopyTo(packed, 1 + SaltSize + NonceSize);
        cipher.CopyTo(packed, 1 + SaltSize + NonceSize + TagSize);
        CryptographicOperations.ZeroMemory(data);
        return PrefixV2 + Convert.ToBase64String(packed);
    }

    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value))
        {
            return value ?? string.Empty;
        }

        try
        {
            return value.StartsWith(PrefixV2, StringComparison.Ordinal)
                ? UnprotectPortable(value[PrefixV2.Length..])
                : legacyProtector?.Unprotect(value[PrefixV1.Length..]) ?? value;
        }
        catch (CryptographicException)
        {
            return value;
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static string UnprotectPortable(string encoded)
    {
        var packed = Convert.FromBase64String(encoded);
        if (packed.Length < 1 + SaltSize + NonceSize + TagSize || packed[0] != FormatVersion)
        {
            throw new CryptographicException("Formato de valor cifrado desconhecido.");
        }

        var salt = packed.AsSpan(1, SaltSize);
        var nonce = packed.AsSpan(1 + SaltSize, NonceSize);
        var tag = packed.AsSpan(1 + SaltSize + NonceSize, TagSize);
        var cipher = packed.AsSpan(1 + SaltSize + NonceSize + TagSize);
        var data = new byte[cipher.Length];
        using (var aes = new AesGcm(DeriveKey(salt), TagSize))
        {
            aes.Decrypt(nonce, cipher, tag, data);
        }

        var text = Encoding.UTF8.GetString(data);
        CryptographicOperations.ZeroMemory(data);
        return text;
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> salt)
    {
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ApplicationSecret, key, salt, KeyInfo);
        return key;
    }
}
