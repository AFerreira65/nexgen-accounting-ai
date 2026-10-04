using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class PartyAgingServiceTests
{
    [TestMethod]
    public async Task AgesOpenBalancesOldestFirst()
    {
        var connectionString = TestDatabase.ConnectionString("Aging");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Idades", "gabinete-idades"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Idades, Lda.", "IS-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Idades", "is@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("1201", "Banco"), ("2111", "Clientes"), ("2211", "Fornecedores"), ("311", "Compras"), ("7111", "Vendas") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var alfa = Party.Create(companyId, "PT", "509999999", "Alfa, Lda.");
            var beta = Party.Create(companyId, "PT", "508888888", "Beta, S.A.");
            var gama = Party.Create(companyId, "PT", "507777777", "Gama Fornecimentos");
            context.AddRange(alfa, beta, gama);

            var number = 0;
            void Entry(DateOnly date, DateOnly? documentDate, params (string Account, decimal Debit, decimal Credit, Party? Party)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(
                    Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}", documentDate: documentDate);
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, line.Party?.Id);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // Alfa: faturas de 100 (janeiro), 200 (maio) e 300 (setembro); pagou 150 — abate à mais antiga e a parte da segunda.
            Entry(new DateOnly(2026, 1, 10), null, ("2111", 100m, 0m, alfa), ("7111", 0m, 100m, null));
            Entry(new DateOnly(2026, 5, 20), null, ("2111", 200m, 0m, alfa), ("7111", 0m, 200m, null));
            Entry(new DateOnly(2026, 9, 15), null, ("2111", 300m, 0m, alfa), ("7111", 0m, 300m, null));
            Entry(new DateOnly(2026, 6, 1), null, ("1201", 150m, 0m, null), ("2111", 0m, 150m, alfa));
            // Beta: adiantou 80 sem fatura.
            Entry(new DateOnly(2026, 9, 1), null, ("1201", 80m, 0m, null), ("2111", 0m, 80m, beta));
            // Gama (fornecedor): fatura de 500 lançada em setembro com data de documento de julho; outra depois da data pedida.
            Entry(new DateOnly(2026, 9, 5), new DateOnly(2026, 7, 5), ("311", 500m, 0m, null), ("2211", 0m, 500m, gama));
            Entry(new DateOnly(2026, 10, 5), null, ("311", 70m, 0m, null), ("2211", 0m, 70m, gama));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new PartyAgingService(context);
            var date = new DateOnly(2026, 9, 30);
            var customers = await service.BuildAsync(userId, companyId, date, PartyAgingService.Customers, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(customers);
            Assert.AreEqual(5, customers!.BucketLabels.Count);
            Assert.AreEqual(2, customers.Rows.Count);
            var alfaRow = customers.Rows[0];
            Assert.AreEqual("Alfa, Lda.", alfaRow.Name);
            Assert.AreEqual(450m, alfaRow.Open);
            // 300 com 15 dias; os 150 que sobram da fatura de maio têm 133 dias.
            CollectionAssert.AreEqual(new[] { 300m, 0m, 0m, 150m, 0m }, alfaRow.Buckets.ToArray());
            Assert.AreEqual(new DateOnly(2026, 5, 20), alfaRow.OldestDate);
            Assert.AreEqual(0m, alfaRow.Advance);
            var betaRow = customers.Rows[1];
            Assert.AreEqual(0m, betaRow.Open);
            Assert.AreEqual(80m, betaRow.Advance);
            Assert.AreEqual(450m, customers.TotalOpen);
            Assert.AreEqual(80m, customers.TotalAdvance);

            var suppliers = await service.BuildAsync(userId, companyId, date, PartyAgingService.Suppliers, CancellationToken.None).ConfigureAwait(false);
            var gamaRow = suppliers!.Rows.Single();
            Assert.AreEqual(500m, gamaRow.Open, "A fatura de outubro é posterior à data pedida.");
            CollectionAssert.AreEqual(new[] { 0m, 0m, 500m, 0m, 0m }, gamaRow.Buckets.ToArray(), "Conta a data do documento (5 de julho: 87 dias), não a do lançamento.");

            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.BuildAsync(userId, companyId, date, "outros", CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, date, PartyAgingService.Customers, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
