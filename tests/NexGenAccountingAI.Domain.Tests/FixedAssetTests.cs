using NexGenAccountingAI.Domain.Accounting;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class FixedAssetTests
{
    private static FixedAsset Asset(
        decimal value, decimal rate, DateOnly start, bool byMonths = false, decimal residual = 0m, DateOnly? disposal = null)
    {
        var asset = FixedAsset.Create(Guid.NewGuid(), "eq-001");
        asset.Update("Equipamento", start, start, value, residual, rate, byMonths, disposal);
        return asset;
    }

    [TestMethod]
    public void StraightLineTakesAFullQuotaInTheFirstYearAndStopsAtTheDepreciableValue()
    {
        var asset = Asset(1000m, 25m, new DateOnly(2024, 9, 15));

        Assert.AreEqual("EQ-001", asset.Code);
        Assert.AreEqual(0m, asset.DepreciationFor(2023));
        Assert.AreEqual(250m, asset.DepreciationFor(2024), "Sem duodécimos, o ano de início leva a quota inteira.");
        Assert.AreEqual(250m, asset.DepreciationFor(2027));
        Assert.AreEqual(1000m, asset.AccumulatedAtEndOf(2027));
        Assert.AreEqual(0m, asset.DepreciationFor(2028), "Totalmente depreciado.");
        Assert.AreEqual(1000m, asset.AccumulatedAtEndOf(2040));
    }

    [TestMethod]
    public void ByMonthsProratesTheFirstYearAndTheRemainderFallsInTheLast()
    {
        var asset = Asset(1200m, 25m, new DateOnly(2024, 10, 1), byMonths: true);

        Assert.AreEqual(75m, asset.DepreciationFor(2024), "Outubro a dezembro: três duodécimos de 300.");
        Assert.AreEqual(300m, asset.DepreciationFor(2025));
        Assert.AreEqual(300m, asset.DepreciationFor(2027));
        Assert.AreEqual(225m, asset.DepreciationFor(2028), "O que faltava para o valor depreciável.");
        Assert.AreEqual(0m, asset.DepreciationFor(2029));
    }

    [TestMethod]
    public void TheResidualValueIsNotDepreciatedAndTheRateIsRounded()
    {
        var asset = Asset(1000m, 33.33m, new DateOnly(2025, 1, 1), residual: 100m);

        Assert.AreEqual(900m, asset.DepreciableValue);
        Assert.AreEqual(299.97m, asset.DepreciationFor(2025));
        Assert.AreEqual(900m, asset.AccumulatedAtEndOf(2028));
        Assert.AreEqual(0.09m, asset.DepreciationFor(2028), "O resto do arredondamento cai no último ano.");
    }

    [TestMethod]
    public void DisposalStopsTheDepreciation()
    {
        var annual = Asset(1000m, 10m, new DateOnly(2024, 1, 1), disposal: new DateOnly(2026, 6, 30));
        Assert.AreEqual(100m, annual.DepreciationFor(2025));
        Assert.AreEqual(0m, annual.DepreciationFor(2026), "Sem duodécimos, o ano da alienação não leva quota.");
        Assert.AreEqual(200m, annual.AccumulatedAtEndOf(2030));

        var monthly = Asset(1200m, 10m, new DateOnly(2024, 1, 1), byMonths: true, disposal: new DateOnly(2026, 6, 30));
        Assert.AreEqual(50m, monthly.DepreciationFor(2026), "Janeiro a maio: os meses até ao anterior ao da alienação.");
        Assert.AreEqual(0m, monthly.DepreciationFor(2027));
    }

    [TestMethod]
    public void InvalidDataIsRejected()
    {
        var asset = FixedAsset.Create(Guid.NewGuid(), "A1");
        var date = new DateOnly(2025, 1, 1);

        Assert.ThrowsExactly<ArgumentException>(() => FixedAsset.Create(Guid.NewGuid(), "com espaço"));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update(" ", date, date, 100m, 0m, 10m, false, null));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update("Ativo", date, date.AddDays(-1), 100m, 0m, 10m, false, null));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update("Ativo", date, date, 0m, 0m, 10m, false, null));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update("Ativo", date, date, 100m, 100m, 10m, false, null));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update("Ativo", date, date, 100m, 0m, 0m, false, null));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update("Ativo", date, date, 100m, 0m, 101m, false, null));
        Assert.ThrowsExactly<ArgumentException>(() => asset.Update("Ativo", date, date, 100m, 0m, 10m, false, date.AddDays(-1)));
    }
}
