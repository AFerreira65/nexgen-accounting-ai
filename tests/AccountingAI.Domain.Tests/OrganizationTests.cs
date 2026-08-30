using AccountingAI.Domain.Organizations;

namespace AccountingAI.Domain.Tests;

[TestClass]
public sealed class OrganizationTests
{
    [TestMethod]
    public void AccountingFirmNormalizesItsSlug()
    {
        var firm = new AccountingFirm(Guid.NewGuid(), "Gabinete Horizonte", "  GABINETE-HORIZONTE  ");

        Assert.AreEqual("gabinete-horizonte", firm.Slug);
    }

    [TestMethod]
    public void CompanyNormalizesCurrencyAndTaxIdentifier()
    {
        var company = new Company(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Empresa Sintética",
            " demo-pt-0001 ",
            TaxRegion.Azores,
            "eur");

        Assert.AreEqual("DEMO-PT-0001", company.TaxRegistrationNumber);
        Assert.AreEqual("EUR", company.BaseCurrency);
        Assert.IsTrue(company.IsActive);
    }

    [TestMethod]
    public void CompanyRejectsInvalidCurrencyCode()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Company(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Empresa Sintética",
            "DEMO-PT-0001",
            TaxRegion.Mainland,
            "EURO"));
    }

    [TestMethod]
    public void UserCompanySettingsAreScopedByCompanyAndUser()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var settings = new UserCompanySettings(companyId, userId);

        settings.Update("pt-PT", "Atlantic/Azores", 2026, InterfaceTheme.Dark, true, 100);

        Assert.AreEqual(companyId, settings.CompanyId);
        Assert.AreEqual(userId, settings.UserId);
        Assert.AreEqual("Atlantic/Azores", settings.TimeZone);
        Assert.AreEqual(2026, settings.DefaultFiscalYear);
        Assert.AreEqual(InterfaceTheme.Dark, settings.Theme);
        Assert.IsTrue(settings.CompactMode);
        Assert.AreEqual(100, settings.PageSize);
    }

    [TestMethod]
    public void UserCompanySettingsRejectInvalidPageSize()
    {
        var settings = new UserCompanySettings(Guid.NewGuid(), Guid.NewGuid());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => settings.Update(
            "pt-PT",
            "Europe/Lisbon",
            2026,
            InterfaceTheme.System,
            false,
            500));
    }
}
