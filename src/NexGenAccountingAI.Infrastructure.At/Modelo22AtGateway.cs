using System.Xml.Linq;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// O Modelo 22 de IRC no webservice da AT (F4C-06), pelo manual de integração de 31 de
/// julho de 2025 e pelo WSDL <c>SubmeterDeclaracaoIRCService</c> (operações
/// <c>validarDeclaracao</c> e <c>submeterDeclaracao</c>). O transporte e a autenticação
/// são os da DP de IVA (<see cref="AtSoapClient"/>); muda o endereço (portas 405 e 705),
/// a versão da declaração e o campo <c>declaracao</c>, que é o ficheiro XML em Base64
/// (sem ZIP nem dupla codificação, ao contrário da DP de IVA).
/// </summary>
public sealed class Modelo22AtGateway(AtSoapClient client, IOptions<AtOptions> options) : IModelo22AtGateway
{
    public const string ProductionEndpoint = "https://servicos.portaldasfinancas.gov.pt:405/dm22ircws/SubmeterDeclaracaoIRCService";
    public const string TestEndpoint = "https://servicos.portaldasfinancas.gov.pt:705/dm22ircws/SubmeterDeclaracaoIRCService";

    private static readonly XNamespace Service = "https://servicos.portaldasfinancas.gov.pt/dm22ircws/SubmeterDeclaracaoIRCService/2020";

    public AtGatewayStatus Status() => client.Status();

    public Task<AtDeclarationResult> ValidateAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default) =>
        client.SendAsync(
            production ? ProductionEndpoint : TestEndpoint, "validarDeclaracao",
            new XElement(Service + "validarDeclaracaoIRCRequest",
                new XElement("versaoDeclaracao", options.Value.Modelo22Version),
                new XElement("declaracao", Convert.ToBase64String(declarationXml))),
            credentials, cancellationToken);

    public Task<AtDeclarationResult> SubmitAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, bool acceptAlerts, CancellationToken cancellationToken = default) =>
        client.SendAsync(
            production ? ProductionEndpoint : TestEndpoint, "submeterDeclaracao",
            new XElement(Service + "submeterDeclaracaoIRCRequest",
                new XElement("versaoDeclaracao", options.Value.Modelo22Version),
                new XElement("declaracao", Convert.ToBase64String(declarationXml)),
                new XElement("aceitaAlertas", acceptAlerts ? "true" : "false")),
            credentials, cancellationToken);

    /// <summary>O corpo SOAP do pedido de validação, para os testes.</summary>
    public static XElement ValidateBody(string version, byte[] declarationXml) =>
        new(Service + "validarDeclaracaoIRCRequest",
            new XElement("versaoDeclaracao", version),
            new XElement("declaracao", Convert.ToBase64String(declarationXml)));
}
