using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatReturnServiceTests
{
    [TestMethod]
    public async Task ComputesTheSettlementWithRegularisationsAndTheCarriedForwardCredit()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Vat_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete IVA", "gabinete-iva"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa IVA, Lda.", "IVA-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador IVA", "iva@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var deductible = Guid.NewGuid();
            var output = Guid.NewGuid();
            var regularisation = Guid.NewGuid();
            var carryForward = Guid.NewGuid();
            var settlement = Guid.NewGuid();
            context.Add(new LedgerAccount(deductible, companyId, "2432", "IVA dedutível"));
            context.Add(new LedgerAccount(output, companyId, "2433", "IVA liquidado"));
            context.Add(new LedgerAccount(regularisation, companyId, "2434", "IVA regularizações"));
            context.Add(new LedgerAccount(carryForward, companyId, "2437", "IVA a recuperar"));
            context.Add(new LedgerAccount(settlement, companyId, "2711", "Outros devedores e credores"));

            // Exercício anterior (2025): fica um excesso a reportar de 50 em 2437.
            context.Add(Entry(companyId, journalId, "P-1", new DateOnly(2025, 12, 20), "AP 2025",
                (carryForward, 50m, 0m), (settlement, 0m, 50m)));

            // 1.º trimestre de 2026.
            context.Add(Entry(companyId, journalId, "L-1", new DateOnly(2026, 2, 10), "FT 1",
                (settlement, 230m, 0m), (output, 0m, 230m)));
            context.Add(Entry(companyId, journalId, "D-1", new DateOnly(2026, 2, 12), "FC 1",
                (deductible, 100m, 0m), (settlement, 0m, 100m)));
            context.Add(Entry(companyId, journalId, "R-1", new DateOnly(2026, 3, 5), "REG 1",
                (settlement, 20m, 0m), (regularisation, 0m, 20m)));

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VatReturnService(context);

            var q1 = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(q1);
            Assert.AreEqual(230m, q1!.OutputVat);
            Assert.AreEqual(100m, q1.InputVat);
            Assert.AreEqual(20m, q1.RegularisationsForState);
            Assert.AreEqual(0m, q1.RegularisationsForTaxpayer);
            Assert.AreEqual(50m, q1.PriorPeriodCredit, "Saldo devedor de 2437 antes de 2026-01-01.");
            Assert.AreEqual(150m, q1.PeriodResult, "(230 + 20) − (100 + 0).");
            Assert.AreEqual(100m, q1.NetPayable, "150 − 50 de excesso anterior.");
            Assert.AreEqual(0m, q1.CreditToCarryForward);
            Assert.IsTrue(q1.PriorCreditAssumed);
            CollectionAssert.AreEqual(
                new[] { "2432", "2433", "2434" },
                q1.Lines.Select(line => line.AccountCode).ToArray());
            Assert.AreEqual("liquidado", q1.Lines.Single(line => line.AccountCode == "2433").Role);

            // Um período só com IVA dedutível → excesso a reportar, nada a entregar.
            var deductibleOnly = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 2, 12), new DateOnly(2026, 2, 12), null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(0m, deductibleOnly!.NetPayable);
            Assert.AreEqual(150m, deductibleOnly.CreditToCarryForward, "0 − 100 − 50 de excesso anterior.");

            // Sem acesso → null.
            Assert.IsNull(await service
                .BuildAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, CancellationToken.None)
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
