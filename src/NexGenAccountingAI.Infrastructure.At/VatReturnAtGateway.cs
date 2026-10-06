using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// A declaração periódica de IVA no webservice da AT (F4A-01/02), pelo manual de
/// integração v1.12 e pelo WSDL <c>DeclaracaoPeriodicaIVAWebService</c>:
/// <list type="bullet">
/// <item>SOAP 1.1 sobre HTTPS, com o certificado do produtor de software (o de testes
/// só é aceite no endereço de testes, porta 706; produção é a 406);</item>
/// <item>as credenciais vão no cabeçalho, cifradas (<see cref="AtWsSecurity"/>);</item>
/// <item>a declaração vai comprimida (ZIP) e em Base64, com <c>versaoDeclaracao</c> «2016».</item>
/// </list>
/// Nenhuma senha nem o conteúdo cifrado ficam em logs.
/// </summary>
public sealed class VatReturnAtGateway(AtSoapClient client) : IVatReturnAtGateway
{
    public const string ProductionEndpoint = "https://servicos.portaldasfinancas.gov.pt:406/dpivaws/DeclaracaoPeriodicaIVAWebService";
    public const string TestEndpoint = "https://servicos.portaldasfinancas.gov.pt:706/dpivaws/DeclaracaoPeriodicaIVAWebService";
    public const string DeclarationVersion = "2016";

    private static readonly XNamespace Service = "https://servicos.portaldasfinancas.gov.pt/dpivaws/DeclaracaoPeriodicaIVAWebService";

    public AtGatewayStatus Status() => client.Status();

    public Task<AtDeclarationResult> ValidateAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default) =>
        SendAsync(
            "validarDeclaracao",
            new XElement(Service + "validarDeclaracaoPeriodicaIVARequest",
                new XElement("versaoDeclaracao", DeclarationVersion),
                new XElement("declaracao", Pack(declarationXml))),
            credentials, production, cancellationToken);

    public Task<AtDeclarationResult> SubmitAsync(
        byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, bool acceptAlerts, CancellationToken cancellationToken = default) =>
        SendAsync(
            "submeterDeclaracao",
            new XElement(Service + "submeterDeclaracaoPeriodicaIVARequest",
                new XElement("versaoDeclaracao", DeclarationVersion),
                new XElement("declaracao", Pack(declarationXml)),
                new XElement("aceitaAlertas", acceptAlerts ? "true" : "false")),
            credentials, production, cancellationToken);

    /// <summary>
    /// «Conversão para base64 do resultado do base64 do zip do ficheiro da declaração» (manual,
    /// campo 1.2): o ficheiro comprimido em ZIP, em Base64, e esse texto outra vez em Base64,
    /// que é a forma de um <c>xsd:base64Binary</c>.
    /// </summary>
    public static string Pack(byte[] declarationXml)
    {
        ArgumentNullException.ThrowIfNull(declarationXml);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("dpiva.xml", CompressionLevel.Optimal).Open();
            entry.Write(declarationXml);
        }

        return Convert.ToBase64String(Encoding.ASCII.GetBytes(Convert.ToBase64String(buffer.ToArray())));
    }

    /// <summary>O envelope SOAP do pedido: cabeçalho de autenticação e corpo.</summary>
    public static XDocument Envelope(XElement body, IReadOnlyList<AtCredential> credentials, RSA authenticationPublicKey, DateTimeOffset now) =>
        new(new XElement(AtWsSecurity.Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "S", AtWsSecurity.Soap.NamespaceName),
            new XElement(AtWsSecurity.Soap + "Header", AtWsSecurity.BuildHeaders(credentials, authenticationPublicKey, now)),
            new XElement(AtWsSecurity.Soap + "Body", body)));

    /// <summary>Lê a resposta da AT: o código, a mensagem, os erros e alertas e, numa submissão, o identificador.</summary>
    public static AtDeclarationResult Parse(string responseXml, int? httpStatus, long durationMilliseconds)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(responseXml);
        }
        catch (XmlException)
        {
            return new AtDeclarationResult(false, null, $"A AT respondeu com um conteúdo que não é XML (HTTP {httpStatus}).", [], [], null, httpStatus, durationMilliseconds);
        }

        var fault = document.Descendants(AtWsSecurity.Soap + "Fault").FirstOrDefault();
        if (fault is not null)
        {
            var text = fault.Elements().FirstOrDefault(element => element.Name.LocalName == "faultstring")?.Value.Trim();
            return new AtDeclarationResult(true, null, $"A AT recusou o pedido: {text ?? "erro sem descrição"}.", [], [], null, httpStatus, durationMilliseconds);
        }

        var response = document.Descendants().FirstOrDefault(element => element.Name.LocalName.EndsWith("Response", StringComparison.Ordinal));
        if (response is null)
        {
            return new AtDeclarationResult(false, null, $"A resposta da AT não tem o formato esperado (HTTP {httpStatus}).", [], [], null, httpStatus, durationMilliseconds);
        }

        string? Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

        List<AtDeclarationMessage> Messages(string container, string item) =>
            response.Elements().Where(element => element.Name.LocalName == container)
                .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == item))
                .Select(entry => new AtDeclarationMessage(
                    Child(entry, "anexo"),
                    Child(entry, "quadro"),
                    Child(entry, "campo"),
                    Child(entry, "codigo"),
                    Child(entry, "mensagem") ?? Child(entry, "descricao") ?? string.Join(" ", entry.Elements().Select(value => value.Value.Trim()).Where(value => value.Length > 0))))
                .ToList();

        var code = int.TryParse(Child(response, "codigo"), out var parsed) ? parsed : (int?)null;
        var submission = response.Elements().FirstOrDefault(element => element.Name.LocalName == "dadosSubmissao");
        var declarationId = submission is not null && long.TryParse(Child(submission, "idDeclaracao"), out var id) ? id : (long?)null;
        return new AtDeclarationResult(
            true, code, Child(response, "mensagem") ?? (code == 0 ? "Sem erros." : "A AT não devolveu mensagem."),
            Messages("erros", "erro"), Messages("alertas", "alerta"), declarationId, httpStatus, durationMilliseconds);
    }

    private Task<AtDeclarationResult> SendAsync(
        string operation, XElement body, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken) =>
        client.SendAsync(production ? ProductionEndpoint : TestEndpoint, operation, body, credentials, cancellationToken);
}
