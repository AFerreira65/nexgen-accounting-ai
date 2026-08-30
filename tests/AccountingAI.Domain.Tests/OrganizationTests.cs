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
    public void CompanyKeepsTheSelectedAccountingStandard()
    {
        var company = new Company(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Microempresa Sintética",
            "DEMO-PT-ME-0001",
            TaxRegion.Mainland,
            accountingStandard: AccountingStandard.SncMicroEntities);

        Assert.AreEqual(AccountingStandard.SncMicroEntities, company.AccountingStandard);
        Assert.IsNull(company.AccountingSetupVersion);
    }

    [TestMethod]
    public void CompanyRejectsAStandardChangeAfterAccountingSetup()
    {
        var company = new Company(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Empresa Sintética",
            "DEMO-PT-0002",
            TaxRegion.Mainland);
        company.MarkAccountingSetup("PT-CC-218-2015-NCRF-v1");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            company.ChangeAccountingStandard(AccountingStandard.SncSmallEntities));
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
    public void CompanyMutationsTrimAndValidate()
    {
        var company = new Company(Guid.NewGuid(), Guid.NewGuid(), "Empresa", "DEMO-PT-0003", TaxRegion.Mainland);

        company.Rename("  Empresa Renomeada, Lda. ");
        company.ChangeTaxRegion(TaxRegion.Madeira);
        company.ChangeBaseCurrency("usd");
        company.Deactivate();

        Assert.AreEqual("Empresa Renomeada, Lda.", company.LegalName);
        Assert.AreEqual(TaxRegion.Madeira, company.TaxRegion);
        Assert.AreEqual("USD", company.BaseCurrency);
        Assert.IsFalse(company.IsActive);
        Assert.ThrowsExactly<ArgumentException>(() => company.ChangeBaseCurrency("EURO"));
        Assert.ThrowsExactly<ArgumentException>(() => company.Rename("  "));
    }

    [TestMethod]
    public void UserAccountRenameAndEmailChange()
    {
        var user = new UserAccount(Guid.NewGuid(), "Ana", "ana@example.pt");

        user.Rename(" Ana Maria ");
        user.ChangeEmail(" Ana.Maria@Example.PT ");
        user.Deactivate();

        Assert.AreEqual("Ana Maria", user.DisplayName);
        Assert.AreEqual("Ana.Maria@Example.PT", user.Email);
        Assert.AreEqual("ANA.MARIA@EXAMPLE.PT", user.NormalizedEmail);
        Assert.IsFalse(user.IsActive);
        Assert.ThrowsExactly<ArgumentException>(() => user.ChangeEmail("sem-arroba"));
    }

    [TestMethod]
    public void CompanyUserRoleChange()
    {
        var membership = new CompanyUser(Guid.NewGuid(), Guid.NewGuid(), CompanyUserRole.Viewer);

        membership.ChangeRole(CompanyUserRole.Accountant);
        membership.Deactivate();

        Assert.AreEqual(CompanyUserRole.Accountant, membership.Role);
        Assert.IsFalse(membership.IsActive);
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
