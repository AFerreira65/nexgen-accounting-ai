using System.Text;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftAccountsReaderTests
{
    private const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private static string SaftDocument(string masterFiles) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="{Namespace}">
          <Header>
            <AuditFileVersion>1.04_01</AuditFileVersion>
            <TaxRegistrationNumber>500000000</TaxRegistrationNumber>
            <TaxAccountingBasis>I</TaxAccountingBasis>
            <CompanyName>Empresa Sintética, Lda.</CompanyName>
            <FiscalYear>2026</FiscalYear>
            <StartDate>2026-01-01</StartDate>
            <EndDate>2026-12-31</EndDate>
            <CurrencyCode>EUR</CurrencyCode>
          </Header>
          <MasterFiles>
        {masterFiles}
          </MasterFiles>
          <GeneralLedgerEntries>
            <NumberOfEntries>1</NumberOfEntries>
            <Journal>
              <JournalID>OD</JournalID>
              <Transaction><AccountID>NAO-DEVE-APARECER</AccountID></Transaction>
            </Journal>
          </GeneralLedgerEntries>
        </AuditFile>
        """;

    private const string TwoAccounts = """
        <GeneralLedgerAccounts>
          <Account>
            <AccountID>11</AccountID>
            <AccountDescription>Caixa</AccountDescription>
            <OpeningDebitBalance>1500.00</OpeningDebitBalance>
            <OpeningCreditBalance>0.00</OpeningCreditBalance>
            <GroupingCategory>GM</GroupingCategory>
          </Account>
          <Account>
            <AccountID>6221</AccountID>
            <AccountDescription>Material de escritório</AccountDescription>
            <GroupingCategory>GM</GroupingCategory>
          </Account>
          <Account>
            <AccountID>6221</AccountID>
            <AccountDescription>Duplicado a ignorar</AccountDescription>
          </Account>
        </GeneralLedgerAccounts>
        """;

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [TestMethod]
    public async Task ReadsAccountsAndIgnoresEntriesAndDuplicates()
    {
        var accounts = await new SaftAccountsReader()
            .ReadAsync(Utf8(SaftDocument(TwoAccounts)))
            .ConfigureAwait(false);

        Assert.AreEqual(2, accounts.Count);
        CollectionAssert.AreEqual(new[] { "11", "6221" }, accounts.Select(account => account.Code).ToArray());
        Assert.AreEqual("Caixa", accounts[0].Description);
        Assert.AreEqual("Material de escritório", accounts[1].Description);
        Assert.AreEqual(1500.00m, accounts[0].OpeningDebit);
        Assert.AreEqual(0m, accounts[0].OpeningCredit);
        Assert.AreEqual(0m, accounts[1].OpeningDebit);
    }

    [TestMethod]
    public async Task ReturnsEmptyWhenThereIsNoAccountsBlock()
    {
        var accounts = await new SaftAccountsReader()
            .ReadAsync(Utf8(SaftDocument("<Customers />")))
            .ConfigureAwait(false);

        Assert.AreEqual(0, accounts.Count);
    }

    [TestMethod]
    public async Task ReadsAWindows1252EncodedFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var document = SaftDocument(TwoAccounts)
            .Replace("encoding=\"utf-8\"", "encoding=\"Windows-1252\"", StringComparison.Ordinal);
        using var stream = new MemoryStream(Encoding.GetEncoding(1252).GetBytes(document));

        var accounts = await new SaftAccountsReader().ReadAsync(stream).ConfigureAwait(false);

        Assert.AreEqual("Material de escritório", accounts[1].Description);
    }

    [TestMethod]
    public async Task RejectsContentThatIsNotXml()
    {
        await Assert.ThrowsAsync<SaftFileFormatException>(async () =>
            await new SaftAccountsReader().ReadAsync(Utf8("<html><body>não é SAF-T</body>")).ConfigureAwait(false));
    }
}
