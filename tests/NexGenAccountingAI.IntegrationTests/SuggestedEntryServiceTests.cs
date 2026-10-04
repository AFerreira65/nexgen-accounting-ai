using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class SuggestedEntryServiceTests
{
    private const string SupplierWithRule = "500584095";
    private const string SupplierWithoutRule = "503504564";

    [TestMethod]
    public async Task SuggestsAndFinalizesEntriesFromEFaturaPurchases()
    {
        var connectionString = TestDatabase.ConnectionString("Suggested");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Sugestões", "gabinete-sugestoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Sugestões, Lda.", "SG-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Sugestões", "sugestoes@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "COMPRAS", "Compras"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("6221", "Trabalhos especializados"), ("24323", "IVA dedutível OBS"),
                ("22111", "Fornecedores c/c"), ("2211001", "Fornecedor A"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            context.Add(VatCode.Create(companyId, "C-OBS-NOR", "Compras OBS normal", VatRate.Normal, 23m, VatOperationNature.IvaDedutivelOutros, VatRegion.Continente));
            var supplier = Party.Create(companyId, "PT", SupplierWithRule, "Fornecedor A, Lda.");
            supplier.SetRoles(isCustomer: false, isSupplier: true);
            supplier.LinkAccounts(null, ids["2211001"]);
            context.Add(supplier);

            var batch = Guid.NewGuid();
            EFaturaPurchase Purchase(string taxId, string number, int day, string type, decimal taxBase, decimal tax, string? status = null) =>
                EFaturaPurchase.Create(companyId, batch, taxId, taxId == SupplierWithRule ? "Fornecedor A, Lda." : "Fornecedor B, S.A.",
                    number, null, new DateOnly(2026, 3, day), type, status, null, taxBase, tax, taxBase + tax, "compras.csv");
            var invoice = Purchase(SupplierWithRule, "FT A/1", 5, "Fatura", 100m, 23m);
            var creditNote = Purchase(SupplierWithRule, "NC A/2", 10, "Nota de crédito", 10m, 2.30m);
            var noRule = Purchase(SupplierWithoutRule, "FT B/9", 12, "Fatura", 50m, 11.50m);
            var alreadyBooked = Purchase(SupplierWithRule, "FT A/3", 15, "Fatura", 40m, 9.20m);
            var cancelled = Purchase(SupplierWithRule, "FT A/4", 20, "Fatura", 30m, 6.90m, "Anulada");
            context.AddRange(invoice, creditNote, noRule, alreadyBooked, cancelled);

            // A fatura FT A/3 já foi lançada à mão: o cruzamento encontra-a pelo número.
            var manual = new JournalEntry(Guid.NewGuid(), companyId, journalId, "M-1", new DateOnly(2026, 3, 16), "FT A/3", "Compra lançada à mão");
            manual.AddLine(Guid.NewGuid(), ids["6221"], "Serviço", 40m, 0m);
            manual.AddLine(Guid.NewGuid(), ids["24323"], "IVA", 9.20m, 0m);
            manual.AddLine(Guid.NewGuid(), ids["22111"], "Fornecedor", 0m, 49.20m);
            manual.Complete(JournalEntryStatus.Reviewed);
            context.Add(manual);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            await new EntryTemplateService(context).SaveAsync(userId, companyId, "COMPRA-SRV", new EntryTemplateInput(
                "Compra de serviços", "COMPRAS", null, null, true,
                [
                    new EntryTemplateLineInput(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, "6221", "C-OBS-NOR", null),
                    new EntryTemplateLineInput(EntryTemplateLineKind.Vat, EntryTemplateSide.Debit, "24323", "C-OBS-NOR", "IVA dedutível"),
                    new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, "22111", null, null),
                ]), CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new SuggestedEntryService(context, new JournalPostingService(context));
            var rule = await service.SaveRuleAsync(userId, companyId, " 500 584 095 ", new SupplierEntryRuleInput("compra-srv", null, true), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(SupplierWithRule, rule!.SupplierTaxId);
            Assert.AreEqual("Fornecedor A, Lda.", rule.SupplierName);
            Assert.AreEqual("COMPRA-SRV", rule.EntryTemplateCode);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveRuleAsync(userId, companyId, SupplierWithoutRule, new SupplierEntryRuleInput("NAO-HA", null, true), CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var from = new DateOnly(2026, 3, 1);
            var to = new DateOnly(2026, 3, 31);
            var report = await service.ListAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(report);
            Assert.AreEqual(5, report!.PurchaseCount);
            Assert.AreEqual(1, report.BookedCount, "A FT A/3 já tem lançamento.");
            Assert.AreEqual(1, report.CancelledCount, "A FT A/4 está anulada no Portal.");
            CollectionAssert.AreEqual(new[] { "FT A/1", "NC A/2", "FT B/9" }, report.Items.Select(item => item.InvoiceNo).ToArray());
            Assert.AreEqual(2, report.ReadyCount);
            Assert.AreEqual(1, report.WithoutRuleCount);

            var first = report.Items[0];
            Assert.AreEqual("COMPRAS", first.JournalCode);
            Assert.AreEqual("Fatura FT A/1 — Fornecedor A, Lda.", first.Description);
            CollectionAssert.AreEqual(new[] { "6221", "24323", "2211001" }, first.Lines.Select(line => line.AccountCode).ToArray(),
                "A contrapartida vai para a conta própria do fornecedor.");
            Assert.AreEqual(100m, first.Lines[0].Debit);
            Assert.AreEqual(23m, first.Lines[1].Debit);
            Assert.AreEqual(123m, first.Lines[2].Credit);
            Assert.AreEqual(SupplierWithRule, first.Lines[2].PartyTaxNumber);
            Assert.IsNull(first.Lines[0].PartyTaxNumber);

            var credit = report.Items[1];
            Assert.IsTrue(credit.IsCreditNote);
            Assert.AreEqual(10m, credit.Lines[0].Credit, "A nota de crédito inverte os lados.");
            Assert.AreEqual(2.30m, credit.Lines[1].Credit);
            Assert.AreEqual(12.30m, credit.Lines[2].Debit);

            Assert.IsFalse(report.Items[2].Ready);
            Assert.AreEqual("O fornecedor não tem regra de lançamento.", report.Items[2].Problem);

            // Finalizar cria os lançamentos por conferir, com origem e-Fatura, e liga-os às faturas.
            var outcomes = await service.FinalizeAsync(userId, companyId, [invoice.Id, creditNote.Id, noRule.Id, alreadyBooked.Id], CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(outcomes);
            Assert.IsNotNull(outcomes!.Single(item => item.PurchaseId == invoice.Id).Reference);
            Assert.IsNotNull(outcomes.Single(item => item.PurchaseId == creditNote.Id).Reference);
            Assert.AreEqual("O fornecedor não tem regra de lançamento.", outcomes.Single(item => item.PurchaseId == noRule.Id).Error);
            Assert.AreEqual("A fatura já tem lançamento ou está anulada.", outcomes.Single(item => item.PurchaseId == alreadyBooked.Id).Error);
            context.ChangeTracker.Clear();

            var reference = outcomes.Single(item => item.PurchaseId == invoice.Id).Reference!;
            var posted = await context.JournalEntries.AsNoTracking().Include(entry => entry.Lines)
                .SingleAsync(entry => entry.CompanyId == companyId && entry.Reference == reference).ConfigureAwait(false);
            Assert.AreEqual(EntrySource.EFatura, posted.Source);
            Assert.AreEqual(JournalEntryStatus.PendingReview, posted.Status);
            Assert.AreEqual("FT A/1", posted.DocumentReference);
            Assert.AreEqual(new DateOnly(2026, 3, 5), posted.DocumentDate);
            Assert.AreEqual(supplier.Id, posted.Lines.Single(line => line.AccountId == ids["2211001"]).PartyId);
            Assert.AreEqual(reference, (await context.EFaturaPurchases.AsNoTracking().SingleAsync(item => item.Id == invoice.Id).ConfigureAwait(false)).JournalEntryReference);

            var after = await service.ListAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(3, after!.BookedCount);
            CollectionAssert.AreEqual(new[] { "FT B/9" }, after.Items.Select(item => item.InvoiceNo).ToArray());

            // Anular o lançamento devolve a fatura às sugestões.
            await new JournalPostingService(context).VoidAsync(companyId, reference, CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var afterVoid = await service.ListAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "FT A/1", "FT B/9" }, afterVoid!.Items.Select(item => item.InvoiceNo).ToArray());

            // Regras: lista, apagar; sem acesso.
            Assert.AreEqual(1, (await service.ListRulesAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false))!.Count);
            Assert.IsTrue(await service.DeleteRuleAsync(userId, companyId, SupplierWithRule, CancellationToken.None).ConfigureAwait(false));
            Assert.IsFalse(await service.DeleteRuleAsync(userId, companyId, SupplierWithRule, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListRulesAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.FinalizeAsync(Guid.NewGuid(), companyId, [invoice.Id], CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
