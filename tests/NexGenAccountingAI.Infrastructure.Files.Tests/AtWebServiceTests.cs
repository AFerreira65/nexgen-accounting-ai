using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Infrastructure.At;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

/// <summary>
/// O pedido aos webservices da AT, conferido com o manual de integração da declaração
/// periódica de IVA (v1.12): faz-se o papel da AT, decifrando com a chave privada.
/// </summary>
[TestClass]
public sealed class AtWebServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 20, 45, 5, 424, TimeSpan.Zero);

    [TestMethod]
    public void TheSecurityHeaderCanBeReadBackByWhoeverHoldsThePrivateKey()
    {
        using var at = RSA.Create(2048);
        var headers = AtWsSecurity.BuildHeaders(
            [
                new AtCredential("111111111", null, AtActor.Taxpayer),
                new AtCredential("333333333/2", "Sénha-do-CC!", AtActor.CertifiedAccountant),
            ],
            at, Now);

        Assert.HasCount(2, headers);

        // O contribuinte, com plenos poderes dados ao contabilista: só o utilizador.
        var taxpayer = headers[0];
        Assert.AreEqual(AtWsSecurity.Wss + "Security", taxpayer.Name);
        Assert.AreEqual("http://at.pt/actor/SPA", taxpayer.Attribute(AtWsSecurity.Soap + "actor")!.Value);
        Assert.AreEqual("2", taxpayer.Attribute(AtWsSecurity.At + "Version")!.Value);
        var taxpayerToken = taxpayer.Element(AtWsSecurity.Wss + "UsernameToken")!;
        Assert.AreEqual("111111111", taxpayerToken.Element(AtWsSecurity.Wss + "Username")!.Value);
        Assert.HasCount(1, taxpayerToken.Elements().ToList(), "Sem senha não há Password, Nonce nem Created.");

        // O contabilista: a AT decifra a chave do pedido com a sua chave privada, e com ela a senha e o digest.
        var accountant = headers[1];
        Assert.AreEqual("http://at.pt/actor/TOC", accountant.Attribute(AtWsSecurity.Soap + "actor")!.Value);
        var token = accountant.Element(AtWsSecurity.Wss + "UsernameToken")!;
        Assert.AreEqual("333333333/2", token.Element(AtWsSecurity.Wss + "Username")!.Value);
        var created = token.Element(AtWsSecurity.Wss + "Created")!.Value;
        Assert.AreEqual("2026-10-05T20:45:05.424Z", created, "UTC, em claro.");

        var key = at.Decrypt(Convert.FromBase64String(token.Element(AtWsSecurity.Wss + "Nonce")!.Value), RSAEncryptionPadding.Pkcs1);
        Assert.HasCount(16, key, "Chave AES de 128 bits.");
        using var aes = Aes.Create();
        aes.Key = key;
        var passwordElement = token.Element(AtWsSecurity.Wss + "Password")!;
        var password = aes.DecryptEcb(Convert.FromBase64String(passwordElement.Value), PaddingMode.PKCS7);
        Assert.AreEqual("Sénha-do-CC!", Encoding.UTF8.GetString(password));
        var digest = aes.DecryptEcb(Convert.FromBase64String(passwordElement.Attribute("Digest")!.Value), PaddingMode.PKCS7);
        CollectionAssert.AreEqual(SHA1.HashData([.. key, .. Encoding.UTF8.GetBytes(created), .. password]), digest, "SHA-1 de chave + data + senha.");

        // Cada autenticação tem a sua chave: a AT recusa uma repetida (código 13).
        var again = AtWsSecurity.BuildHeaders([new AtCredential("333333333/2", "Sénha-do-CC!", AtActor.CertifiedAccountant)], at, Now);
        Assert.AreNotEqual(token.Element(AtWsSecurity.Wss + "Nonce")!.Value, again[0].Descendants(AtWsSecurity.Wss + "Nonce").Single().Value);

        Assert.ThrowsExactly<ArgumentException>(() => AtWsSecurity.BuildHeaders([], at, Now));
        Assert.ThrowsExactly<ArgumentException>(() => AtWsSecurity.BuildHeaders(
            [new AtCredential("1", "a", AtActor.Taxpayer), new AtCredential("2", "b", AtActor.Taxpayer)], at, Now));
    }

    [TestMethod]
    public void TheModelo22TravelsAsPlainBase64AndTheBodyFollowsTheWsdl()
    {
        var xml = Encoding.UTF8.GetBytes("<Modelo22IRCv2026 xmlns=\"dm22irccli-net\" version=\"v2026\"/>");
        var body = Modelo22AtGateway.ValidateBody("2228", xml);
        XNamespace service = "https://servicos.portaldasfinancas.gov.pt/dm22ircws/SubmeterDeclaracaoIRCService/2020";
        Assert.AreEqual(service + "validarDeclaracaoIRCRequest", body.Name);
        Assert.AreEqual("2228", body.Element("versaoDeclaracao")!.Value);
        // O manual do Modelo 22 pede o ficheiro em Base64, sem ZIP nem dupla codificação.
        CollectionAssert.AreEqual(xml, Convert.FromBase64String(body.Element("declaracao")!.Value));
        StringAssert.Contains(Modelo22AtGateway.TestEndpoint, ":705/dm22ircws/");
        StringAssert.Contains(Modelo22AtGateway.ProductionEndpoint, ":405/dm22ircws/");
    }

    [TestMethod]
    public void TheIesBodyCarriesTheFileVersionAndPlainBase64()
    {
        var xml = Encoding.UTF8.GetBytes("<IES-DA xmlns=\"http://www.portaldasfinancas.gov.pt/de/schema/IES-DA/2021\" versao=\"10\" nif=\"500584095\" ano=\"2026\"/>");
        var body = IesAtGateway.Body("submeterDeclaracaoIESRequest", xml);
        XNamespace service = "https://servicos.portaldasfinancas.gov.pt/iesws/SubmeterDeclaracaoIESService/201505";
        Assert.AreEqual(service + "submeterDeclaracaoIESRequest", body.Name);
        Assert.AreEqual("10", body.Element("versaoDeclaracao")!.Value, "A versão é a do atributo do próprio ficheiro.");
        CollectionAssert.AreEqual(xml, Convert.FromBase64String(body.Element("declaracao")!.Value));
        Assert.IsNull(body.Element("aceitaAlertas"), "O serviço da IES não tem alertas.");
        StringAssert.Contains(IesAtGateway.TestEndpoint, ":707/iesws/");
        StringAssert.Contains(IesAtGateway.ProductionEndpoint, ":407/iesws/");
        Assert.ThrowsExactly<InvalidOperationException>(() => IesAtGateway.VersionOf(Encoding.UTF8.GetBytes("<IES-DA/>")));
    }

    [TestMethod]
    public void TheDeclarationTravelsZippedAndTheEnvelopeFollowsTheWsdl()
    {
        var xml = Encoding.UTF8.GetBytes("<dpiva xmlns=\"http://www.at.gov.pt/schemas/dpiva\" version=\"06\"/>");
        var packed = VatReturnAtGateway.Pack(xml);

        // Base64 de (Base64 do ZIP), como diz o manual; lá dentro, o ficheiro tal como foi gerado.
        var zip = Convert.FromBase64String(Encoding.ASCII.GetString(Convert.FromBase64String(packed)));
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var entry = new StreamReader(archive.Entries.Single().Open(), Encoding.UTF8);
        Assert.AreEqual(Encoding.UTF8.GetString(xml), entry.ReadToEnd());

        using var at = RSA.Create(2048);
        XNamespace service = "https://servicos.portaldasfinancas.gov.pt/dpivaws/DeclaracaoPeriodicaIVAWebService";
        var envelope = VatReturnAtGateway.Envelope(
            new XElement(service + "validarDeclaracaoPeriodicaIVARequest", new XElement("versaoDeclaracao", "2016"), new XElement("declaracao", packed)),
            [new AtCredential("500584095/3", "senha", AtActor.Taxpayer)], at, Now);
        var root = envelope.Root!;
        Assert.AreEqual(AtWsSecurity.Soap + "Envelope", root.Name);
        Assert.HasCount(1, root.Element(AtWsSecurity.Soap + "Header")!.Elements(AtWsSecurity.Wss + "Security").ToList());
        var request = root.Element(AtWsSecurity.Soap + "Body")!.Elements().Single();
        Assert.AreEqual(service + "validarDeclaracaoPeriodicaIVARequest", request.Name);
        // O esquema do WSDL não qualifica os elementos de dentro.
        Assert.AreEqual(XNamespace.None, request.Elements().First().Name.Namespace);
        Assert.IsFalse(envelope.ToString().Contains("senha", StringComparison.Ordinal), "A senha nunca vai em claro.");
    }

    [TestMethod]
    public void ReadsWhatTheTaxAuthorityAnswers()
    {
        const string prefix = "<S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\"><S:Body>";
        const string suffix = "</S:Body></S:Envelope>";
        const string ns = "xmlns:ns2=\"https://servicos.portaldasfinancas.gov.pt/dpivaws/DeclaracaoPeriodicaIVAWebService\"";

        var valid = VatReturnAtGateway.Parse(
            $"{prefix}<ns2:validarDeclaracaoPeriodicaIVAResponse {ns}><codigo>0</codigo><mensagem>Declaração válida.</mensagem></ns2:validarDeclaracaoPeriodicaIVAResponse>{suffix}", 200, 120);
        Assert.IsTrue(valid.Succeeded);
        Assert.AreEqual("Declaração válida.", valid.Message);

        var invalid = VatReturnAtGateway.Parse(
            $"{prefix}<ns2:validarDeclaracaoPeriodicaIVAResponse {ns}><codigo>-11</codigo><mensagem>A declaração apresenta um ou mais erros e/ou alertas.</mensagem>" +
            "<alertas><temMaisAlertas>false</temMaisAlertas><alerta><anexo>Rosto</anexo><quadro>06</quadro><campo>61</campo><codigo>A01</codigo><mensagem>Confirme o excesso a reportar.</mensagem></alerta></alertas>" +
            "<erros><temMaisErros>false</temMaisErros><erro><anexo>Rosto</anexo><quadro>06</quadro><campo>90</campo><codigo>E15</codigo><mensagem>O campo 90 não corresponde à soma.</mensagem></erro></erros>" +
            $"</ns2:validarDeclaracaoPeriodicaIVAResponse>{suffix}", 200, 80);
        Assert.IsTrue(invalid.Reached);
        Assert.IsFalse(invalid.Succeeded);
        Assert.AreEqual(-11, invalid.Code);
        Assert.AreEqual(new AtDeclarationMessage("Rosto", "06", "90", "E15", "O campo 90 não corresponde à soma."), invalid.Errors.Single());
        Assert.AreEqual("61", invalid.Alerts.Single().Field);

        var submitted = VatReturnAtGateway.Parse(
            $"{prefix}<ns2:submeterDeclaracaoPeriodicaIVAResponse {ns}><codigo>0</codigo><mensagem>Declaração submetida com sucesso</mensagem>" +
            $"<dadosSubmissao><data>2026-10-05T21:00:00</data><ano>2026</ano><periodo>09</periodo><idDeclaracao>123456789012</idDeclaracao></dadosSubmissao></ns2:submeterDeclaracaoPeriodicaIVAResponse>{suffix}", 200, 300);
        Assert.AreEqual(123456789012L, submitted.DeclarationId);

        var wrongPassword = VatReturnAtGateway.Parse(
            $"{prefix}<ns2:validarDeclaracaoPeriodicaIVAResponse {ns}><codigo>99</codigo><mensagem>Erro na validação da senha.</mensagem></ns2:validarDeclaracaoPeriodicaIVAResponse>{suffix}", 200, 50);
        Assert.AreEqual(99, wrongPassword.Code);
        Assert.IsFalse(wrongPassword.Succeeded);

        var fault = VatReturnAtGateway.Parse($"{prefix}<S:Fault><faultcode>S:Server</faultcode><faultstring>Pedido SOAP inválido</faultstring></S:Fault>{suffix}", 500, 10);
        Assert.IsTrue(fault.Reached);
        StringAssert.Contains(fault.Message, "Pedido SOAP inválido");

        var garbage = VatReturnAtGateway.Parse("<html>403 Forbidden", 403, 5);
        Assert.IsFalse(garbage.Reached);
        StringAssert.Contains(garbage.Message, "403");
    }
}
