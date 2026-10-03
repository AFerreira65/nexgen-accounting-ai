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
    public void DocumentDateIsOptionalAndIndependentFromTheEntryDate()
    {
        var withDate = new JournalEntry(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "GERAL-2026-00001", new DateOnly(2026, 3, 10),
            "FT 1/1", "Compra", documentDate: new DateOnly(2026, 2, 27));

        Assert.AreEqual(new DateOnly(2026, 2, 27), withDate.DocumentDate);
        Assert.IsNull(CreateEntry().DocumentDate);
    }

    [TestMethod]
    public void LineCanCarryAPartyButNotAnEmptyOne()
    {
        var entry = CreateEntry();
        var partyId = Guid.NewGuid();
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Cliente", 100m, 0m, partyId);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Venda", 0m, 100m);

        Assert.AreEqual(partyId, entry.Lines.First().PartyId);
        Assert.IsNull(entry.Lines.Last().PartyId);
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateEntry().AddLine(Guid.NewGuid(), Guid.NewGuid(), "Cliente", 100m, 0m, Guid.Empty));

        var vatCodeId = Guid.NewGuid();
        var withVat = CreateEntry();
        withVat.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Venda", 0m, 100m, vatCodeId: vatCodeId);
        Assert.AreEqual(vatCodeId, withVat.Lines.Single().VatCodeId);
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateEntry().AddLine(Guid.NewGuid(), Guid.NewGuid(), "Venda", 0m, 100m, vatCodeId: Guid.Empty));
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
        Assert.AreEqual(EntrySource.Manual, entry.Source);

        entry.SetSource(EntrySource.SaftImport);
        Assert.AreEqual(EntrySource.SaftImport, entry.Source);
    }

    [TestMethod]
    public void EntryLineRejectsSimultaneousDebitAndCredit()
    {
        var entry = CreateEntry();

        Assert.ThrowsExactly<ArgumentException>(() =>
            entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Linha inválida", 10m, 10m));
    }

    [TestMethod]
    public void PendingEntryCanBeReviewedAndVoided()
    {
        var entry = CreateEntry();
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Débito", 40m, 0m);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Crédito", 0m, 40m);
        entry.Complete(JournalEntryStatus.PendingReview);

        entry.MarkReviewed();
        Assert.AreEqual(JournalEntryStatus.Reviewed, entry.Status);
        Assert.ThrowsExactly<InvalidOperationException>(() => entry.MarkReviewed());

        entry.Reopen();
        Assert.AreEqual(JournalEntryStatus.PendingReview, entry.Status);
        Assert.ThrowsExactly<InvalidOperationException>(() => entry.Reopen());

        entry.Void();
        Assert.AreEqual(JournalEntryStatus.Voided, entry.Status);
        Assert.ThrowsExactly<InvalidOperationException>(() => entry.Reopen());
    }

    [TestMethod]
    public void CompleteRejectsTheVoidedStatus()
    {
        var entry = CreateEntry();
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "D", 10m, 0m);
        entry.AddLine(Guid.NewGuid(), Guid.NewGuid(), "C", 0m, 10m);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => entry.Complete(JournalEntryStatus.Voided));
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

    [TestMethod]
    public void JournalRenameAndActivationToggles()
    {
        var journal = new AccountingJournal(Guid.NewGuid(), Guid.NewGuid(), "vendas", "Diário de vendas");
        Assert.AreEqual("VENDAS", journal.Code);

        journal.Rename("  Diário de vendas e serviços ");
        Assert.AreEqual("Diário de vendas e serviços", journal.Name);
        Assert.ThrowsExactly<ArgumentException>(() => journal.Rename(" "));

        journal.Deactivate();
        Assert.IsFalse(journal.IsActive);
        journal.Reactivate();
        Assert.IsTrue(journal.IsActive);
    }

    [TestMethod]
    public void DocumentTypeRenameAndChangeDefaultJournal()
    {
        var journalId = Guid.NewGuid();
        var documentType = new AccountingDocumentType(
            Guid.NewGuid(), Guid.NewGuid(), journalId, "ftv", "Fatura a cliente");
        Assert.AreEqual("FTV", documentType.Code);
        Assert.AreEqual(journalId, documentType.DefaultJournalId);

        var newJournalId = Guid.NewGuid();
        documentType.ChangeDefaultJournal(newJournalId);
        Assert.AreEqual(newJournalId, documentType.DefaultJournalId);
        Assert.ThrowsExactly<ArgumentException>(() => documentType.ChangeDefaultJournal(Guid.Empty));

        documentType.Deactivate();
        Assert.IsFalse(documentType.IsActive);
        documentType.Reactivate();
        Assert.IsTrue(documentType.IsActive);
    }

    [TestMethod]
    public void DocumentNumberSeriesHandsOutConsecutiveNumbers()
    {
        var series = new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "ftv", 2026);
        Assert.AreEqual("FTV", series.Series);
        Assert.AreEqual(1, series.NextNumber);
        Assert.AreEqual(1, series.Take());
        Assert.AreEqual(2, series.Take());
        Assert.AreEqual(3, series.NextNumber);

        var resumed = new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "OD", 2026, nextNumber: 843);
        Assert.AreEqual(843, resumed.Take());

        Assert.ThrowsExactly<ArgumentException>(() =>
            new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), " ", 2026));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "OD", 1999));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "OD", 2026, nextNumber: 0));
    }

    [TestMethod]
    public void DocumentNumberSeriesCanOnlyAdvance()
    {
        var series = new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "FTV", 2026, nextNumber: 3);

        series.AdvanceTo(10);

        Assert.AreEqual(10, series.NextNumber);
        series.AdvanceTo(10);
        Assert.ThrowsExactly<InvalidOperationException>(() => series.AdvanceTo(9));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), new string('X', 25), 2026));
    }

    [TestMethod]
    public void DocumentNumberSeriesFormatsWithConfigurableWidth()
    {
        var series = new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "ftv", 2026);
        Assert.AreEqual(5, series.Width);
        Assert.AreEqual("FTV-2026-00007", series.Format(7));

        series.ChangeWidth(3);
        Assert.AreEqual("FTV-2026-007", series.Format(7));
        Assert.AreEqual("FTV-2026-1234", series.Format(1234)); // não trunca acima da largura

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => series.ChangeWidth(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => series.ChangeWidth(13));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new DocumentNumberSeries(Guid.NewGuid(), Guid.NewGuid(), "OD", 2026, width: 20));
    }

    [TestMethod]
    public void EntryNotesAreTrimmedSetClearedAndLengthLimited()
    {
        var entry = CreateEntry();
        Assert.IsNull(entry.Notes);

        entry.SetNotes("  Reconciliar com o extrato bancário  ");
        Assert.AreEqual("Reconciliar com o extrato bancário", entry.Notes);

        entry.SetNotes("   ");
        Assert.IsNull(entry.Notes);

        Assert.ThrowsExactly<ArgumentException>(() => entry.SetNotes(new string('x', JournalEntry.NotesMaxLength + 1)));

        var withNotes = new JournalEntry(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TEST-2026-00002",
            new DateOnly(2026, 8, 30), "Doc", "Mov", notes: "  Nota inicial  ");
        Assert.AreEqual("Nota inicial", withNotes.Notes);
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
