using System.Text;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftEntriesReaderPartyTests
{
    private const string Document = """
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01">
          <Header><AuditFileVersion>1.04_01</AuditFileVersion></Header>
          <MasterFiles>
            <GeneralLedgerAccounts><Account><AccountID>21111</AccountID></Account></GeneralLedgerAccounts>
            <Customer>
              <CustomerID>C1</CustomerID>
              <AccountID>21111</AccountID>
              <CustomerTaxID>500584095</CustomerTaxID>
              <CompanyName>Cliente A, Lda.</CompanyName>
              <BillingAddress><Country>PT</Country></BillingAddress>
            </Customer>
            <Supplier>
              <SupplierID>F1</SupplierID>
              <AccountID>22111</AccountID>
              <SupplierTaxID>B12345678</SupplierTaxID>
              <CompanyName>Proveedor, S.L.</CompanyName>
              <BillingAddress><Country>ES</Country></BillingAddress>
            </Supplier>
          </MasterFiles>
          <GeneralLedgerEntries>
            <NumberOfEntries>2</NumberOfEntries>
            <TotalDebit>150.00</TotalDebit>
            <TotalCredit>150.00</TotalCredit>
            <Journal>
              <JournalID>VD</JournalID>
              <Description>Vendas</Description>
              <Transaction>
                <TransactionID>2026-02-10 VD 1</TransactionID>
                <TransactionDate>2026-02-10</TransactionDate>
                <Description>Venda</Description>
                <GLPostingDate>2026-02-11T09:00:00</GLPostingDate>
                <CustomerID> C1 </CustomerID>
                <Lines>
                  <DebitLine><AccountID>21111</AccountID><DebitAmount>100.00</DebitAmount></DebitLine>
                  <CreditLine><AccountID>7111</AccountID><CreditAmount>100.00</CreditAmount></CreditLine>
                </Lines>
              </Transaction>
              <Transaction>
                <TransactionID>2026-02-12 CP 1</TransactionID>
                <TransactionDate>2026-02-12</TransactionDate>
                <Description>Compra</Description>
                <SupplierID>F1</SupplierID>
                <Lines>
                  <DebitLine><AccountID>6221</AccountID><DebitAmount>50.00</DebitAmount></DebitLine>
                  <CreditLine><AccountID>22111</AccountID><CreditAmount>50.00</CreditAmount></CreditLine>
                </Lines>
              </Transaction>
            </Journal>
          </GeneralLedgerEntries>
        </AuditFile>
        """;

    [TestMethod]
    public async Task ReadsTheTransactionPartyAndTheMasterFileParties()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(Document));

        var read = await new SaftEntriesReader().ReadTransactionsAsync(content).ConfigureAwait(false);

        Assert.AreEqual(2, read.Items.Count);
        Assert.AreEqual("C1", read.Items[0].CustomerId);
        Assert.IsNull(read.Items[0].SupplierId);
        Assert.IsNull(read.Items[1].CustomerId);
        Assert.AreEqual("F1", read.Items[1].SupplierId);
        Assert.AreEqual(2, read.Items[0].Lines.Count);

        Assert.IsNotNull(read.Parties);
        Assert.AreEqual(2, read.Parties!.Count);
        Assert.AreEqual(new SaftParty(SaftPartyKind.Customer, "C1", "21111", "500584095", "Cliente A, Lda.", "PT"), read.Parties[0]);
        Assert.AreEqual(new SaftParty(SaftPartyKind.Supplier, "F1", "22111", "B12345678", "Proveedor, S.L.", "ES"), read.Parties[1]);
    }
}
