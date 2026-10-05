using NexGenAccountingAI.Application.Banking;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class FirmDashboardServiceTests
{
    private sealed class FakeReader(BankStatementFile file) : IBankStatementReader
    {
        public Task<BankStatementFile> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken = default) => Task.FromResult(file);
    }

    [TestMethod]
    public async Task SummarisesEveryCompanyOfTheUserByWhatNeedsAttention()
    {
        var connectionString = TestDatabase.ConnectionString("FirmDashboard");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Painel", "gabinete-painel"));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Painel", "painel@test.invalid"));
            var busy = Guid.NewGuid();
            var quiet = Guid.NewGuid();
            var other = Guid.NewGuid();
            context.Add(new Company(busy, firmId, "Empresa Ocupada, Lda.", "PN-PT-0001", TaxRegion.Mainland));
            context.Add(new Company(quiet, firmId, "Empresa Calma, Lda.", "PN-PT-0002", TaxRegion.Mainland));
            context.Add(new Company(other, firmId, "Empresa de Outro, Lda.", "PN-PT-0003", TaxRegion.Mainland));
            context.Add(new CompanyUser(busy, userId, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(quiet, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, busy, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("12011", "Banco"), ("6311", "Remunerações"), ("7211", "Vendas") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], busy, code, name));
            }

            JournalEntry Entry(string reference, DateOnly date, string debit, string credit, decimal amount, JournalEntryStatus status, EntrySource source)
            {
                var entry = new JournalEntry(Guid.NewGuid(), busy, journalId, reference, date, reference, "Lançamento");
                entry.SetSource(source);
                entry.AddLine(Guid.NewGuid(), ids[debit], "Débito", amount, 0m);
                entry.AddLine(Guid.NewGuid(), ids[credit], "Crédito", 0m, amount);
                entry.Complete(status);
                return entry;
            }

            context.Add(Entry("S-0", new DateOnly(2025, 6, 1), "6311", "12011", 900m, JournalEntryStatus.Reviewed, EntrySource.Manual));
            context.Add(Entry("S-Z", new DateOnly(2025, 12, 15), "6311", "12011", 900m, JournalEntryStatus.Reviewed, EntrySource.Manual));
            context.Add(Entry("S-1", new DateOnly(2026, 3, 31), "6311", "12011", 900m, JournalEntryStatus.Reviewed, EntrySource.Manual));
            context.Add(Entry("V-1", new DateOnly(2026, 4, 15), "12011", "7211", 100m, JournalEntryStatus.PendingReview, EntrySource.EFatura));
            context.Add(EFaturaPurchase.Create(busy, Guid.NewGuid(), "500584095", "Fornecedor", "FT 9", null, new DateOnly(2026, 4, 20), "Fatura", null, null, 10m, 2.30m, 12.30m, "x.csv"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            await new BankStatementService(new FakeReader(new BankStatementFile("CSV", null, null, null, null,
                [new BankStatementRow(1, new DateOnly(2026, 4, 30), null, "COMISSAO", -5m, null, null, null)], [])), context)
                .ImportAsync(userId, busy, "12011", Stream.Null, "abril.csv", false, CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new FirmDashboardService(context, new ObligationCalendarService(context), new SuggestedEntryService(context, new JournalPostingService(context)));
            var dashboard = await service.BuildAsync(userId, new DateOnly(2026, 5, 10), CancellationToken.None).ConfigureAwait(false);

            CollectionAssert.AreEqual(new[] { "Empresa Ocupada, Lda.", "Empresa Calma, Lda." }, dashboard.Companies.Select(item => item.LegalName).ToArray(),
                "Só as empresas do utilizador, a que mais precisa de atenção primeiro.");
            var first = dashboard.Companies[0];
            Assert.AreEqual(1, first.PendingReview);
            Assert.AreEqual(1, first.PurchasesToPost);
            Assert.AreEqual(1, first.UnreconciledBankLines);
            Assert.AreEqual(4, first.EntriesLast12Months);
            Assert.AreEqual(0.25m, first.AutomaticShare);
            Assert.AreEqual(0.5m, first.AutomaticShareLast3Months, "Março a maio: um manual e um do e-Fatura.");
            Assert.AreEqual(0m, first.AutomaticSharePrevious3Months, "Dezembro a fevereiro: só o manual de dezembro.");
            Assert.IsNull(dashboard.Companies.Single(item => item.LegalName == "Empresa Calma, Lda.").AutomaticShareLast3Months);

            // A evolução: doze meses, de junho de 2025 a maio de 2026, pela origem.
            var automation = dashboard.Automation!;
            Assert.AreEqual(12, automation.Count);
            Assert.AreEqual(new DateOnly(2025, 6, 1), automation[0].Month);
            Assert.AreEqual(1, automation[0].Manual, "O 1 de junho entra no primeiro mês.");
            var april = automation.Single(month => month.Month == new DateOnly(2026, 4, 1));
            Assert.AreEqual(1, april.EFatura);
            Assert.AreEqual(1m, april.AutomaticShare);
            Assert.IsNull(automation.Single(month => month.Month == new DateOnly(2026, 1, 1)).AutomaticShare, "Mês sem lançamentos.");
            var second = dashboard.Companies[1];
            Assert.IsTrue(first.OverdueObligations > second.OverdueObligations, "A declaração mensal de remunerações de março está em atraso só na primeira.");
            Assert.AreEqual(0, second.PendingReview);
            Assert.IsNotNull(first.NextObligation);
            Assert.IsTrue(first.NextDueDate >= new DateOnly(2026, 5, 10));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
