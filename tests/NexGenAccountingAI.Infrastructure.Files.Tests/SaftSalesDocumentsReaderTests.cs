using System.Text;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftSalesDocumentsReaderTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01">
          <Header><AuditFileVersion>1.04_01</AuditFileVersion><TaxRegistrationNumber>500584095</TaxRegistrationNumber></Header>
          <MasterFiles>
            <Customer><CustomerID>C1</CustomerID><AccountID>2111</AccountID><CustomerTaxID>503504564</CustomerTaxID><CompanyName>Cliente Um, Lda.</CompanyName><BillingAddress><AddressDetail>Rua</AddressDetail><City>Porto</City><PostalCode>4000-001</PostalCode><Country>PT</Country></BillingAddress></Customer>
            <Customer><CustomerID>CF</CustomerID><AccountID>Desconhecido</AccountID><CustomerTaxID>999999990</CustomerTaxID><CompanyName>Consumidor final</CompanyName><BillingAddress><AddressDetail>Desconhecido</AddressDetail><City>Desconhecido</City><PostalCode>Desconhecido</PostalCode><Country>Desconhecido</Country></BillingAddress></Customer>
          </MasterFiles>
          <SourceDocuments>
            <SalesInvoices>
              <NumberOfEntries>3</NumberOfEntries>
              <Invoice>
                <InvoiceNo>FT A/1</InvoiceNo><ATCUD>ABCD-1</ATCUD>
                <DocumentStatus><InvoiceStatus>N</InvoiceStatus></DocumentStatus>
                <InvoiceDate>2026-03-05</InvoiceDate><InvoiceType>FT</InvoiceType><CustomerID>C1</CustomerID>
                <Line><LineNumber>1</LineNumber><CreditAmount>100.004</CreditAmount><Tax><TaxType>IVA</TaxType><TaxPercentage>23</TaxPercentage></Tax></Line>
                <Line><LineNumber>2</LineNumber><CreditAmount>50.00</CreditAmount><Tax><TaxType>IVA</TaxType><TaxPercentage>6.000000</TaxPercentage></Tax></Line>
                <Line><LineNumber>3</LineNumber><CreditAmount>10.00</CreditAmount><Tax><TaxType>IVA</TaxType><TaxPercentage>23</TaxPercentage></Tax></Line>
                <DocumentTotals><TaxPayable>28.31</TaxPayable><NetTotal>160.00</NetTotal><GrossTotal>188.31</GrossTotal></DocumentTotals>
              </Invoice>
              <Invoice>
                <InvoiceNo>NC A/1</InvoiceNo>
                <DocumentStatus><InvoiceStatus>N</InvoiceStatus></DocumentStatus>
                <InvoiceDate>2026-03-10</InvoiceDate><InvoiceType>NC</InvoiceType><CustomerID>CF</CustomerID>
                <Line><LineNumber>1</LineNumber><DebitAmount>20.00</DebitAmount><Tax><TaxType>IVA</TaxType><TaxPercentage>23</TaxPercentage></Tax></Line>
                <DocumentTotals><TaxPayable>4.60</TaxPayable><NetTotal>20.00</NetTotal><GrossTotal>24.60</GrossTotal></DocumentTotals>
              </Invoice>
              <Invoice>
                <InvoiceNo>FT A/2</InvoiceNo>
                <DocumentStatus><InvoiceStatus>A</InvoiceStatus></DocumentStatus>
                <InvoiceDate>2026-03-12</InvoiceDate><InvoiceType>FT</InvoiceType><CustomerID>C1</CustomerID>
                <Line><LineNumber>1</LineNumber><CreditAmount>5.00</CreditAmount><Tax><TaxType>IVA</TaxType><TaxPercentage>0</TaxPercentage></Tax></Line>
                <DocumentTotals><TaxPayable>0.00</TaxPayable><NetTotal>5.00</NetTotal><GrossTotal>5.00</GrossTotal></DocumentTotals>
              </Invoice>
            </SalesInvoices>
          </SourceDocuments>
        </AuditFile>
        """;

    private static Task<SaftSalesDocumentsResult> Read(string xml) =>
        new SaftSalesDocumentsReader().ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [TestMethod]
    public async Task ReadsEachDocumentWithTheCustomerAndTheTaxesByRate()
    {
        var result = await Read(Xml).ConfigureAwait(false);

        Assert.AreEqual("500584095", result.CompanyTaxId);
        Assert.AreEqual(0, result.Warnings.Count);
        Assert.AreEqual(3, result.Documents.Count);

        var invoice = result.Documents[0];
        Assert.AreEqual("FT A/1", invoice.InvoiceNo);
        Assert.AreEqual("ABCD-1", invoice.Atcud);
        Assert.AreEqual(new DateOnly(2026, 3, 5), invoice.InvoiceDate);
        Assert.AreEqual("503504564", invoice.CustomerTaxId);
        Assert.AreEqual("Cliente Um, Lda.", invoice.CustomerName);
        Assert.AreEqual("PT", invoice.CustomerCountry);
        Assert.AreEqual(2, invoice.Taxes.Count, "Uma parte por taxa, a de mais base primeiro.");
        Assert.AreEqual(23m, invoice.Taxes[0].Percentage);
        Assert.AreEqual(110m, invoice.Taxes[0].TaxBase, "As linhas à mesma taxa somam-se; o acerto ao cêntimo fica na taxa maior.");
        Assert.AreEqual(25.31m, invoice.Taxes[0].TaxAmount, "O imposto acerta pelo TaxPayable do documento.");
        Assert.AreEqual(6m, invoice.Taxes[1].Percentage);
        Assert.AreEqual(50m, invoice.Taxes[1].TaxBase);
        Assert.AreEqual(3m, invoice.Taxes[1].TaxAmount);
        Assert.AreEqual(invoice.NetTotal, invoice.Taxes.Sum(tax => tax.TaxBase));
        Assert.AreEqual(invoice.TaxPayable, invoice.Taxes.Sum(tax => tax.TaxAmount));

        var creditNote = result.Documents[1];
        Assert.AreEqual("NC", creditNote.InvoiceType);
        Assert.AreEqual("999999990", creditNote.CustomerTaxId);
        Assert.AreEqual(20m, creditNote.Taxes.Single().TaxBase, "Os valores ficam positivos; o tipo diz o sentido.");
        Assert.AreEqual(4.60m, creditNote.Taxes.Single().TaxAmount);

        Assert.IsTrue(result.Documents[2].IsVoided);
        Assert.AreEqual(0m, result.Documents[2].Taxes.Single().Percentage);
    }

    [TestMethod]
    public async Task AFileWithoutSalesInvoicesIsReported()
    {
        var result = await Read("""
            <?xml version="1.0" encoding="utf-8"?>
            <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01"><Header><TaxRegistrationNumber>500584095</TaxRegistrationNumber></Header><MasterFiles /></AuditFile>
            """).ConfigureAwait(false);

        Assert.AreEqual(0, result.Documents.Count);
        StringAssert.Contains(result.Warnings.Single(), "não é um SAF-T de faturação");
    }

    [TestMethod]
    public async Task AnUnreadableFileIsAFormatError()
    {
        await Assert.ThrowsExactlyAsync<SaftFileFormatException>(() => Read("<AuditFile><Header>")).ConfigureAwait(false);
    }
}
