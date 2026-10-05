using System.Linq;
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

    [TestMethod]
    public void IndexedSuggestionsMatchTheOriginalScan()
    {
        // O algoritmo de antes do índice (percorria o catálogo para cada conta).
        static int? Scan(string reference, string code)
        {
            var entries = TaxonomyCatalog.For(reference);
            var exact = entries.Where(entry => entry.SncAccounts.Contains(code, StringComparer.Ordinal)).ToList();
            if (exact.Count > 0)
            {
                return exact.Count == 1 ? exact[0].Code : null;
            }

            var byPrefix = entries
                .Where(entry => entry.SncAccounts.Any(snc => snc.Length == 2 && code.Length > 2 && code.StartsWith(snc, StringComparison.Ordinal)))
                .ToList();
            return byPrefix.Count == 1 ? byPrefix[0].Code : null;
        }

        foreach (var reference in new[] { "S", "M", "N" })
        {
            var codes = TaxonomyCatalog.For(reference).SelectMany(entry => entry.SncAccounts)
                .SelectMany(snc => new[] { snc, snc + "1", snc + "99", snc + "0001" })
                .Concat(Enumerable.Range(10, 80).Select(ledger => $"{ledger}{ledger}"))
                .Distinct()
                .ToList();
            Assert.IsGreaterThan(1000, codes.Count);
            foreach (var code in codes)
            {
                Assert.AreEqual(Scan(reference, code), TaxonomyCatalog.SuggestFor(reference, code), $"{reference} {code}");
            }
        }
    }
}
