using AccountingAI.Domain.Accounting;

namespace AccountingAI.Domain.Tests;

[TestClass]
public sealed class PartyTests
{
    [TestMethod]
    public void CreateNormalizesCountryTaxNumberAndName()
    {
        var party = Party.Create(Guid.NewGuid(), " es ", " b 1234 5678 ", "  Proveedor, S.L.  ");

        Assert.AreEqual("ES", party.CountryCode);
        Assert.AreEqual("B12345678", party.TaxRegistrationNumber);
        Assert.AreEqual("Proveedor, S.L.", party.Name);
        Assert.IsTrue(party.IsActive);
        Assert.IsFalse(party.IsCustomer);
        Assert.IsFalse(party.IsSupplier);
    }

    [TestMethod]
    public void CountryDefaultsToPortugal()
    {
        var party = Party.Create(Guid.NewGuid(), null, "500584095", "Cliente, Lda.");

        Assert.AreEqual("PT", party.CountryCode);
    }

    [TestMethod]
    public void CreateRejectsMissingOrMalformedIdentification()
    {
        var companyId = Guid.NewGuid();

        Assert.ThrowsExactly<ArgumentException>(() => Party.Create(Guid.Empty, "PT", "500584095", "Nome"));
        Assert.ThrowsExactly<ArgumentException>(() => Party.Create(companyId, "PRT", "500584095", "Nome"));
        Assert.ThrowsExactly<ArgumentException>(() => Party.Create(companyId, "PT", "   ", "Nome"));
        Assert.ThrowsExactly<ArgumentException>(() => Party.Create(companyId, "PT", new string('1', 31), "Nome"));
        Assert.ThrowsExactly<ArgumentException>(() => Party.Create(companyId, "PT", "500584095", " "));
    }

    [TestMethod]
    public void RolesAccountsAndStateAreEditable()
    {
        var party = Party.Create(Guid.NewGuid(), "PT", "500584095", "Cliente, Lda.");
        var customerAccount = Guid.NewGuid();

        party.SetRoles(isCustomer: true, isSupplier: true);
        party.LinkAccounts(customerAccount, null);
        party.Rename("Cliente e Fornecedor, Lda.");
        party.SetActive(false);

        Assert.IsTrue(party.IsCustomer);
        Assert.IsTrue(party.IsSupplier);
        Assert.AreEqual(customerAccount, party.CustomerAccountId);
        Assert.IsNull(party.SupplierAccountId);
        Assert.AreEqual("Cliente e Fornecedor, Lda.", party.Name);
        Assert.IsFalse(party.IsActive);
    }
}
