using System.Text;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftSalesInvoicesReaderTests
{
    private const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private static string SaftDocument(string sourceDocuments) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="{Namespace}">
          <Header><AuditFileVersion>1.04_01</AuditFileVersion></Header>
          <MasterFiles />
          <SourceDocuments>
        {sourceDocuments}
          </SourceDocuments>
        </AuditFile>
        """;

    private static string Invoice(string number, string date, string status, string net, string tax, string gross) =>
        $"""
          <Invoice>
            <DocumentNumber>{number}</DocumentNumber>
            <DocumentStatus><InvoiceStatus>{status}</InvoiceStatus></DocumentStatus>
            <InvoiceDate>{date}</InvoiceDate>
            <InvoiceType>FT</InvoiceType>
            <Line>
              <LineNumber>1</LineNumber>
              <CreditAmount>{net}</CreditAmount>
              <Tax><TaxType>IVA</TaxType><TaxPercentage>23.00</TaxPercentage></Tax>
            </Line>
            <DocumentTotals><TaxPayable>{tax}</TaxPayable><NetTotal>{net}</NetTotal><GrossTotal>{gross}</GrossTotal></DocumentTotals>
          </Invoice>
        """;

    private static Task<SaftSalesInvoicesSummary> Read(string xml) =>
        new SaftSalesInvoicesReader().ReadSummaryAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [TestMethod]
    public async Task SummarisesInvoicesByMonthAndCarriesTheDeclaredTotals()
    {
        var xml = SaftDocument($"""
            <SalesInvoices>
              <NumberOfEntries>3</NumberOfEntries>
              <TotalDebit>0.00</TotalDebit>
              <TotalCredit>600.00</TotalCredit>
            {Invoice("FT A/1", "2026-08-04", "N", "100.00", "23.00", "123.00")}
            {Invoice("FT A/2", "2026-08-20", "N", "200.00", "46.00", "246.00")}
            {Invoice("FT A/3", "2026-09-02", "N", "300.00", "69.00", "369.00")}
            </SalesInvoices>
            """);

        var summary = await Read(xml);

        Assert.IsTrue(summary.HasSalesInvoices);
        Assert.AreEqual(3, summary.DeclaredNumberOfEntries);
        Assert.AreEqual(600.00m, summary.DeclaredTotalCredit);
        Assert.AreEqual(3, summary.InvoiceCount);
        Assert.AreEqual(0, summary.VoidedCount);
        Assert.AreEqual(600.00m, summary.Net);
        Assert.AreEqual(138.00m, summary.Tax);
        Assert.AreEqual(738.00m, summary.Gross);
        Assert.AreEqual(new DateOnly(2026, 8, 4), summary.FromDate);
        Assert.AreEqual(new DateOnly(2026, 9, 2), summary.ToDate);
        Assert.AreEqual(2, summary.Months.Count);

        var august = summary.Months.Single(month => month.Month == 8);
        Assert.AreEqual(2, august.Count);
        Assert.AreEqual(300.00m, august.Net);
        Assert.AreEqual(69.00m, august.Tax);

        var september = summary.Months.Single(month => month.Month == 9);
        Assert.AreEqual(1, september.Count);
        Assert.AreEqual(300.00m, september.Net);
    }

    [TestMethod]
    public async Task ExcludesVoidedInvoicesFromTheTotalsButCountsThem()
    {
        var xml = SaftDocument($"""
            <SalesInvoices>
              <NumberOfEntries>2</NumberOfEntries>
            {Invoice("FT A/1", "2026-08-04", "N", "100.00", "23.00", "123.00")}
            {Invoice("FT A/2", "2026-08-05", "A", "999.00", "229.77", "1228.77")}
            </SalesInvoices>
            """);

        var summary = await Read(xml);

        Assert.AreEqual(1, summary.InvoiceCount);
        Assert.AreEqual(1, summary.VoidedCount);
        Assert.AreEqual(100.00m, summary.Net);
        Assert.AreEqual(23.00m, summary.Tax);
        Assert.AreEqual(1, summary.Months.Single().Count);
    }

    [TestMethod]
    public async Task AFileWithoutTheBlockReportsNothing()
    {
        var summary = await Read(SaftDocument("    <Payments><NumberOfEntries>0</NumberOfEntries></Payments>"));

        Assert.IsFalse(summary.HasSalesInvoices);
        Assert.AreEqual(0, summary.InvoiceCount);
        Assert.AreEqual(0, summary.Months.Count);
    }

    [TestMethod]
    public async Task MalformedXmlThrowsAReadableFormatError()
    {
        await Assert.ThrowsExactlyAsync<SaftFileFormatException>(() =>
            Read("<AuditFile><SourceDocuments><SalesInvoices><Invoice></SalesInvoices>"));
    }
}
