using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class ProvisionMapServiceTests
{
    [TestMethod]
    public async Task BuildsModelo30FromTheLedgerAndTheTaxedParts()
    {
        var connectionString = TestDatabase.ConnectionString("Modelo30");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Modelo 30", "gabinete-modelo-30"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Modelo 30, Lda.", "M3-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Modelo 30", "modelo30@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("2111", "Clientes"), ("2191", "Imparidade de clientes"), ("2921", "Provisões para garantias"),
                ("56", "Resultados transitados"), ("6511", "Imparidade em clientes"), ("6712", "Provisões"), ("7621", "Reversões de imparidade"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var number = 0;
            void Entry(DateOnly date, string debit, string credit, decimal amount, EntrySource source = EntrySource.Manual)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, $"P-{++number}", date, $"D-{number}", "Lançamento");
                entry.SetSource(source);
                entry.AddLine(Guid.NewGuid(), ids[debit], "Débito", amount, 0m);
                entry.AddLine(Guid.NewGuid(), ids[credit], "Crédito", 0m, amount);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry(new DateOnly(2026, 1, 1), "56", "2191", 1000m, EntrySource.Opening);
            Entry(new DateOnly(2026, 1, 1), "56", "2921", 500m, EntrySource.Opening);
            Entry(new DateOnly(2026, 3, 31), "6511", "2191", 300m);   // reforço
            Entry(new DateOnly(2026, 6, 30), "2191", "7621", 200m);   // reversão (conta 76)
            Entry(new DateOnly(2026, 9, 30), "2191", "2111", 100m);   // utilização: o crédito incobrável abatido
            Entry(new DateOnly(2026, 10, 31), "6712", "2921", 50m);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new ProvisionMapService(context);
            var map = await service.BuildAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(map);
            var impairment = map.Lines.Single(line => line.AccountCode == "2191");
            Assert.AreEqual(1, impairment.Group);
            Assert.AreEqual(1000m, impairment.Opening, "Coluna 2: o saldo que vem da abertura.");
            Assert.AreEqual(300m, impairment.Constituted, "Coluna 9.");
            Assert.AreEqual(200m, impairment.Reversed);
            Assert.AreEqual(100m, impairment.Used);
            Assert.AreEqual(1000m, impairment.Closing, "Coluna 10 = 1 000 − 100 − 200 + 300.");
            var provision = map.Lines.Single(line => line.AccountCode == "2921");
            Assert.AreEqual(2, provision.Group, "Provisões: grupo II por omissão.");
            Assert.AreEqual(550m, provision.Closing);
            Assert.IsTrue(map.Warnings.Any(warning => warning.Contains("grupo II")));

            Assert.IsTrue(await service.UpdateAsync(userId, companyId, 2026, "2191", new ProvisionMapUpdate(1, 400m, 50m, 0m), CancellationToken.None).ConfigureAwait(false));
            Assert.IsTrue(await service.UpdateAsync(userId, companyId, 2026, "2921", new ProvisionMapUpdate(3, 0m, 0m, 0m), CancellationToken.None).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UpdateAsync(userId, companyId, 2026, "6511", new ProvisionMapUpdate(1, 0m, 0m, 0m), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UpdateAsync(userId, companyId, 2026, "2921", new ProvisionMapUpdate(1, 0m, 0m, 0m), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UpdateAsync(userId, companyId, 2026, "2921", new ProvisionMapUpdate(3, -1m, 0m, 0m), CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var after = await service.BuildAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            var line = after!.Lines.Single(item => item.AccountCode == "2191");
            Assert.AreEqual(400m, line.TaxedOpening);
            Assert.AreEqual(600m, line.AcceptedOpening, "Coluna 4 = (2) − (3).");
            Assert.AreEqual(50m, line.TaxedUsed);
            Assert.AreEqual(50m, line.AcceptedUsed, "Coluna 6: a utilização menos a parte tributada.");
            Assert.AreEqual(200m, line.AcceptedReversed);
            Assert.AreEqual(3, after.Lines.Single(item => item.AccountCode == "2921").Group);
            Assert.AreEqual(550m, after.Total(3));
            Assert.IsFalse(after.Warnings.Any(warning => warning.Contains("grupo II")));

            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
