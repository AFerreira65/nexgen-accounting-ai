using NexGenAccountingAI.Application.Fiscal;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Banking;
using NexGenAccountingAI.Domain.Billing;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>O papel de trabalho de fecho de um mês (F4F-02).</summary>
[TestClass]
public sealed class ClosingChecklistServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [TestMethod]
    public async Task ShowsWhatIsLeftAndWhenTheMonthIsReadyToClose()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("Closing"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Fecho", "gabinete-fecho"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Fecho, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista Fecho", "fecho@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name, taxonomy) in new[] { ("1201", "Banco", 2), ("7211", "Serviços", 507) })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", taxonomy));
            }

            JournalEntry Entry(string reference, JournalEntryStatus status)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 9, 10), reference, $"Lançamento {reference}");
                entry.AddLine(Guid.NewGuid(), ids["1201"], "D", 100m, 0m);
                entry.AddLine(Guid.NewGuid(), ids["7211"], "C", 0m, 100m);
                entry.Complete(status);
                context.Add(entry);
                return entry;
            }

            Entry("L-1", JournalEntryStatus.PendingReview);
            Entry("L-2", JournalEntryStatus.Reviewed);
            var batch = Guid.NewGuid();
            var purchase = EFaturaPurchase.Create(companyId, batch, "500000009", "Fornecedor Z", "FT 1", null, new DateOnly(2026, 9, 3), "FT", "Registado", null, 100m, 23m, 123m, "efatura.csv");
            context.Add(purchase);
            context.Add(SalesDocument.Create(companyId, batch, "FT 2026/1", null, "FT", new DateOnly(2026, 9, 5), false, "500000001", "Cliente A", "PT", 100m, 23m, 123m, [], "saft.xml"));
            var import = new BankStatementImport(Guid.NewGuid(), companyId, ids["1201"], "extrato.csv", "csv", null, null, null, null, null, null, 1);
            context.Add(import);
            context.Add(BankStatementLine.Create(companyId, ids["1201"], import.Id, "k1", new DateOnly(2026, 9, 12), null, "Recebimento", 100m, null, null));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var clock = new FixedClock(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
            var statements = new FinancialStatementsService(context);
            var calendar = new ObligationCalendarService(context);
            var service = new ClosingChecklistService(context, new AnomalyService(context), calendar, statements, clock);
            var none = CancellationToken.None;
            ClosingItem Item(ClosingChecklist list, string key) => list.Items.Single(item => item.Key == key);

            var first = await service.BuildAsync(userId, companyId, 2026, 9, none).ConfigureAwait(false);
            Assert.IsNotNull(first);
            Assert.AreEqual("2026-09", first!.PeriodKey);
            Assert.AreEqual(1, Item(first, "lancamentos-por-conferir").Count);
            Assert.AreEqual("attention", Item(first, "lancamentos-por-conferir").State);
            Assert.AreEqual("ok", Item(first, "lancamentos-descabidos").State);
            Assert.AreEqual(1, Item(first, "compras-por-lancar").Count);
            Assert.AreEqual(1, Item(first, "vendas-por-contabilizar").Count);
            Assert.AreEqual(1, Item(first, "bancos-por-conciliar").Count);
            Assert.AreEqual("attention", Item(first, "iva-do-periodo").State, "O IVA mensal de setembro só se entrega em outubro.");
            Assert.IsFalse(first.Ready);
            Assert.AreEqual(ClosingChecklistService.ManualItems.Count, first.PendingManual);
            Assert.IsTrue(first.Attention >= 5);

            // Tratar do que faltava: rever o lançamento, lançar a compra e a venda, conciliar o banco e entregar o IVA.
            var pending = await context.JournalEntries.Include(entry => entry.Lines).SingleAsync(entry => entry.Reference == "L-1").ConfigureAwait(false);
            pending.Complete(JournalEntryStatus.Reviewed);
            var tracked = await context.EFaturaPurchases.SingleAsync().ConfigureAwait(false);
            tracked.LinkToEntry("L-2");
            var sale = await context.SalesDocuments.SingleAsync().ConfigureAwait(false);
            sale.LinkToEntry("L-2");
            var line = await context.BankStatementLines.SingleAsync().ConfigureAwait(false);
            var reconciliation = new BankReconciliation(Guid.NewGuid(), companyId, ids["1201"], ["L-2"], automatic: false);
            context.Add(reconciliation);
            await context.SaveChangesAsync().ConfigureAwait(false);
            line.AttachTo(reconciliation.Id);
            var delivered = TaxObligationRecord.Create(companyId, ObligationCatalog.VatReturnMonthly, "2026-09");
            delivered.Update(TaxObligationState.Entregue, new DateOnly(2026, 10, 5), "Comprovativo 123", null);
            context.Add(delivered);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var second = await service.BuildAsync(userId, companyId, 2026, 9, none).ConfigureAwait(false);
            Assert.AreEqual(0, second!.Attention, string.Join(" | ", second.Items.Where(item => item.State == "attention").Select(item => item.Label)));
            Assert.AreEqual("ok", Item(second, "iva-do-periodo").State);
            Assert.IsFalse(second.Ready, "Faltam as conferências manuais.");

            // As manuais: marcar regista quem e quando; desmarcar volta a pendente.
            foreach (var manual in ClosingChecklistService.ManualItems)
            {
                Assert.IsTrue(await service.SetManualAsync(userId, companyId, 2026, 9, manual.Key, true, manual.Key == "bancos-conferidos" ? "Bate à cêntimo" : null, none).ConfigureAwait(false));
            }

            var ready = await service.BuildAsync(userId, companyId, 2026, 9, none).ConfigureAwait(false);
            Assert.IsTrue(ready!.Ready);
            Assert.AreEqual(0, ready.PendingManual);
            var banks = Item(ready, "bancos-conferidos");
            Assert.AreEqual("done", banks.State);
            Assert.AreEqual("Bate à cêntimo", banks.Note);
            Assert.AreEqual("Contabilista Fecho", banks.CheckedByName);
            Assert.AreEqual(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), banks.CheckedAtUtc);
            Assert.IsTrue(await service.SetManualAsync(userId, companyId, 2026, 9, "depreciacoes", false, null, none).ConfigureAwait(false));
            var reopened = await service.BuildAsync(userId, companyId, 2026, 9, none).ConfigureAwait(false);
            Assert.AreEqual("pending", Item(reopened!, "depreciacoes").State);
            Assert.IsNull(Item(reopened!, "depreciacoes").CheckedByName);
            Assert.IsFalse(reopened!.Ready);

            // Outro mês não herda as marcas; acesso e validação.
            var other = await service.BuildAsync(userId, companyId, 2026, 8, none).ConfigureAwait(false);
            Assert.AreEqual(ClosingChecklistService.ManualItems.Count, other!.PendingManual);
            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, 2026, 9, none).ConfigureAwait(false));
            Assert.IsFalse(await service.SetManualAsync(Guid.NewGuid(), companyId, 2026, 9, "depreciacoes", true, null, none).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SetManualAsync(userId, companyId, 2026, 9, "inventada", true, null, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, 2026, 13, none)).ConfigureAwait(false);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
