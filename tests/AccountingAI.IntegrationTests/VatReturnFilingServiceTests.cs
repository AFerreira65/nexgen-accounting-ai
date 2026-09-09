using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatReturnFilingServiceTests
{
    [TestMethod]
    public async Task SavesTheFilingAndChainsTheCarriedForwardCreditToTheNextPeriod()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_VatFiling_{Guid.NewGuid():N}";
        var configured = Environment.GetEnvironmentVariable("ACCOUNTINGAI_TEST_SQL_CONNECTION");
        var builder = string.IsNullOrWhiteSpace(configured)
            ? new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            }
            : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = databaseName;

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure"))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Filing", "gabinete-filing"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Filing, Lda.", "FIL-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Filing", "filing@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            var deductible = Guid.NewGuid();
            var output = Guid.NewGuid();
            var settlement = Guid.NewGuid();
            context.Add(new LedgerAccount(deductible, companyId, "2432", "IVA dedutível"));
            context.Add(new LedgerAccount(output, companyId, "2433", "IVA liquidado"));
            context.Add(new LedgerAccount(settlement, companyId, "2711", "Outros devedores e credores"));

            // 1.º trimestre de 2026: liquidado 100, dedutível 200 → resultado −100.
            context.Add(Entry(companyId, journalId, "L-1", new DateOnly(2026, 2, 10), "FT 1",
                (settlement, 100m, 0m), (output, 0m, 100m)));
            context.Add(Entry(companyId, journalId, "D-1", new DateOnly(2026, 2, 12), "FC 1",
                (deductible, 200m, 0m), (settlement, 0m, 200m)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VatReturnFilingService(context, new VatReturnService(context));
            var q1From = new DateOnly(2026, 1, 1);
            var q1To = new DateOnly(2026, 3, 31);
            var q2From = new DateOnly(2026, 4, 1);
            var q2To = new DateOnly(2026, 6, 30);

            // Antes de guardar: não guardado, excesso anterior assumido (0), não vem de um apuramento anterior.
            var before = await service.GetAsync(userId, companyId, q1From, q1To, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(before);
            Assert.IsFalse(before!.Saved);
            Assert.IsFalse(before.PriorFromPreviousFiling);
            Assert.IsTrue(before.Return.PriorCreditAssumed);

            // Guardar com um excesso anterior explícito de 30.
            var saved = await service.SaveAsync(userId, companyId, q1From, q1To, 30m, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(saved);
            Assert.IsTrue(saved!.Saved);
            Assert.AreEqual(30m, saved.Return.PriorPeriodCredit);
            Assert.IsFalse(saved.Return.PriorCreditAssumed);
            Assert.AreEqual(130m, saved.Return.CreditToCarryForward, "−100 − 30.");
            Assert.AreEqual(1, await context.VatReturnFilings.CountAsync().ConfigureAwait(false));

            // Reler o 1.º trimestre: agora guardado, com o excesso indicado.
            var reloaded = await service.GetAsync(userId, companyId, q1From, q1To, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(reloaded!.Saved);
            Assert.AreEqual(30m, reloaded.PriorPeriodCredit);

            // 2.º trimestre: o excesso anterior sugerido vem do apuramento do 1.º trimestre (130).
            var q2 = await service.GetAsync(userId, companyId, q2From, q2To, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(q2);
            Assert.IsTrue(q2!.PriorFromPreviousFiling);
            Assert.AreEqual(130m, q2.SuggestedPriorCredit);
            Assert.AreEqual(130m, q2.Return.PriorPeriodCredit);
            Assert.IsFalse(q2.Return.PriorCreditAssumed);

            // Histórico.
            var history = await service.ListAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(history);
            Assert.AreEqual(1, history!.Count);
            Assert.AreEqual(130m, history[0].CreditToCarryForward);

            // Sem acesso.
            Assert.IsNull(await service.GetAsync(Guid.NewGuid(), companyId, q1From, q1To, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private static JournalEntry Entry(
        Guid companyId, Guid journalId, string reference, DateOnly date, string document,
        params (Guid AccountId, decimal Debit, decimal Credit)[] lines)
    {
        var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, document, $"Lançamento {reference}");
        foreach (var line in lines)
        {
            entry.AddLine(Guid.NewGuid(), line.AccountId, "Linha", line.Debit, line.Credit);
        }

        entry.Complete(JournalEntryStatus.Reviewed);
        return entry;
    }
}
