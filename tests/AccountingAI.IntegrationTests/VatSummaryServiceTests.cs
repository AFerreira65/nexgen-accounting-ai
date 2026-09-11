using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatSummaryServiceTests
{
    [TestMethod]
    public async Task GroupsThePeriodMovementByNatureRateAndRegionSplitIntoBaseAndTax()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_VatSummary_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Resumo IVA", "gabinete-resumo-iva"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Resumo IVA, Lda.", "RI-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Resumo IVA", "ri@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var cash = Guid.NewGuid();
            var sales = Guid.NewGuid();
            var outputVat = Guid.NewGuid();
            var expense = Guid.NewGuid();
            var inputVat = Guid.NewGuid();
            var unclassifiedExpense = Guid.NewGuid();
            var noMovementSales = Guid.NewGuid();
            context.Add(new LedgerAccount(cash, companyId, "1201", "Depósito à ordem"));
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(outputVat, companyId, "24331131", "IVA liquidado tx normal"));
            context.Add(new LedgerAccount(expense, companyId, "6221", "Serviços especializados"));
            context.Add(new LedgerAccount(inputVat, companyId, "24321131", "IVA dedutível tx normal"));
            context.Add(new LedgerAccount(unclassifiedExpense, companyId, "6222", "Trabalhos especializados"));
            context.Add(new LedgerAccount(noMovementSales, companyId, "7112", "Prestações de serviços"));

            context.Add(AccountVatClassification.Create(
                companyId, sales, VatRate.Normal, VatOperationNature.TransmissoesInternas, VatRegion.Continente));
            context.Add(AccountVatClassification.Create(
                companyId, outputVat, VatRate.Normal, VatOperationNature.TransmissoesInternas, VatRegion.Continente));
            context.Add(AccountVatClassification.Create(
                companyId, expense, VatRate.Normal, VatOperationNature.IvaDedutivelOutros, VatRegion.Continente));
            context.Add(AccountVatClassification.Create(
                companyId, inputVat, VatRate.Normal, VatOperationNature.IvaDedutivelOutros, VatRegion.Continente));
            // Classificada mas sem movimento no período — não deve aparecer no resumo.
            context.Add(AccountVatClassification.Create(
                companyId, noMovementSales, VatRate.Reduzida, VatOperationNature.PrestacoesServicosInternas, VatRegion.Acores));

            // Venda: 253 = 220 (base) + 33 (IVA).
            context.Add(Entry(companyId, journalId, "L-1", new DateOnly(2026, 2, 10), "FT 1",
                (cash, 253m, 0m), (sales, 0m, 220m), (outputVat, 0m, 33m)));

            // Compra: 110,70 = 90 (base) + 20,70 (IVA), conta de gasto sem classificação.
            context.Add(Entry(companyId, journalId, "D-1", new DateOnly(2026, 2, 12), "FC 1",
                (expense, 90m, 0m), (inputVat, 20.7m, 0m), (cash, 0m, 110.7m)));
            context.Add(Entry(companyId, journalId, "D-2", new DateOnly(2026, 2, 13), "FC 2",
                (unclassifiedExpense, 15m, 0m), (cash, 0m, 15m)));

            // Fora do período — não deve entrar no resumo do 1.º trimestre.
            context.Add(Entry(companyId, journalId, "L-2", new DateOnly(2026, 4, 1), "FT 2",
                (cash, 61m, 0m), (sales, 0m, 50m), (outputVat, 0m, 11m)));

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VatSummaryService(context);

            var q1 = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(q1);
            Assert.AreEqual(4, q1!.Rows.Count, "Vendas + IVA liquidado + gasto + IVA dedutível; sem a conta não classificada nem a sem movimento.");

            var salesRow = q1.Rows.Single(row => row.Kind == "base" && row.Nature == "TransmissoesInternas");
            Assert.AreEqual("Normal", salesRow.Rate);
            Assert.AreEqual("Continente", salesRow.Region);
            Assert.AreEqual(0m, salesRow.Debit);
            Assert.AreEqual(220m, salesRow.Credit);
            Assert.AreEqual(-220m, salesRow.Net);
            Assert.AreEqual(1, salesRow.AccountCount);

            var outputVatRow = q1.Rows.Single(row => row.Kind == "imposto" && row.Nature == "TransmissoesInternas");
            Assert.AreEqual(33m, outputVatRow.Credit);
            Assert.AreEqual(-33m, outputVatRow.Net);

            var expenseRow = q1.Rows.Single(row => row.Kind == "base" && row.Nature == "IvaDedutivelOutros");
            Assert.AreEqual(90m, expenseRow.Debit);
            Assert.AreEqual(90m, expenseRow.Net);

            var inputVatRow = q1.Rows.Single(row => row.Kind == "imposto" && row.Nature == "IvaDedutivelOutros");
            Assert.AreEqual(20.7m, inputVatRow.Debit);
            Assert.AreEqual(20.7m, inputVatRow.Net);

            // A conta de gasto não classificada, com movimento no período, aparece em Unclassified.
            Assert.AreEqual(1, q1.Unclassified.Count);
            var unclassifiedRow = q1.Unclassified.Single();
            Assert.AreEqual("6222", unclassifiedRow.AccountCode);
            Assert.AreEqual(15m, unclassifiedRow.Debit);
            Assert.AreEqual(0m, unclassifiedRow.Credit);
            Assert.AreEqual(15m, unclassifiedRow.Net);

            // Sem acesso → null.
            Assert.IsNull(await service
                .BuildAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), CancellationToken.None)
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
