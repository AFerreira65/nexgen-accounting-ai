namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// A configuração geral da ligação à AT, comum a todas as empresas da instalação:
/// onde estão o certificado de transporte (PFX/P12) e a chave pública do sistema de
/// autenticação da AT (CER), e a senha do certificado, que se guarda cifrada. Só há
/// uma linha. Uma pasta ou um nome em branco significa «não indicado»: a aplicação
/// recorre então à secção <c>At</c> do ficheiro de configuração.
/// </summary>
public sealed class AtGeneralSettings
{
    public const int PathMaxLength = 260;

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

    public string? CertificateDirectory { get; private set; }

    public string? CertificateFileName { get; private set; }

    public string? PublicKeyDirectory { get; private set; }

    public string? PublicKeyFileName { get; private set; }

    /// <summary>A senha do certificado, no formato cifrado (<c>enc:v1:…</c>).</summary>
    public string? EncryptedCertificatePassword { get; private set; }

    public uint RowVersion { get; private set; }

    public static AtGeneralSettings Create() => new(Guid.NewGuid());

    /// <summary>
    /// Substitui os locais e os nomes. Os valores em branco ficam por indicar; a pasta e o
    /// nome têm de vir os dois ou nenhum, a pasta tem de ser absoluta e o nome não pode
    /// conter pastas.
    /// </summary>
    public void SetLocations(
        string? certificateDirectory, string? certificateFileName, string? publicKeyDirectory, string? publicKeyFileName)
    {
        var certificate = Normalize("do certificado", certificateDirectory, certificateFileName, CertificateExtensions);
        var publicKey = Normalize("da chave pública", publicKeyDirectory, publicKeyFileName, PublicKeyExtensions);
        CertificateDirectory = certificate.Directory;
        CertificateFileName = certificate.FileName;
        PublicKeyDirectory = publicKey.Directory;
        PublicKeyFileName = publicKey.FileName;
    }

    /// <summary>Guarda a senha já cifrada, ou retira-a quando <paramref name="encryptedPassword"/> é <c>null</c>.</summary>
    public void SetEncryptedPassword(string? encryptedPassword) =>
        EncryptedCertificatePassword = string.IsNullOrWhiteSpace(encryptedPassword) ? null : encryptedPassword;

    private static (string? Directory, string? FileName) Normalize(
        string what, string? directory, string? fileName, string[] extensions)
    {
        var folder = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim().TrimEnd('\\', '/');
        var name = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim();
        if (folder is null && name is null)
        {
            return (null, null);
        }

        if (folder is null || name is null)
        {
            throw new ArgumentException($"Indique a pasta e o nome do ficheiro {what}, ou deixe os dois em branco.");
        }

        if (folder.Length > PathMaxLength || name.Length > PathMaxLength)
        {
            throw new ArgumentException($"A pasta e o nome do ficheiro {what} não podem ter mais de {PathMaxLength} caracteres.");
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"A pasta do ficheiro {what} tem de ser um caminho completo (por exemplo C:\\AT\\certificados).");
        }

        if (name.IndexOfAny(['\\', '/', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"O nome do ficheiro {what} não pode conter pastas nem carateres inválidos.");
        }

        if (!extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"O ficheiro {what} tem de ser do tipo {string.Join(", ", extensions)}.");
        }

        return (folder, name);
    }
}
