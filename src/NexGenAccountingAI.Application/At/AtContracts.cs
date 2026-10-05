namespace NexGenAccountingAI.Application.At;

/// <summary>Em que qualidade um utilizador se autentica num webservice da AT.</summary>
public enum AtActor
{
    /// <summary>O sujeito passivo (<c>http://at.pt/actor/SPA</c>).</summary>
    Taxpayer,

    /// <summary>O contabilista certificado (<c>http://at.pt/actor/TOC</c>).</summary>
    CertifiedAccountant,
}

/// <summary>
/// Uma credencial do Portal das Finanças para um pedido: o utilizador (<c>NIF</c> ou
/// <c>NIF/subutilizador</c>) e a senha. Sem senha, só identifica o contribuinte — é o
/// caso do sujeito passivo quando o contabilista tem plenos poderes declarativos.
/// </summary>
public sealed record AtCredential(string Username, string? Password, AtActor Actor);

/// <summary>Um erro ou alerta devolvido pela AT sobre uma declaração.</summary>
public sealed record AtDeclarationMessage(string? Annex, string? Section, string? Field, string? Code, string Text);

/// <summary>O resultado de validar ou submeter uma declaração num webservice da AT.</summary>
/// <param name="Reached">Se a AT chegou a responder (falso: sem configuração, sem rede, certificado recusado).</param>
/// <param name="Code">O código de resposta da AT; zero é sucesso.</param>
/// <param name="Message">A mensagem da AT, ou a explicação da falha.</param>
/// <param name="DeclarationId">O identificador da declaração, numa submissão aceite.</param>
public sealed record AtDeclarationResult(
    bool Reached,
    int? Code,
    string Message,
    IReadOnlyList<AtDeclarationMessage> Errors,
    IReadOnlyList<AtDeclarationMessage> Alerts,
    long? DeclarationId,
    int? HttpStatus,
    long DurationMilliseconds)
{
    public bool Succeeded => Reached && Code == 0;
}

/// <summary>O que falta para a aplicação conseguir falar com os webservices da AT.</summary>
public sealed record AtGatewayStatus(bool CertificateReady, bool PublicKeyReady, string? Problem)
{
    public bool Ready => CertificateReady && PublicKeyReady;
}

/// <summary>A declaração periódica de IVA no webservice da AT (validar e submeter).</summary>
public interface IVatReturnAtGateway
{
    AtGatewayStatus Status();

    /// <summary>Valida a declaração pelas regras da AT, sem a registar.</summary>
    Task<AtDeclarationResult> ValidateAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default);

    /// <summary>Submete a declaração. <paramref name="acceptAlerts"/>: continuar apesar de alertas.</summary>
    Task<AtDeclarationResult> SubmitAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, bool acceptAlerts, CancellationToken cancellationToken = default);
}
