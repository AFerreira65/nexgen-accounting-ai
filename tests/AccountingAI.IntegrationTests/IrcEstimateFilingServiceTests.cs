using AccountingAI.Application.Irc;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class IrcEstimateFilingServiceTests
{
    [TestMethod]
    public async Task SavesTheFilingAndRecomputesAgainstTheCurrentLedgerOnRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_IrcFiling_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete IRC Filing", "gabinete-irc-filing"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa IRC Filing, Lda.", "IRCF-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador IRC Filing", "ircf@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            var cash = Guid.NewGuid();
            var sales = Guid.NewGuid();
            context.Add(new LedgerAccount(cash, companyId, "1201", "Depósito à ordem"));
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));

            // 1.º trimestre de 2026: rendimento 10 000 → resultado líquido 10 000.
            context.Add(Entry(companyId, journalId, "L-1", new DateOnly(2026, 2, 10), "FT 1",
                (cash, 10000m, 0m), (sales, 0m, 10000m)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var ledger = new AccountLedgerService(context);
            var calculator = new IrcEstimateService(context, ledger);
            var service = new IrcEstimateFilingService(context, calculator);
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 3, 31);

            // Antes de guardar: acesso confirmado, nada guardado.
            var before = await service.GetAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(before);
            Assert.IsFalse(before!.Saved);
            Assert.IsNull(before.Estimate);

            // Guardar com uma correção de 1000 e taxa de 21%.
            var adjustments = new List<IrcAdjustment> { new("Tributação autónoma", 1000m) };
            var saved = await service.SaveAsync(userId, companyId, from, to, 0.21m, adjustments, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(saved);
            Assert.IsTrue(saved!.Saved);
            Assert.AreEqual(10000m, saved.Estimate!.NetIncome);
            Assert.AreEqual(11000m, saved.Estimate.TaxableResult, "10000 + 1000.");
            Assert.AreEqual(2310m, saved.Estimate.EstimatedTax, "11000 × 0.21.");
            Assert.AreEqual(1, await context.IrcEstimateFilings.CountAsync().ConfigureAwait(false));

            // Reler: agora guardado, com a taxa e a correção indicadas.
            var reloaded = await service.GetAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(reloaded!.Saved);
            Assert.AreEqual(0.21m, reloaded.TaxRate);
            Assert.AreEqual(1, reloaded.Adjustments.Count);
            Assert.AreEqual(2310m, reloaded.Estimate!.EstimatedTax);

            // Um novo lançamento no período muda o resultado líquido — a releitura reflete-o
            // sem perder a taxa/correção guardadas.
            context.Add(Entry(companyId, journalId, "L-2", new DateOnly(2026, 3, 1), "FT 2",
                (cash, 2000m, 0m), (sales, 0m, 2000m)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var afterNewEntry = await service.GetAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(12000m, afterNewEntry!.Estimate!.NetIncome, "10000 + 2000.");
            Assert.AreEqual(13000m, afterNewEntry.Estimate.TaxableResult);
            Assert.AreEqual(0.21m, afterNewEntry.TaxRate, "A taxa guardada mantém-se.");

            // Guardar de novo com outra taxa — atualiza a mesma linha (uma por período).
            var updated = await service.SaveAsync(userId, companyId, from, to, 0.17m, adjustments, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0.17m, updated!.TaxRate);
            Assert.AreEqual(1, await context.IrcEstimateFilings.CountAsync().ConfigureAwait(false), "Atualiza, não duplica.");

            // Histórico.
            var history = await service.ListAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(history);
            Assert.AreEqual(1, history!.Count);
            Assert.AreEqual(0.17m, history[0].TaxRate);

            // Sem acesso.
            Assert.IsNull(await service.GetAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
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
