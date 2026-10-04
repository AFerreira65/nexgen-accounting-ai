using NexGenAccountingAI.Domain.Saft;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class SaftHeaderTests
{
    [TestMethod]
    public void NormalizesTheTaxNumberAndCurrencyAndFlagsAccounting()
    {
        var header = Create(basis: "c", nif: " 500000000 ", currency: "eur");

        Assert.AreEqual("500000000", header.TaxRegistrationNumber);
        Assert.AreEqual("EUR", header.CurrencyCode);
        Assert.AreEqual("C", header.TaxAccountingBasis);
        Assert.IsTrue(header.IncludesAccounting);
    }

    [TestMethod]
    public void InvoicingBasisIsNotAccounting()
    {
        Assert.IsFalse(Create(basis: "F").IncludesAccounting);
        Assert.IsFalse(Create(basis: "P").IncludesAccounting);
    }

    [TestMethod]
    public void RejectsMissingRequiredFields()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Create(version: " "));
        Assert.ThrowsExactly<ArgumentException>(() => Create(nif: ""));
        Assert.ThrowsExactly<ArgumentException>(() => Create(basis: ""));
    }

    [TestMethod]
    public void RejectsAnInvertedPeriod()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SaftHeader(
            "1.04_01",
            "500000000",
            "I",
            "Empresa Sintética, Lda.",
            2026,
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 1, 1),
            "EUR"));
    }

    [TestMethod]
    public void RejectsAFiscalYearOutsideTheExpectedRange()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(fiscalYear: 1999));
    }

    private static SaftHeader Create(
        string version = "1.04_01",
        string nif = "500000000",
        string basis = "I",
        string currency = "EUR",
        int fiscalYear = 2026) =>
        new(
            version,
            nif,
            basis,
            "Empresa Sintética, Lda.",
            fiscalYear,
            new DateOnly(fiscalYear, 1, 1),
            new DateOnly(fiscalYear, 12, 31),
            currency);
}
