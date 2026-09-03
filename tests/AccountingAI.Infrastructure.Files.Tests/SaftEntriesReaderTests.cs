using System.Text;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftEntriesReaderTests
{
    private const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private static string SaftDocument(string entries) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="{Namespace}">
          <Header><AuditFileVersion>1.04_01</AuditFileVersion></Header>
          <MasterFiles />
        {entries}
        </AuditFile>
        """;

    private const string TwoJournals = """
        <GeneralLedgerEntries>
          <NumberOfEntries>3</NumberOfEntries>
          <TotalDebit>150.00</TotalDebit>
          <TotalCredit>150.00</TotalCredit>
          <Journal>
            <JournalID>COMPRAS</JournalID>
            <Description>Diário de compras</Description>
            <Transaction TransactionID="C 1">
              <TransactionDate>2026-03-05</TransactionDate>
              <Description>Fatura de fornecedor</Description>
              <Lines>
                <DebitLine><AccountID>6221</AccountID><DebitAmount><Amount>100.00</Amount></DebitAmount></DebitLine>
                <CreditLine><AccountID>2211001</AccountID><CreditAmount><Amount>100.00</Amount></CreditAmount></CreditLine>
              </Lines>
            </Transaction>
            <Transaction TransactionID="C 2">
              <TransactionDate>2026-01-31</TransactionDate>
              <Lines>
                <DebitLine><AccountID>6221</AccountID><DebitAmount><Amount>50.00</Amount></DebitAmount></DebitLine>
                <CreditLine><AccountID>2211001</AccountID><CreditAmount><Amount>50.00</Amount></CreditAmount></CreditLine>
              </Lines>
            </Transaction>
          </Journal>
          <Journal>
            <JournalID>VENDAS</JournalID>
            <Description>Diário de vendas</Description>
            <Transaction TransactionID="V 1">
              <TransactionDate>2026-06-20</TransactionDate>
              <Lines>
                <DebitLine><AccountID>2111001</AccountID><DebitAmount><Amount>120.00</Amount></DebitAmount></DebitLine>
                <CreditLine><AccountID>7211</AccountID><CreditAmount><Amount>120.00</Amount></CreditAmount></CreditLine>
              </Lines>
            </Transaction>
          </Journal>
        </GeneralLedgerEntries>
        """;

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [TestMethod]
    public async Task ReadsTheSummaryWithoutMaterialisingLines()
    {
        var summary = await new SaftEntriesReader()
            .ReadSummaryAsync(Utf8(SaftDocument(TwoJournals)))
            .ConfigureAwait(false);

        Assert.IsTrue(summary.HasEntries);
        Assert.AreEqual(3, summary.TransactionCount);
        Assert.AreEqual(3, summary.DeclaredEntryCount);
        Assert.AreEqual(150.00m, summary.TotalDebit);
        Assert.AreEqual(150.00m, summary.TotalCredit);
        Assert.IsTrue(summary.Balanced);
        Assert.AreEqual(new DateOnly(2026, 1, 31), summary.FromDate);
        Assert.AreEqual(new DateOnly(2026, 6, 20), summary.ToDate);
        Assert.AreEqual(2, summary.Journals.Count);
        var compras = summary.Journals.Single(item => item.Code == "COMPRAS");
        Assert.AreEqual("Diário de compras", compras.Description);
        Assert.AreEqual(2, compras.TransactionCount);
        Assert.AreEqual(1, summary.Journals.Single(item => item.Code == "VENDAS").TransactionCount);
    }

    [TestMethod]
    public async Task ReturnsNoEntriesWhenTheBlockIsAbsent()
    {
        var summary = await new SaftEntriesReader()
            .ReadSummaryAsync(Utf8(SaftDocument("")))
            .ConfigureAwait(false);

        Assert.IsFalse(summary.HasEntries);
        Assert.AreEqual(0, summary.TransactionCount);
    }

    [TestMethod]
    public async Task DetectsAnUnbalancedFile()
    {
        var document = SaftDocument(TwoJournals).Replace("<TotalCredit>150.00</TotalCredit>", "<TotalCredit>149.00</TotalCredit>", StringComparison.Ordinal);

        var summary = await new SaftEntriesReader().ReadSummaryAsync(Utf8(document)).ConfigureAwait(false);

        Assert.IsFalse(summary.Balanced);
    }

    [TestMethod]
    public async Task ReadsAWindows1252EncodedFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var document = SaftDocument(TwoJournals).Replace("encoding=\"utf-8\"", "encoding=\"Windows-1252\"", StringComparison.Ordinal);
        using var stream = new MemoryStream(Encoding.GetEncoding(1252).GetBytes(document));

        var summary = await new SaftEntriesReader().ReadSummaryAsync(stream).ConfigureAwait(false);

        Assert.AreEqual("Diário de compras", summary.Journals.Single(item => item.Code == "COMPRAS").Description);
    }

    [TestMethod]
    public async Task RejectsContentThatIsNotXml()
    {
        await Assert.ThrowsAsync<SaftFileFormatException>(async () =>
            await new SaftEntriesReader().ReadSummaryAsync(Utf8("<AuditFile><broken")).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task ReadsTheTransactionsWithTheirLines()
    {
        var read = await new SaftEntriesReader()
            .ReadTransactionsAsync(Utf8(SaftDocument(TwoJournals)))
            .ConfigureAwait(false);

        Assert.AreEqual(3, read.DeclaredEntryCount);
        Assert.AreEqual(150.00m, read.DeclaredTotalDebit);
        Assert.AreEqual(150.00m, read.DeclaredTotalCredit);

        var transactions = read.Items;
        Assert.HasCount(3, transactions);

        var first = transactions[0];
        Assert.AreEqual("COMPRAS", first.JournalCode);
        Assert.AreEqual("Diário de compras", first.JournalDescription);
        Assert.AreEqual("C 1", first.TransactionId);
        Assert.AreEqual(new DateOnly(2026, 3, 5), first.Date);
        Assert.AreEqual("Fatura de fornecedor", first.Description);
        Assert.HasCount(2, first.Lines);
        var debit = first.Lines.Single(line => line.Debit > 0);
        Assert.AreEqual("6221", debit.AccountCode);
        Assert.AreEqual(100.00m, debit.Debit);
        Assert.AreEqual(0m, debit.Credit);
        var credit = first.Lines.Single(line => line.Credit > 0);
        Assert.AreEqual("2211001", credit.AccountCode);
        Assert.AreEqual(100.00m, credit.Credit);

        Assert.AreEqual("VENDAS", transactions[2].JournalCode);
        Assert.AreEqual("V 1", transactions[2].TransactionId);
        Assert.AreEqual("7211", transactions[2].Lines.Single(line => line.Credit > 0).AccountCode);
    }

    [TestMethod]
    public async Task ReadsNoTransactionsWhenTheBlockIsAbsent()
    {
        var read = await new SaftEntriesReader()
            .ReadTransactionsAsync(Utf8(SaftDocument("")))
            .ConfigureAwait(false);

        Assert.AreEqual(0, read.Items.Count);
        Assert.IsNull(read.DeclaredTotalDebit);
    }
}
