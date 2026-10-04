using System.Text;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftValidationModelReaderTests
{
    private const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private static string Document(
        string basis = "I",
        string nif = "500000000",
        string start = "2026-01-01",
        string end = "2026-12-31",
        string numberOfEntries = "2",
        string totalDebit = "300.00",
        string totalCredit = "300.00",
        string firstTransactionDate = "2026-02-10",
        string secondCreditAccount = "2211001",
        string secondCreditAmount = "100.00",
        string secondDebitAmount = "100.00") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="{Namespace}">
          <Header>
            <AuditFileVersion>1.04_01</AuditFileVersion>
            <TaxRegistrationNumber>{nif}</TaxRegistrationNumber>
            <TaxAccountingBasis>{basis}</TaxAccountingBasis>
            <CompanyName>Empresa Sintética, Lda.</CompanyName>
            <FiscalYear>2026</FiscalYear>
            <StartDate>{start}</StartDate>
            <EndDate>{end}</EndDate>
            <CurrencyCode>EUR</CurrencyCode>
            <ProductCompanyTaxID>500000000</ProductCompanyTaxID>
            <SoftwareCertificateNumber>0</SoftwareCertificateNumber>
            <ProductID>NexGen Accounting AI/NexGenAccountingAI</ProductID>
          </Header>
          <MasterFiles>
            <GeneralLedgerAccounts>
              <Account><AccountID>6221</AccountID><AccountDescription>FSE</AccountDescription></Account>
              <Account><AccountID>2211001</AccountID><AccountDescription>Fornecedor</AccountDescription></Account>
              <Account><AccountID>111</AccountID><AccountDescription>Caixa</AccountDescription></Account>
            </GeneralLedgerAccounts>
          </MasterFiles>
          <GeneralLedgerEntries>
            <NumberOfEntries>{numberOfEntries}</NumberOfEntries>
            <TotalDebit>{totalDebit}</TotalDebit>
            <TotalCredit>{totalCredit}</TotalCredit>
            <Journal>
              <JournalID>COMPRAS</JournalID>
              <Description>Diário de compras</Description>
              <Transaction TransactionID="C 1">
                <TransactionDate>{firstTransactionDate}</TransactionDate>
                <Lines>
                  <DebitLine><AccountID>6221</AccountID><DebitAmount><Amount>200.00</Amount></DebitAmount></DebitLine>
                  <CreditLine><AccountID>2211001</AccountID><CreditAmount><Amount>200.00</Amount></CreditAmount></CreditLine>
                </Lines>
              </Transaction>
            </Journal>
            <Journal>
              <JournalID>CAIXA</JournalID>
              <Description>Diário de caixa</Description>
              <Transaction TransactionID="X 1">
                <TransactionDate>2026-03-05</TransactionDate>
                <Lines>
                  <DebitLine><AccountID>111</AccountID><DebitAmount><Amount>{secondDebitAmount}</Amount></DebitAmount></DebitLine>
                  <CreditLine><AccountID>{secondCreditAccount}</AccountID><CreditAmount><Amount>{secondCreditAmount}</Amount></CreditAmount></CreditLine>
                </Lines>
              </Transaction>
            </Journal>
          </GeneralLedgerEntries>
        </AuditFile>
        """;

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [TestMethod]
    public async Task ReadsHeaderAccountsJournalsAndEntryTotals()
    {
        var model = await new SaftValidationModelReader().ReadAsync(Utf8(Document())).ConfigureAwait(false);

        Assert.AreEqual("1.04_01", model.AuditFileVersion);
        Assert.AreEqual("500000000", model.TaxRegistrationNumber);
        Assert.AreEqual("I", model.TaxAccountingBasis);
        Assert.AreEqual(2026, model.FiscalYear);
        Assert.AreEqual(new DateOnly(2026, 1, 1), model.StartDate);
        Assert.AreEqual(new DateOnly(2026, 12, 31), model.EndDate);
        Assert.AreEqual("NexGen Accounting AI/NexGenAccountingAI", model.ProductId);
        Assert.AreEqual("500000000", model.ProductCompanyTaxId);
        Assert.AreEqual("0", model.SoftwareCertificateNumber);

        CollectionAssert.AreEquivalent(new[] { "6221", "2211001", "111" }, model.AccountIds.ToArray());
        Assert.AreEqual(2, model.JournalCount);

        Assert.IsTrue(model.HasGeneralLedgerEntries);
        Assert.AreEqual(2, model.DeclaredNumberOfEntries);
        Assert.AreEqual(300.00m, model.DeclaredTotalDebit);
        Assert.AreEqual(300.00m, model.DeclaredTotalCredit);
        Assert.AreEqual(2, model.TransactionCount);
        Assert.AreEqual(300.00m, model.LineDebitTotal);
        Assert.AreEqual(300.00m, model.LineCreditTotal);

        Assert.AreEqual(0, model.UnbalancedTransactions.Count);
        Assert.AreEqual(0, model.UnresolvedAccountRefs.Count);
        Assert.AreEqual(0, model.OutOfPeriodTransactions.Count);
        Assert.IsFalse(model.FlaggedListsTruncated);
    }

    [TestMethod]
    public async Task FlagsAnUnbalancedTransaction()
    {
        var model = await new SaftValidationModelReader()
            .ReadAsync(Utf8(Document(secondDebitAmount: "100.00", secondCreditAmount: "90.00")))
            .ConfigureAwait(false);

        var flagged = model.UnbalancedTransactions.Single();
        Assert.AreEqual("X 1", flagged.TransactionId);
        Assert.AreEqual(100.00m, flagged.DebitTotal);
        Assert.AreEqual(90.00m, flagged.CreditTotal);
        // O total de linhas do ficheiro reflete o desequilíbrio.
        Assert.AreEqual(300.00m, model.LineDebitTotal);
        Assert.AreEqual(290.00m, model.LineCreditTotal);
    }

    [TestMethod]
    public async Task FlagsAnUnresolvedAccountReference()
    {
        var model = await new SaftValidationModelReader()
            .ReadAsync(Utf8(Document(secondCreditAccount: "9999999")))
            .ConfigureAwait(false);

        var unresolved = model.UnresolvedAccountRefs.Single();
        Assert.AreEqual("X 1", unresolved.TransactionId);
        Assert.AreEqual("9999999", unresolved.AccountId);
    }

    [TestMethod]
    public async Task FlagsATransactionOutsideThePeriod()
    {
        var model = await new SaftValidationModelReader()
            .ReadAsync(Utf8(Document(firstTransactionDate: "2025-12-30")))
            .ConfigureAwait(false);

        var outside = model.OutOfPeriodTransactions.Single();
        Assert.AreEqual("C 1", outside.TransactionId);
        Assert.AreEqual(new DateOnly(2025, 12, 30), outside.Date);
    }

    [TestMethod]
    public async Task AFileWithoutEntriesReportsNone()
    {
        var noEntries = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <AuditFile xmlns="{Namespace}">
              <Header>
                <AuditFileVersion>1.04_01</AuditFileVersion>
                <TaxRegistrationNumber>500000000</TaxRegistrationNumber>
                <TaxAccountingBasis>F</TaxAccountingBasis>
                <CompanyName>Sem contabilidade</CompanyName>
                <FiscalYear>2026</FiscalYear>
                <StartDate>2026-01-01</StartDate>
                <EndDate>2026-12-31</EndDate>
                <CurrencyCode>EUR</CurrencyCode>
              </Header>
              <MasterFiles />
            </AuditFile>
            """;

        var model = await new SaftValidationModelReader().ReadAsync(Utf8(noEntries)).ConfigureAwait(false);

        Assert.IsFalse(model.HasGeneralLedgerEntries);
        Assert.AreEqual(0, model.TransactionCount);
        Assert.AreEqual(0, model.JournalCount);
        Assert.IsNull(model.DeclaredNumberOfEntries);
    }

    [TestMethod]
    public async Task MalformedXmlThrows()
    {
        await Assert.ThrowsExactlyAsync<SaftFileFormatException>(() =>
            new SaftValidationModelReader().ReadAsync(Utf8("<AuditFile><Header>"))).ConfigureAwait(false);
    }
}
