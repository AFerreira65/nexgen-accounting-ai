using System.Xml.Linq;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// A IES/DA no webservice da AT (F4C-07), pelo manual de integração de 31 de julho de
/// 2025 e pelo WSDL <c>SubmeterDeclaracaoIESService</c> (portas 407 e 707). O transporte
/// e a autenticação são os da DP de IVA (<see cref="AtSoapClient"/>). O ficheiro vai em
/// Base64 simples e a <c>versaoDeclaracao</c> é a do atributo <c>versao</c> do próprio
/// ficheiro, para as duas coisas não divergirem. Ao contrário dos outros serviços, a IES
/// exige as credenciais do contabilista certificado (<see cref="AtActor.CertifiedAccountant"/>)
/// além das do sujeito passivo.
/// </summary>
public sealed class IesAtGateway(AtSoapClient client) : IIesAtGateway
{
    public const string ProductionEndpoint = "https://servicos.portaldasfinancas.gov.pt:407/iesws/SubmeterDeclaracaoIESService";
    public const string TestEndpoint = "https://servicos.portaldasfinancas.gov.pt:707/iesws/SubmeterDeclaracaoIESService";

    private static readonly XNamespace Service = "https://servicos.portaldasfinancas.gov.pt/iesws/SubmeterDeclaracaoIESService/201505";

    public AtGatewayStatus Status() => client.Status();

    public Task<AtDeclarationResult> ValidateAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default) =>
        client.SendAsync(production ? ProductionEndpoint : TestEndpoint, "validarDeclaracao", Body("validarDeclaracaoIESRequest", declarationXml), credentials, cancellationToken);

    public Task<AtDeclarationResult> SubmitAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default) =>
        client.SendAsync(production ? ProductionEndpoint : TestEndpoint, "submeterDeclaracao", Body("submeterDeclaracaoIESRequest", declarationXml), credentials, cancellationToken);

    /// <summary>O corpo SOAP do pedido: a versão do ficheiro e o ficheiro em Base64.</summary>
    public static XElement Body(string requestName, byte[] declarationXml) =>
        new(Service + requestName,
            new XElement("versaoDeclaracao", VersionOf(declarationXml)),
            new XElement("declaracao", Convert.ToBase64String(declarationXml)));

    /// <summary>O atributo <c>versao</c> do elemento raiz da declaração.</summary>
    public static string VersionOf(byte[] declarationXml)
    {
        using var stream = new MemoryStream(declarationXml);
        var root = XDocument.Load(stream).Root;
        return root?.Attribute("versao")?.Value
            ?? throw new InvalidOperationException("O ficheiro da IES não tem o atributo «versao» no elemento raiz.");
    }
}
