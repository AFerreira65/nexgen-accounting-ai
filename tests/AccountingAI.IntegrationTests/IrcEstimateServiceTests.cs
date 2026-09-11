using AccountingAI.Application.Irc;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class IrcEstimateServiceTests
{
    [TestMethod]
    public async Task ComputesTheEstimateFromNetIncomePlusAdjustmentsTimesTheRate()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Irc_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete IRC", "gabinete-irc"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa IRC, Lda.", "IRC-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador IRC", "irc@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var cash = Guid.NewGuid();
            var sales = Guid.NewGuid();
            var expense = Guid.NewGuid();
            context.Add(new LedgerAccount(cash, companyId, "1201", "Depósito à ordem"));
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(expense, companyId, "6221", "Serviços especializados"));

            // Rendimento 10 000, gasto 4 000 → resultado líquido 6 000.
            context.Add(Entry(companyId, journalId, "L-1", new DateOnly(2026, 2, 10), "FT 1",
                (cash, 10000m, 0m), (sales, 0m, 10000m)));
            context.Add(Entry(companyId, journalId, "D-1", new DateOnly(2026, 2, 12), "FC 1",
                (expense, 4000m, 0m), (cash, 0m, 4000m)));

            // Fora do período — não deve entrar.
            context.Add(Entry(companyId, journalId, "L-2", new DateOnly(2026, 4, 1), "FT 2",
                (cash, 500m, 0m), (sales, 0m, 500m)));

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var ledger = new AccountLedgerService(context);
            var service = new IrcEstimateService(context, ledger);

            var adjustments = new List<IrcAdjustment> { new("Tributação autónoma", 1000m), new("Benefício fiscal", -500m) };
            var q1 = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 0.21m, adjustments, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(q1);
            Assert.AreEqual(6000m, q1!.NetIncome);
            Assert.AreEqual(500m, q1.AdjustmentsTotal, "1000 − 500.");
            Assert.AreEqual(6500m, q1.TaxableResult, "6000 + 500.");
            Assert.AreEqual(0.21m, q1.TaxRate);
            Assert.AreEqual(1365m, q1.EstimatedTax, "6500 × 0.21.");
            Assert.AreEqual(2, q1.Adjustments.Count);

            // Resultado fiscal negativo → coleta 0, sem prejuízo a reportar.
            var lossAdjustments = new List<IrcAdjustment> { new("Correção", -20000m) };
            var loss = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 0.21m, lossAdjustments, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(-14000m, loss!.TaxableResult, "6000 − 20000.");
            Assert.AreEqual(0m, loss.EstimatedTax);

            // Sem correções.
            var noAdjustments = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 0.21m, [], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(6000m, noAdjustments!.TaxableResult);
            Assert.AreEqual(1260m, noAdjustments.EstimatedTax, "6000 × 0.21.");

            // Sem acesso → null.
            Assert.IsNull(await service
                .BuildAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 0.21m, [], CancellationToken.None)
                .ConfigureAwait(false));
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
