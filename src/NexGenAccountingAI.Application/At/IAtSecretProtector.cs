namespace NexGenAccountingAI.Application.At;

/// <summary>
/// Cifra e decifra segredos guardados na configuração ou na base de dados (a
/// senha do certificado, as credenciais da AT por empresa). Um valor cifrado
/// tem o prefixo <c>enc:v1:</c>; um valor sem prefixo é devolvido tal como está.
/// A implementação (<c>AtSecretProtector</c>) vive em
/// <c>NexGenAccountingAI.Infrastructure.At</c>.
/// </summary>
public interface IAtSecretProtector
{
    bool IsProtected(string? value);

    /// <summary>Devolve o valor cifrado, com o prefixo <c>enc:v1:</c>.</summary>
    string Protect(string plaintext);

    /// <summary>Decifra um valor <c>enc:v1:…</c>; um valor sem prefixo é devolvido inalterado.</summary>
    string Unprotect(string? value);
}
