using NexGenAccountingAI.Infrastructure.Persistence;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class TaxonomyCatalogTests
{
    [TestMethod]
    public void LoadsBothReferentialsFromThePortariaTranscription()
    {
        var s = TaxonomyCatalog.For("S");
        var m = TaxonomyCatalog.For("M");

        Assert.IsTrue(s.Count > 600, $"Taxonomia S deve ter mais de 600 códigos, tem {s.Count}.");
        Assert.IsTrue(m.Count > 300, $"Taxonomia M deve ter mais de 300 códigos, tem {m.Count}.");
        Assert.AreEqual("Caixa", s[0].Description);
        Assert.AreEqual(1, s[0].Code);
        CollectionAssert.Contains(s[0].SncAccounts.ToArray(), "11");
        StringAssert.Contains(TaxonomyCatalog.Source, "302/2016");
    }

    [TestMethod]
    public void ReferentialNReusesTaxonomyS()
    {
        CollectionAssert.AreEqual(
            TaxonomyCatalog.For("S").Select(item => item.Code).ToArray(),
            TaxonomyCatalog.For("N").Select(item => item.Code).ToArray());
    }

    [TestMethod]
    public void ReferentialOAlwaysUsesCodeOne()
    {
        var o = TaxonomyCatalog.For("O");

        Assert.HasCount(1, o);
        Assert.AreEqual(1, o[0].Code);
        Assert.AreEqual(1, TaxonomyCatalog.SuggestFor("O", "271"));
    }

    [TestMethod]
    public void SuggestForOnlyReturnsUnambiguousMatches()
    {
        Assert.AreEqual(1, TaxonomyCatalog.SuggestFor("S", "11"));
        Assert.AreEqual(2, TaxonomyCatalog.SuggestFor("S", "12"));
        Assert.AreEqual(2, TaxonomyCatalog.SuggestFor("M", "12"));

        // "219" is shared by several Taxonomia S entries (Perdas por imparidade …) → ambiguous.
        Assert.IsNull(TaxonomyCatalog.SuggestFor("S", "219"));
        // A synthetic detail account that is not in the annex.
        Assert.IsNull(TaxonomyCatalog.SuggestFor("S", "2111001"));
    }
}
