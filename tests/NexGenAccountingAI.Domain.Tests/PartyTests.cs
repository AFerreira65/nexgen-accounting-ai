using NexGenAccountingAI.Domain.Accounting;

namespace NexGenAccountingAI.Domain.Tests;

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
    public void AddressIsOptionalAndTheCompanyPostalCodeIsPortuguese()
    {
        var party = Party.Create(Guid.NewGuid(), "ES", "B12345678", "Proveedor, S.L.");
        party.SetAddress("  Calle Mayor, 1 ", " ", "28001");
        Assert.AreEqual("Calle Mayor, 1", party.AddressDetail);
        Assert.IsNull(party.City);
        Assert.AreEqual("28001", party.PostalCode, "Um terceiro pode ter um código postal estrangeiro.");
        Assert.ThrowsExactly<ArgumentException>(() => party.SetAddress(new string('x', 211), null, null));

        var company = new NexGenAccountingAI.Domain.Organizations.Company(
            Guid.NewGuid(), Guid.NewGuid(), "Empresa, Lda.", "500584095", NexGenAccountingAI.Domain.Organizations.TaxRegion.Mainland);
        company.SetAddress("Rua Nova, 10", "Lisboa", "1000-001");
        Assert.AreEqual("1000-001", company.PostalCode);
        Assert.ThrowsExactly<ArgumentException>(() => company.SetAddress("Rua Nova, 10", "Lisboa", "1000001"));
        Assert.ThrowsExactly<ArgumentException>(() => company.SetAddress("Rua Nova, 10", "Lisboa", "28001"));
        company.SetAddress(null, null, null);
        Assert.IsNull(company.AddressDetail);
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
