namespace NexGenAccountingAI.Application.At;

/// <summary>
/// O estado do certificado de transporte da AT, sem segredos: nunca inclui a
/// senha nem os bytes da chave privada.
/// </summary>
/// <param name="Configured">`true` quando há um ficheiro de certificado configurado.</param>
/// <param name="Valid">`true` quando o certificado carrega e está dentro do período de validade.</param>
/// <param name="Subject">O sujeito (DN) do certificado.</param>
/// <param name="Issuer">A entidade emissora.</param>
/// <param name="SerialNumber">O número de série (hexadecimal).</param>
/// <param name="Thumbprint">A impressão digital SHA-1 (hexadecimal).</param>
/// <param name="NotBeforeUtc">Início da validade (UTC).</param>
/// <param name="NotAfterUtc">Fim da validade (UTC).</param>
/// <param name="HasPrivateKey">Se o ficheiro traz a chave privada.</param>
/// <param name="Environment">Ambiente configurado (<c>Testes</c> / <c>Producao</c>).</param>
/// <param name="Problem">Descrição em pt-PT do que falhou, ou <c>null</c>.</param>
/// <param name="Source">De onde vem a configuração: <c>geral</c> (guardada na aplicação), <c>ficheiro</c> (secção At) ou <c>nenhuma</c>.</param>
public sealed record AtCertificateStatus(
    bool Configured,
    bool Valid,
    string? Subject,
    string? Issuer,
    string? SerialNumber,
    string? Thumbprint,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    bool HasPrivateKey,
    string Environment,
    string? Problem,
    string Source = "nenhuma")
{
    /// <summary>Sem ficheiro configurado.</summary>
    public static AtCertificateStatus NotConfigured(string environment) =>
        new(false, false, null, null, null, null, null, null, false, environment,
            "Não há certificado de transporte da AT configurado: indique-o na configuração geral da AT.");

    /// <summary>Configurado, mas o carregamento falhou.</summary>
    public static AtCertificateStatus Failed(string environment, string problem) =>
        new(true, false, null, null, null, null, null, null, false, environment, problem);
}
