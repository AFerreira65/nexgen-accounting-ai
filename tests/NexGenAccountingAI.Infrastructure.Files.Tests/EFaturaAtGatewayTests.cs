using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Infrastructure.At;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

/// <summary>A consulta de faturas do e-Fatura (<c>fatshare</c>), conferida com o WSDL e o manual «Aspetos Específicos».</summary>
[TestClass]
public sealed class EFaturaAtGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 30, 15, 420, TimeSpan.Zero);

    [TestMethod]
    public void TheRequestCanBeReadBackByWhoeverHoldsThePrivateKey()
    {
        using var at = RSA.Create(2048);
        var envelope = EFaturaAtGateway.Envelope(
            new AtCredential("500584095/1", "Sénha-WFA!", AtActor.Taxpayer), "500584095",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 2, at, Now);

        XNamespace fat = "http://factemi.at.min_financas.pt/fatshareInvoices";
        var request = envelope.Descendants(fat + "InvoicesRequest").Single();
        Assert.AreEqual("500584095", request.Element(fat + "CustomerTaxID")!.Value);
        Assert.AreEqual("2026-09-01", request.Element(fat + "StartDate")!.Value);
        Assert.AreEqual("2026-09-30", request.Element(fat + "EndDate")!.Value);
        Assert.AreEqual("2", request.Element(fat + "Pagination")!.Element(fat + "nPage")!.Value);
        Assert.AreEqual("5000", request.Element(fat + "Pagination")!.Element(fat + "nDocsPage")!.Value);
        Assert.IsNull(request.Element(fat + "TaxRegistrationNumber"), "Só adquirente: é um ou outro.");

        // O cabeçalho simples do manual: sem ator nem Digest, e com a data também cifrada.
        var security = envelope.Descendants(AtWsSecurity.Wss + "Security").Single();
        Assert.IsNull(security.Attribute(AtWsSecurity.Soap + "actor"));
        var token = security.Element(AtWsSecurity.Wss + "UsernameToken")!;
        Assert.AreEqual("500584095/1", token.Element(AtWsSecurity.Wss + "Username")!.Value);
        Assert.IsNull(token.Element(AtWsSecurity.Wss + "Password")!.Attribute("Digest"));

        var key = at.Decrypt(Convert.FromBase64String(token.Element(AtWsSecurity.Wss + "Nonce")!.Value), RSAEncryptionPadding.Pkcs1);
        Assert.HasCount(16, key);
        Assert.AreEqual("Sénha-WFA!", Decipher(key, token.Element(AtWsSecurity.Wss + "Password")!.Value));
        Assert.AreEqual("2026-10-07T09:30:15.42Z", Decipher(key, token.Element(AtWsSecurity.Wss + "Created")!.Value));
    }

    [TestMethod]
    public void ThePeriodIsSplitByMonthBecauseTheAtAnswersToOneMonthAtATime()
    {
        var intervals = EFaturaAtGateway.MonthlyIntervals(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 10));

        CollectionAssert.AreEqual(
            new[]
            {
                (new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31)),
                (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
                (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10)),
            },
            intervals.ToArray());
        Assert.HasCount(1, EFaturaAtGateway.MonthlyIntervals(new DateOnly(2026, 2, 3), new DateOnly(2026, 2, 3)));
        Assert.ThrowsExactly<ArgumentException>(() => EFaturaAtGateway.MonthlyIntervals(new DateOnly(2026, 2, 3), new DateOnly(2026, 2, 2)));
    }

    [TestMethod]
    public void ASuccessfulPageYieldsTheInvoicesAndSkipsTheIncompleteOnes()
    {
        var page = EFaturaAtGateway.ParseResponse(Response(
            200, "Operação efetuada com sucesso.",
            """
            <fat:InvoicesList>
              <fat:Invoice>
                <fat:InvoiceNo>FT 2026/15</fat:InvoiceNo><fat:ATCUD>JJ1234-15</fat:ATCUD><fat:InvoiceDate>2026-09-12</fat:InvoiceDate>
                <fat:InvoiceType>FT</fat:InvoiceType><fat:SelfBillingIndicator>0</fat:SelfBillingIndicator>
                <fat:TaxRegistrationNumber>509999999</fat:TaxRegistrationNumber><fat:CashVATSchemeIndicator>0</fat:CashVATSchemeIndicator>
                <fat:Sector>C</fat:Sector>
                <fat:LineSummary><fat:TaxAmount>23.00</fat:TaxAmount></fat:LineSummary>
                <fat:TaxPayable>23.00</fat:TaxPayable><fat:NetTotal>100.00</fat:NetTotal><fat:GrossTotal>123.00</fat:GrossTotal>
              </fat:Invoice>
              <fat:Invoice>
                <fat:InvoiceNo>FS 9/1</fat:InvoiceNo><fat:InvoiceDate>2026-09-30</fat:InvoiceDate><fat:InvoiceType>FS</fat:InvoiceType>
                <fat:TaxRegistrationNumber>500888888</fat:TaxRegistrationNumber>
                <fat:TaxPayable>0.00</fat:TaxPayable><fat:NetTotal>12.50</fat:NetTotal><fat:GrossTotal>12.50</fat:GrossTotal>
              </fat:Invoice>
              <fat:Invoice>
                <fat:InvoiceNo>SEM-DATA</fat:InvoiceNo><fat:TaxRegistrationNumber>500777777</fat:TaxRegistrationNumber>
                <fat:TaxPayable>1</fat:TaxPayable><fat:NetTotal>1</fat:NetTotal><fat:GrossTotal>2</fat:GrossTotal>
              </fat:Invoice>
              <fat:PaginationTotal><fat:totalDocs>3</fat:totalDocs><fat:totalDocsSent>3</fat:totalDocsSent><fat:totalPages>4</fat:totalPages><fat:nPage>1</fat:nPage></fat:PaginationTotal>
            </fat:InvoicesList>
            """));

        Assert.AreEqual(200, page.Code);
        Assert.AreEqual(4, page.TotalPages);
        Assert.HasCount(2, page.Invoices);
        var first = page.Invoices[0];
        Assert.AreEqual("509999999", first.SupplierTaxId);
        Assert.AreEqual("FT 2026/15", first.InvoiceNo);
        Assert.AreEqual("JJ1234-15", first.Atcud);
        Assert.AreEqual(new DateOnly(2026, 9, 12), first.InvoiceDate);
        Assert.AreEqual("FT", first.InvoiceType);
        Assert.AreEqual("C", first.Sector);
        Assert.AreEqual(100.00m, first.NetTotal);
        Assert.AreEqual(23.00m, first.TaxPayable);
        Assert.AreEqual(123.00m, first.GrossTotal);
        Assert.IsNull(page.Invoices[1].Atcud);
        Assert.HasCount(1, page.Warnings);
        StringAssert.Contains(page.Warnings[0], "SEM-DATA");
    }

    [TestMethod]
    public void AnEmptyMonthIsStillSuccess()
    {
        var page = EFaturaAtGateway.ParseResponse(Response(200, "Operação efetuada com sucesso.", string.Empty));

        Assert.AreEqual(200, page.Code);
        Assert.IsEmpty(page.Invoices);
        Assert.AreEqual(1, page.TotalPages);
    }

    [TestMethod]
    public void TheAtCodeForAnEmptyMonthIsNotARefusal()
    {
        // Resposta real da AT (2026-10-07) a um mês sem documentos.
        var page = EFaturaAtGateway.ParseResponse(Response(486, "Lista de faturas vazia.", string.Empty));

        Assert.AreEqual(200, page.Code);
        Assert.IsEmpty(page.Invoices);
    }

    [TestMethod]
    public void RefusalsAndBrokenAnswersCarryTheReason()
    {
        var refused = EFaturaAtGateway.ParseResponse(Response(414, "O nif indicado não corresponde ao de login", string.Empty));
        Assert.AreEqual(414, refused.Code);
        StringAssert.Contains(refused.Message, "414");
        StringAssert.Contains(refused.Message, "login");

        var fault = EFaturaAtGateway.ParseResponse(
            """<S:Envelope xmlns:S="http://schemas.xmlsoap.org/soap/envelope/"><S:Body><S:Fault><faultcode>S:Client</faultcode><faultstring>Utilizador inválido</faultstring></S:Fault></S:Body></S:Envelope>""");
        Assert.IsNull(fault.Code);
        StringAssert.Contains(fault.Message, "Utilizador inválido");

        var html = EFaturaAtGateway.ParseResponse("<html><body>Bad gateway");
        Assert.IsNull(html.Code);
        StringAssert.Contains(html.Message, "XML");
    }

    private static string Response(int code, string description, string list) =>
        $"""
        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/"><SOAP-ENV:Body>
        <fat:InvoicesResponse xmlns:fat="http://factemi.at.min_financas.pt/fatshareInvoices">
        {list}
        <fat:estadoExecucao><fat:EstadoOperacao>{code}</fat:EstadoOperacao><fat:Desc>{description}</fat:Desc></fat:estadoExecucao>
        </fat:InvoicesResponse></SOAP-ENV:Body></SOAP-ENV:Envelope>
        """;

    private static string Decipher(byte[] key, string base64)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return Encoding.UTF8.GetString(aes.DecryptEcb(Convert.FromBase64String(base64), PaddingMode.PKCS7));
    }
}
