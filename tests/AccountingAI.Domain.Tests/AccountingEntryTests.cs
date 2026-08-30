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

    private static JournalEntry CreateEntry() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "TEST-2026-00001",
        new DateOnly(2026, 8, 30),
        "Documento sintético",
        "Movimento sintético");
}
