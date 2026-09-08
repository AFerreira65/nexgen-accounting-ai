using AccountingAI.Application.CrossCheck;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class CrossCheckBuilderTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static CrossCheckInvoiceInput Invoice(
        string invoiceNo, DateOnly date, decimal baseAmount, decimal vat, string nif = "500000001") =>
        new(Guid.NewGuid(), nif, "Fornecedor " + nif, invoiceNo, null, date,
            baseAmount, vat, decimal.Round(baseAmount + vat, 2));

    private static CrossCheckPurchaseEntryInput Entry(
        string reference, DateOnly date, string documentReference, decimal purchaseBase, decimal vat) =>
        new(reference, date, documentReference, "Compra", purchaseBase, vat);

    private static CrossCheckReport Build(
        IReadOnlyList<CrossCheckInvoiceInput> invoices,
        IReadOnlyList<CrossCheckPurchaseEntryInput> entries) =>
        CrossCheckBuilder.Build(From, To, invoices, entries, [], 0m, 0m);

    [TestMethod]
    public void MatchesAnInvoiceToAnEntryByNormalisedNumberWithinTheDateTolerance()
    {
        var report = Build(
            [Invoice("FT 2026/418", new DateOnly(2026, 8, 29), 1200m, 276m)],
            [Entry("COMP-1", new DateOnly(2026, 9, 3), "FT 2026/418 · Norte Office, Lda.", 1200m, 276m)]);

        Assert.AreEqual(1, report.Purchases.MatchedCount);
        Assert.AreEqual(0, report.Purchases.EfaturaWithoutAccounting.Count);
        Assert.AreEqual(0, report.Purchases.AccountingWithoutEfatura.Count);
        Assert.IsTrue(report.Purchases.Balanced);
        Assert.AreEqual(0m, report.Purchases.BaseDelta);
    }

    [TestMethod]
    public void LeavesAnInvoiceUnbookedWhenNothingInAccountingMatches()
    {
        var report = Build(
            [Invoice("FT 2026/418", new DateOnly(2026, 8, 29), 1200m, 276m)],
            [Entry("COMP-1", new DateOnly(2026, 8, 29), "FT 2026/999", 500m, 115m)]);

        Assert.AreEqual(0, report.Purchases.MatchedCount);
        Assert.AreEqual(1, report.Purchases.UnbookedCount);
        Assert.AreEqual("FT 2026/418", report.Purchases.EfaturaWithoutAccounting[0].InvoiceNo);
        Assert.AreEqual(1, report.Purchases.AccountingWithoutEfatura.Count);
    }

    [TestMethod]
    public void DoesNotMatchWhenTheDateGapExceedsTheTolerance()
    {
        var report = Build(
            [Invoice("FT 2026/418", new DateOnly(2026, 8, 1), 1200m, 276m)],
            [Entry("COMP-1", new DateOnly(2026, 10, 15), "FT 2026/418", 1200m, 276m)]);

        Assert.AreEqual(0, report.Purchases.MatchedCount);
        Assert.AreEqual(1, report.Purchases.UnbookedCount);
    }

    [TestMethod]
    public void ReportsTheDeltaWhenAMatchedEntryHasDifferentValues()
    {
        var report = Build(
            [Invoice("FT 2026/418", new DateOnly(2026, 8, 29), 1200m, 276m)],
            [Entry("COMP-1", new DateOnly(2026, 8, 29), "FT 2026/418", 1000m, 230m)]);

        Assert.AreEqual(1, report.Purchases.MatchedCount);
        Assert.AreEqual(-200m, report.Purchases.BaseDelta);
        Assert.AreEqual(-46m, report.Purchases.VatDelta);
        Assert.IsFalse(report.Purchases.Balanced);
    }

    [TestMethod]
    public void BucketsTotalsByCalendarMonth()
    {
        var report = Build(
            [
                Invoice("A/1", new DateOnly(2026, 7, 10), 100m, 23m),
                Invoice("A/2", new DateOnly(2026, 8, 11), 200m, 46m)
            ],
            [Entry("C-1", new DateOnly(2026, 7, 10), "A/1", 100m, 23m)]);

        Assert.AreEqual(2, report.Purchases.Months.Count);
        var july = report.Purchases.Months.Single(month => month.Month == 7);
        Assert.AreEqual(100m, july.EfaturaBase);
        Assert.AreEqual(100m, july.AccountingBase);
        Assert.IsTrue(july.Balanced);
        var august = report.Purchases.Months.Single(month => month.Month == 8);
        Assert.AreEqual(200m, august.EfaturaBase);
        Assert.AreEqual(0m, august.AccountingBase);
        Assert.AreEqual(-200m, august.BaseDelta);
    }

    [TestMethod]
    public void EachEntryMatchesAtMostOneInvoice()
    {
        var report = Build(
            [
                Invoice("FT 100", new DateOnly(2026, 5, 2), 50m, 11.5m),
                Invoice("FT 100", new DateOnly(2026, 5, 3), 50m, 11.5m)
            ],
            [Entry("C-1", new DateOnly(2026, 5, 2), "FT 100", 50m, 11.5m)]);

        Assert.AreEqual(1, report.Purchases.MatchedCount);
        Assert.AreEqual(1, report.Purchases.UnbookedCount);
    }

    [TestMethod]
    public void CarriesTheSalesMonthsAndPeriodTotalsThrough()
    {
        var report = CrossCheckBuilder.Build(
            From, To, [], [],
            [new CrossCheckSalesMonthInput(2026, 8, 2500m, 575m)],
            4476m, 4476m);

        Assert.AreEqual(2500m, report.Sales.Income);
        Assert.AreEqual(575m, report.Sales.OutputVat);
        Assert.AreEqual(3075m, report.Sales.Total);
        Assert.IsTrue(report.Totals.Balanced);
    }

    [TestMethod]
    public void IgnoresInvoiceNumbersTooShortToMatchReliably()
    {
        var report = Build(
            [Invoice("7", new DateOnly(2026, 4, 1), 10m, 2.3m)],
            [Entry("C-1", new DateOnly(2026, 4, 1), "7", 10m, 2.3m)]);

        Assert.AreEqual(0, report.Purchases.MatchedCount);
        Assert.AreEqual(1, report.Purchases.UnbookedCount);
        Assert.AreEqual(1, report.Purchases.AccountingWithoutEfatura.Count);
    }
}
