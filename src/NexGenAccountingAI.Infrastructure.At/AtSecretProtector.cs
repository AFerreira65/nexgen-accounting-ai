using System.Security.Cryptography;
using NexGenAccountingAI.Application.At;
using Microsoft.AspNetCore.DataProtection;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// Cifra e decifra os valores da configuração da AT (a senha do certificado e,
/// se se quiser, o local do ficheiro). Um valor cifrado tem o prefixo
/// <c>enc:v1:</c>; um valor sem prefixo é devolvido tal como está — a
/// configuração pode ir sendo migrada. A cifra é a Data Protection do ASP.NET
/// Core, com o chaveiro protegido pelo DPAPI do Windows em disco.
/// </summary>
public sealed class AtSecretProtector : IAtSecretProtector
{
    private const string Prefix = "enc:v1:";
    // Não renomear com o produto: os segredos já cifrados ficariam ilegíveis.
    private const string Purpose = "AccountingAI.At.Certificate.v1";

    private readonly IDataProtector protector;

    public AtSecretProtector(IDataProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        this.protector = protector;
    }

    /// <summary>O chaveiro por omissão: <c>%LOCALAPPDATA%\AccountingAI\dp-keys</c>.</summary>
    public static string DefaultKeyRingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AccountingAI", // nome anterior ao NexGen, mantido para não perder o chaveiro
        "dp-keys");

    /// <summary>Cria um protetor autónomo (o mesmo chaveiro serve a aplicação e a linha de comandos).</summary>
    public static AtSecretProtector Create(string? keyRingPath = null)
    {
        var directory = new DirectoryInfo(keyRingPath ?? DefaultKeyRingPath);
        directory.Create();
        var provider = DataProtectionProvider.Create(directory);
        return new AtSecretProtector(provider.CreateProtector(Purpose));
    }

    public bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Devolve o valor cifrado, com o prefixo <c>enc:v1:</c>.</summary>
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Prefix + protector.Protect(plaintext);
    }

    /// <summary>
    /// Decifra um valor <c>enc:v1:…</c>; um valor sem prefixo (ou que não decifra
    /// com este chaveiro) é devolvido inalterado.
    /// </summary>
    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value))
        {
            return value ?? string.Empty;
        }

        try
        {
            return protector.Unprotect(value[Prefix.Length..]);
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
}
