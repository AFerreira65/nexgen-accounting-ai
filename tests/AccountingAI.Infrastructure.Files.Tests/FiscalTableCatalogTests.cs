using AccountingAI.Application.Fiscal;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class FiscalTableCatalogTests
{
    [TestMethod]
    public void IrcRatesFollowTheTransitionOfLaw64Of2025()
    {
        Assert.AreEqual(0.19m, FiscalTableCatalog.For(2026)!.Irc.GeneralRate);
        Assert.AreEqual(0.18m, FiscalTableCatalog.For(2027)!.Irc.GeneralRate);
        Assert.AreEqual(0.17m, FiscalTableCatalog.For(2028)!.Irc.GeneralRate);
        Assert.AreEqual(0.15m, FiscalTableCatalog.For(2026)!.Irc.SmeRate);
        Assert.AreEqual(50_000m, FiscalTableCatalog.For(2026)!.Irc.SmeThreshold);
        Assert.AreEqual(FiscalSourceStatus.Oficial, FiscalTableCatalog.For(2026)!.Irc.Source.Status);
        Assert.AreEqual(FiscalSourceStatus.Secundaria, FiscalTableCatalog.For(2025)!.Irc.Source.Status);
    }

    [TestMethod]
    public void YearsOutsideTheCatalogAreNotInvented()
    {
        Assert.IsNull(FiscalTableCatalog.For(2023));

        var later = FiscalTableCatalog.For(2031)!;
        Assert.AreEqual(2031, later.Year);
        Assert.IsTrue(later.CarriedForward);
        Assert.AreEqual(0.17m, later.Irc.GeneralRate);
        Assert.IsFalse(FiscalTableCatalog.For(2028)!.CarriedForward);

        // As taxas das viaturas eram outras antes de 2025: 2024 fica sem tabela.
        Assert.AreEqual(0, FiscalTableCatalog.For(2024)!.AutonomousTaxation.Count);
        Assert.IsTrue(FiscalTableCatalog.For(2026)!.AutonomousTaxation.Count > 0);
    }

    [TestMethod]
    public void IrcAppliesTheReducedRateOnlyToTheFirstSliceOfAnSme()
    {
        var table = FiscalTableCatalog.For(2026)!;

        Assert.AreEqual(19_000m, table.IrcOn(100_000m, smallOrMediumEnterprise: false));
        // 50 000 × 15% + 50 000 × 19%.
        Assert.AreEqual(17_000m, table.IrcOn(100_000m, smallOrMediumEnterprise: true));
        Assert.AreEqual(4_500m, table.IrcOn(30_000m, smallOrMediumEnterprise: true));
        Assert.AreEqual(0m, table.IrcOn(-5_000m, smallOrMediumEnterprise: true));
    }

    [TestMethod]
    public void StateSurtaxIsAppliedBySlices()
    {
        var table = FiscalTableCatalog.For(2026)!;

        Assert.AreEqual(0m, table.StateSurtaxOn(1_500_000m));
        Assert.AreEqual(15_000m, table.StateSurtaxOn(2_000_000m));
        // 6 000 000 × 3% + 2 500 000 × 5%.
        Assert.AreEqual(305_000m, table.StateSurtaxOn(10_000_000m));
        // 6 000 000 × 3% + 27 500 000 × 5% + 5 000 000 × 9%.
        Assert.AreEqual(2_005_000m, table.StateSurtaxOn(40_000_000m));
    }

    [TestMethod]
    public void VatRatesComeByRegion()
    {
        var vat = FiscalTableCatalog.For(2026)!.Vat;

        Assert.AreEqual(0.23m, vat.Single(item => item.Region == "Continente").Normal);
        Assert.AreEqual(0.16m, vat.Single(item => item.Region == "Acores").Normal);
        Assert.AreEqual(0.12m, vat.Single(item => item.Region == "Madeira").Intermediate);
        Assert.AreEqual(FiscalSourceStatus.Oficial, vat.Single(item => item.Region == "Continente").Source.Status);
        Assert.AreEqual(FiscalSourceStatus.Secundaria, vat.Single(item => item.Region == "Acores").Source.Status);
    }
}
