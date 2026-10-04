using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class TreasuryForecastServiceTests
{
    [TestMethod]
    public async Task ForecastsCashFromOpenDocumentsAndFixedCosts()
    {
        var connectionString = TestDatabase.ConnectionString("Treasury");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Tesouraria", "gabinete-tesouraria"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Tesouraria, Lda.", "TS-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Tesouraria", "tesouraria@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("12011", "Banco"), ("2111", "Clientes"), ("2211", "Fornecedores"), ("6221", "Serviços"), ("6311", "Remunerações"), ("7211", "Vendas"), ("56", "Resultados transitados") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var customerA = Party.Create(companyId, "PT", "500584095", "Cliente A");
            var customerB = Party.Create(companyId, "PT", "503504564", "Cliente B");
            var supplier = Party.Create(companyId, "PT", "509999990", "Fornecedor C");
            context.AddRange(customerA, customerB, supplier);

            var number = 0;
            void Entry(DateOnly date, string debit, string credit, decimal amount, Party? party = null, EntrySource source = EntrySource.Manual)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, $"T-{++number}", date, $"D-{number}", "Lançamento");
                entry.SetSource(source);
                entry.AddLine(Guid.NewGuid(), ids[debit], "Débito", amount, 0m, partyId: debit is "2111" or "2211" ? party?.Id : null);
                entry.AddLine(Guid.NewGuid(), ids[credit], "Crédito", 0m, amount, partyId: credit is "2111" or "2211" ? party?.Id : null);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry(new DateOnly(2025, 8, 1), "2111", "7211", 500m, customerB);                       // duvidoso: mais de 180 dias
            Entry(new DateOnly(2026, 1, 1), "12011", "56", 5000m, source: EntrySource.Opening);
            Entry(new DateOnly(2026, 1, 31), "6311", "12011", 900m);
            Entry(new DateOnly(2026, 2, 28), "6311", "12011", 900m);
            Entry(new DateOnly(2026, 3, 15), "2111", "7211", 1230m, customerA);
            Entry(new DateOnly(2026, 3, 20), "6221", "2211", 615m, supplier);
            Entry(new DateOnly(2026, 3, 30), "12011", "2111", 1800m, customerA);   // um recebimento antigo do cliente A
            Entry(new DateOnly(2026, 2, 1), "2111", "7211", 1800m, customerA);     // a fatura que esse recebimento pagou
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new TreasuryForecastService(context);
            var date = new DateOnly(2026, 3, 31);
            var forecast = await service.BuildAsync(userId, companyId, date, 3, 30, 30, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(forecast);
            Assert.AreEqual(5000m - 900m - 900m + 1800m, forecast.OpeningCash);
            Assert.IsTrue(forecast.FixedFromHistory);
            Assert.AreEqual(600m, forecast.FixedMonthly, "Pessoal de dezembro (0), janeiro e fevereiro, a dividir por três.");
            Assert.AreEqual(500m, forecast.DoubtfulReceivables);
            Assert.AreEqual(3, forecast.Months.Count);
            Assert.AreEqual(forecast.OpeningCash, forecast.Months[0].Closing, "Em março não falta nada: o resto do mês é zero.");
            Assert.AreEqual(1230m, forecast.Months[1].Receipts, "A fatura de 15 de março mais 30 dias cai em abril; a de fevereiro está paga.");
            Assert.AreEqual(615m, forecast.Months[1].Payments);
            Assert.AreEqual(forecast.OpeningCash + 1230m - 615m - 600m, forecast.Months[1].Closing);
            Assert.AreEqual(forecast.Months[1].Closing - 600m, forecast.Months[2].Closing);
            Assert.AreEqual("Cliente A", forecast.TopReceipts.Single().PartyName);

            // Prazo zero e gastos fixos indicados: o que já venceu entra no primeiro mês.
            var immediate = await service.BuildAsync(userId, companyId, date, 1, 0, 0, 100m, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(1230m, immediate!.Months[0].Receipts);
            Assert.IsFalse(immediate.FixedFromHistory);
            Assert.AreEqual(100m, immediate.FixedMonthly);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, date, 0, 30, 30, null, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, date, 3, -1, 30, null, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, date, 3, 30, 30, null, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
