namespace NexGenAccountingAI.Application.At;

/// <summary>
/// O que a configuração geral da AT (guardada na base de dados) indica. A senha vem
/// cifrada (<c>enc:v1:…</c>); só a camada que fala com a AT a decifra.
/// </summary>
public sealed record AtGeneralSettingsSnapshot(
    string? CertificateDirectory,
    string? CertificateFileName,
    string? PublicKeyDirectory,
    string? PublicKeyFileName,
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
/// <param name="Source"><c>geral</c> (configuração geral), <c>ficheiro</c> (secção At) ou <c>nenhuma</c>.</param>
public sealed record AtPublicKeyStatus(
    bool Configured,
    bool Valid,
    string? Path,
    string? Subject,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    string Source,
    string? Problem);
