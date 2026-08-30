using AccountingAI.Domain.Accounting;

namespace AccountingAI.Domain.Tests;

[TestClass]
public sealed class AccountingEntryTests
{
    [TestMethod]
    public void CompletedEntryMustBeBalanced()
    {
        var entry = CreateEntry();
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Base", 100m, 0m);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Contrapartida", 0m, 99m);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            entry.Complete(JournalEntryStatus.PendingReview));
    }

    [TestMethod]
    public void BalancedEntryStoresTotalAndReviewStatus()
    {
        var entry = CreateEntry();
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Base", 100m, 0m);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "IVA", 23m, 0m);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Fornecedor", 0m, 123m);

        entry.Complete(JournalEntryStatus.Reviewed);

        Assert.AreEqual(123m, entry.TotalAmount);
        Assert.AreEqual(JournalEntryStatus.Reviewed, entry.Status);
        Assert.HasCount(3, entry.Lines);
    }

    [TestMethod]
    public void EntryLineRejectsSimultaneousDebitAndCredit()
    {
        var entry = CreateEntry();

        Assert.ThrowsExactly<ArgumentException>(() =>
            entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Linha inválida", 10m, 10m));
    }

    [TestMethod]
    public void CompletedEntryRejectsAdditionalLines()
    {
        var entry = CreateEntry();
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Débito", 25m, 0m);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Crédito", 0m, 25m);
        entry.Complete(JournalEntryStatus.PendingReview);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Linha tardia", 1m, 0m));
    }

    [TestMethod]
    public void PostingAccountAcceptsAValidTaxonomy()
    {
        var account = new LedgerAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "12",
            "Depósitos à ordem",
            allowsPosting: true);

        account.AssignTaxonomy("s", 2);

        Assert.AreEqual("S", account.TaxonomyReference);
        Assert.AreEqual(2, account.TaxonomyCode);
    }

    [TestMethod]
    public void AggregateAccountRejectsATaxonomy()
    {
        var account = new LedgerAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1",
            "Meios financeiros líquidos",
            allowsPosting: false);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            account.AssignTaxonomy("S", 1));
    }

    [TestMethod]
    public void RenameTrimsTheNameAndKeepsTheCode()
    {
        var account = new LedgerAccount(Guid.NewGuid(), Guid.NewGuid(), "6221", "Material de escritório");

        account.Rename("  Material de escritório e economato  ");

        Assert.AreEqual("Material de escritório e economato", account.Name);
        Assert.AreEqual("6221", account.Code);
        Assert.ThrowsExactly<ArgumentException>(() => account.Rename("   "));
    }

    [TestMethod]
    public void DeactivateAndReactivateToggleTheState()
    {
        var account = new LedgerAccount(Guid.NewGuid(), Guid.NewGuid(), "6221", "Material de escritório");
        Assert.IsTrue(account.IsActive);

        account.Deactivate();
        Assert.IsFalse(account.IsActive);

        account.Reactivate();
        Assert.IsTrue(account.IsActive);
    }

    [TestMethod]
    public void ClearingTheTaxonomyRemovesReferenceAndCode()
    {
        var account = new LedgerAccount(Guid.NewGuid(), Guid.NewGuid(), "12", "Depósitos à ordem");
        account.AssignTaxonomy("S", 2);

        account.AssignTaxonomy(null, null);

        Assert.IsNull(account.TaxonomyReference);
        Assert.IsNull(account.TaxonomyCode);
    }

    private static JournalEntry CreateEntry() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "TEST-2026-00001",
        new DateOnly(2026, 8, 30),
        "Documento sintético",
        "Movimento sintético");
}
