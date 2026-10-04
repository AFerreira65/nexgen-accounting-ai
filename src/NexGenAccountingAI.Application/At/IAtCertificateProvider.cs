namespace NexGenAccountingAI.Application.At;

/// <summary>
/// Descreve o certificado de transporte da AT configurado. A implementação real
/// (<c>AtCertificateProvider</c>) vive em <c>NexGenAccountingAI.Infrastructure.At</c> e
/// é a única que carrega o ficheiro e a senha (ADR-0008). O futuro cliente SOAP
/// obtém o <c>X509Certificate2</c> a partir dessa implementação.
/// </summary>
public interface IAtCertificateProvider
{
    AtCertificateStatus Describe();
}
