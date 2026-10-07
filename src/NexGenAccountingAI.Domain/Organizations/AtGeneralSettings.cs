namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// A configuração geral da ligação à AT, comum a todas as empresas e a todos os postos
/// que usam a mesma base de dados: o certificado de transporte (PFX/P12), a chave pública
/// do sistema de autenticação da AT (CER) e a senha do certificado. Os ficheiros ficam
/// <b>na própria base de dados</b>, para qualquer posto os poder usar sem os ter
/// instalados; a senha fica cifrada (<c>enc:v2:…</c>, cifra portátil). Só há uma linha,
/// e só o produtor a altera.
/// </summary>
public sealed class AtGeneralSettings
{
    public const int FileNameMaxLength = 260;

    /// <summary>Um certificado com a cadeia cabe com folga em 256 KB.</summary>
    public const int FileMaxBytes = 256 * 1024;

    public const int SingletonKey = 1;

    private static readonly string[] CertificateExtensions = [".pfx", ".p12"];

    private static readonly string[] PublicKeyExtensions = [".cer", ".crt", ".pem"];

    private AtGeneralSettings()
    {
    }

    private AtGeneralSettings(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    /// <summary>Constante, para a base de dados garantir uma só linha.</summary>
    public int Key { get; private set; } = SingletonKey;

    /// <summary>O nome do ficheiro do certificado tal como foi carregado (só para mostrar).</summary>
    public string? CertificateFileName { get; private set; }

    /// <summary>O ficheiro PFX/P12, tal como a AT o enviou: continua protegido pela sua senha.</summary>
    public byte[]? CertificateData { get; private set; }

    public string? PublicKeyFileName { get; private set; }

    /// <summary>O ficheiro CER/CRT/PEM da chave pública da AT.</summary>
    public byte[]? PublicKeyData { get; private set; }

    /// <summary>A senha do certificado, cifrada (<c>enc:v2:…</c>).</summary>
    public string? EncryptedCertificatePassword { get; private set; }

    public uint RowVersion { get; private set; }

    public static AtGeneralSettings Create() => new(Guid.NewGuid());

    /// <summary>Guarda o certificado e a sua senha (já cifrada) em conjunto: um não faz sentido sem o outro.</summary>
    public void SetCertificate(string? fileName, byte[] data, string encryptedPassword)
    {
        var name = NormalizeFile("do certificado", fileName, data, CertificateExtensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);
        CertificateFileName = name;
        CertificateData = data;
        EncryptedCertificatePassword = encryptedPassword;
    }

    /// <summary>Troca só a senha do certificado já guardado.</summary>
    public void SetEncryptedPassword(string encryptedPassword)
    {
        if (CertificateData is null)
        {
            throw new InvalidOperationException("Carregue primeiro o certificado.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);
        EncryptedCertificatePassword = encryptedPassword;
    }

    public void ClearCertificate()
    {
        CertificateFileName = null;
        CertificateData = null;
        EncryptedCertificatePassword = null;
    }

    public void SetPublicKey(string? fileName, byte[] data)
    {
        PublicKeyFileName = NormalizeFile("da chave pública", fileName, data, PublicKeyExtensions);
        PublicKeyData = data;
    }

    public void ClearPublicKey()
    {
        PublicKeyFileName = null;
        PublicKeyData = null;
    }

    private static string NormalizeFile(string what, string? fileName, byte[] data, string[] extensions)
    {
        ArgumentNullException.ThrowIfNull(data);
        // Só o nome: o que um browser ou um atalho possa trazer de pastas é descartado.
        var name = Path.GetFileName((fileName ?? string.Empty).Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"Indique o nome do ficheiro {what}.");
        }

        if (name.Length > FileNameMaxLength || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"O nome do ficheiro {what} é inválido.");
        }

        if (!extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"O ficheiro {what} tem de ser do tipo {string.Join(", ", extensions)}.");
        }

        if (data.Length == 0)
        {
            throw new ArgumentException($"O ficheiro {what} está vazio.");
        }

        if (data.Length > FileMaxBytes)
        {
            throw new ArgumentException($"O ficheiro {what} tem mais de {FileMaxBytes / 1024} KB.");
        }

        return name;
    }
}
