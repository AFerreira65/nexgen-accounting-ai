using System.Text;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftPartiesReaderTests
{
    private const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private static MemoryStream Document(string masterFiles, string tail = "") => new(Encoding.UTF8.GetBytes(
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="{Namespace}">
          <Header><AuditFileVersion>1.04_01</AuditFileVersion></Header>
          <MasterFiles>
        {masterFiles}
          </MasterFiles>
        {tail}
        </AuditFile>
        """));

    [TestMethod]
    public async Task ReadsCustomersAndSuppliersWithAccountAndCountry()
    {
        using var content = Document(
            """
            <GeneralLedgerAccounts><Account><AccountID>21111</AccountID></Account></GeneralLedgerAccounts>
            <Customer>
              <CustomerID>C1</CustomerID>
              <AccountID>21111</AccountID>
              <CustomerTaxID>500584095</CustomerTaxID>
              <CompanyName> Cliente A, Lda. </CompanyName>
              <BillingAddress><AddressDetail>Rua</AddressDetail><City>Lisboa</City><Country>PT</Country></BillingAddress>
              <ShipToAddress><Country>ES</Country></ShipToAddress>
            </Customer>
            <Customer>
              <CustomerID>C2</CustomerID>
              <AccountID>Desconhecido</AccountID>
              <CustomerTaxID>999999990</CustomerTaxID>
              <CompanyName>Consumidor final</CompanyName>
            </Customer>
            <Supplier>
              <SupplierID>F1</SupplierID>
              <AccountID>22111</AccountID>
              <SupplierTaxID>B12345678</SupplierTaxID>
              <CompanyName>Proveedor B, S.L.</CompanyName>
              <BillingAddress><Country>ES</Country></BillingAddress>
            </Supplier>
            <Product><ProductCode>P1</ProductCode></Product>
            """,
            """
            <SourceDocuments><SalesInvoices><Invoice><Customer><CustomerID>NAO-DEVE-APARECER</CustomerID></Customer></Invoice></SalesInvoices></SourceDocuments>
            """);

        var parties = await new SaftPartiesReader().ReadAsync(content).ConfigureAwait(false);

        Assert.AreEqual(3, parties.Count);
        Assert.AreEqual(new SaftParty(SaftPartyKind.Customer, "C1", "21111", "500584095", "Cliente A, Lda.", "PT", "Rua", "Lisboa"), parties[0]);
        Assert.AreEqual(new SaftParty(SaftPartyKind.Customer, "C2", "Desconhecido", "999999990", "Consumidor final", null), parties[1]);
        Assert.AreEqual(new SaftParty(SaftPartyKind.Supplier, "F1", "22111", "B12345678", "Proveedor B, S.L.", "ES"), parties[2]);
    }

    [TestMethod]
    public async Task FileWithoutPartiesReturnsEmpty()
    {
        using var content = Document(
            "<GeneralLedgerAccounts />",
            "<GeneralLedgerEntries><NumberOfEntries>0</NumberOfEntries></GeneralLedgerEntries>");

        var parties = await new SaftPartiesReader().ReadAsync(content).ConfigureAwait(false);

        Assert.AreEqual(0, parties.Count);
    }

    [TestMethod]
    public async Task MalformedXmlIsReportedAsSaftFormatError()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<AuditFile><MasterFiles><Customer>"));

        await Assert.ThrowsExactlyAsync<SaftFileFormatException>(
            () => new SaftPartiesReader().ReadAsync(content)).ConfigureAwait(false);
    }
}
