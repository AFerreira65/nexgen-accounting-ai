namespace NexGenAccountingAI.Application.At;

/// <summary>
/// O que a configuração geral da AT (guardada na base de dados) indica: os ficheiros e a
/// senha do certificado, esta ainda cifrada (<c>enc:v2:…</c>). Só a camada que fala com a
/// AT a decifra.
/// </summary>
public sealed record AtGeneralSettingsSnapshot(
    string? CertificateFileName,
    byte[]? CertificateData,
    string? PublicKeyFileName,
    byte[]? PublicKeyData,
    string? EncryptedCertificatePassword);

/// <summary>Lê a configuração geral da AT guardada, de forma síncrona (a ligação à AT é síncrona).</summary>
public interface IAtGeneralSettingsReader
{
    /// <returns>A configuração guardada, ou <c>null</c> quando ainda não há.</returns>
    AtGeneralSettingsSnapshot? Read();
}

/// <summary>Avisa a camada da AT de que a configuração geral mudou, para deixar de usar a que tinha em memória.</summary>
public interface IAtSettingsInvalidator
{
    void Invalidate();
}

/// <summary>
/// O estado da chave pública do sistema de autenticação da AT, com que se cifra a senha
/// enviada aos webservices.
/// </summary>
/// <param name="FileName">O nome do ficheiro tal como foi carregado, ou o caminho no caso do ficheiro de configuração.</param>
/// <param name="Source"><c>geral</c> (configuração geral), <c>ficheiro</c> (secção At do servidor) ou <c>nenhuma</c>.</param>
public sealed record AtPublicKeyStatus(
    bool Configured,
    bool Valid,
    string? FileName,
    string? Subject,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    string Source,
    string? Problem);

/// <summary>
/// Valida os ficheiros que o produtor carrega, antes de serem guardados: o certificado tem de
/// abrir com a senha e trazer a chave privada; a chave pública tem de ser um certificado com
/// chave RSA.
/// </summary>
public interface IAtFileValidator
{
    /// <exception cref="ArgumentException">O ficheiro não abre com a senha, não traz chave privada ou está fora de prazo.</exception>
    AtCertificateInfo ValidateCertificate(byte[] pfx, string password);

    /// <exception cref="ArgumentException">O ficheiro não é um certificado com chave pública RSA.</exception>
    AtCertificateInfo ValidatePublicKey(byte[] certificate);
}

/// <summary>O que se mostra de um certificado carregado.</summary>
public sealed record AtCertificateInfo(string Subject, DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc);
