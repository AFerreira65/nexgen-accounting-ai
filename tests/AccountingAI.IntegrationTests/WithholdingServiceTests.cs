using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class WithholdingServiceTests
{
    [TestMethod]
    public async Task ReportsWithholdingsByAccountMonthAndBeneficiary()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Withholding_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Retenções", "gabinete-retencoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Retenções, Lda.", "RF-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Retenções", "rf@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("1201", "Depósito à ordem"), ("2211", "Fornecedores"), ("2421", "Retenções — trabalho dependente"),
                ("2422", "Retenções — trabalho independente"), ("56", "Resultados transitados"), ("6221", "Trabalhos especializados"), ("6321", "Remunerações"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var lawyer = Party.Create(companyId, "PT", "509999999", "Advogada, Unipessoal");
            var designer = Party.Create(companyId, "PT", "508888888", "Designer Independente");
            context.AddRange(lawyer, designer);

            var number = 0;
            void Entry(DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit, Party? Party)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                entry.SetSource(source);
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, line.Party?.Id);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // Abertura: 30 de retenções de dezembro por entregar.
            Entry(new DateOnly(2026, 1, 1), EntrySource.Opening, ("56", 30m, 0m, null), ("2422", 0m, 30m, null));
            // Janeiro: entrega das retenções de dezembro; honorários da advogada (1 000, retenção de 250).
            Entry(new DateOnly(2026, 1, 20), EntrySource.Manual, ("2422", 30m, 0m, null), ("1201", 0m, 30m, null));
            Entry(new DateOnly(2026, 1, 25), EntrySource.Manual, ("6221", 1000m, 0m, null), ("2422", 0m, 250m, null), ("2211", 0m, 750m, lawyer));
            // Fevereiro: entrega; outra fatura da advogada e uma do designer com o terceiro na linha da retenção.
            Entry(new DateOnly(2026, 2, 20), EntrySource.Manual, ("2422", 250m, 0m, null), ("1201", 0m, 250m, null));
            Entry(new DateOnly(2026, 2, 26), EntrySource.Manual, ("6221", 400m, 0m, null), ("2422", 0m, 100m, null), ("2211", 0m, 300m, lawyer));
            Entry(new DateOnly(2026, 2, 27), EntrySource.Manual, ("6221", 200m, 0m, null), ("2422", 0m, 23m, designer), ("2211", 0m, 177m, designer));
            // Salários: a retenção não tem terceiro.
            Entry(new DateOnly(2026, 2, 28), EntrySource.Manual, ("6321", 2000m, 0m, null), ("2421", 0m, 300m, null), ("1201", 0m, 1700m, null));
            // Anulado e de outro exercício: de fora.
            var voided = new JournalEntry(Guid.NewGuid(), companyId, journalId, "L-X", new DateOnly(2026, 3, 1), "L-X", "Anulado");
            voided.AddLine(Guid.NewGuid(), ids["6221"], "Linha", 999m, 0m);
            voided.AddLine(Guid.NewGuid(), ids["2422"], "Linha", 0m, 999m);
            voided.Complete(JournalEntryStatus.Reviewed);
            voided.Void();
            context.Add(voided);
            Entry(new DateOnly(2027, 1, 5), EntrySource.Manual, ("6221", 100m, 0m, null), ("2422", 0m, 25m, null), ("2211", 0m, 75m, lawyer));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new WithholdingService(context);
            var report = await service.BuildAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(report);
            CollectionAssert.AreEqual(new[] { "2421", "2422" }, report!.Accounts.Select(row => row.AccountCode).ToArray());
            var independent = report.Accounts[1];
            Assert.AreEqual(30m, independent.Opening);
            Assert.AreEqual(373m, independent.Withheld);
            Assert.AreEqual(280m, independent.Delivered);
            Assert.AreEqual(123m, independent.Balance, "As retenções de fevereiro, por entregar.");
            Assert.AreEqual(673m, report.TotalWithheld);
            Assert.AreEqual(280m, report.TotalDelivered);
            Assert.AreEqual(423m, report.TotalBalance);

            var january = report.Months.Single(row => row.Month == 1);
            Assert.AreEqual((250m, 30m), (january.Withheld, january.Delivered));
            Assert.AreEqual(123m, report.Months.Single(row => row.Month == 2 && row.AccountCode == "2422").Withheld);
            Assert.AreEqual(300m, report.Months.Single(row => row.Month == 2 && row.AccountCode == "2421").Withheld);

            // A advogada herda-se do lançamento; o designer vem da própria linha.
            var lawyerRow = report.Beneficiaries.Single(row => row.TaxNumber == "509999999");
            Assert.AreEqual(350m, lawyerRow.Withheld);
            Assert.AreEqual(1400m, lawyerRow.IncomeEstimate);
            Assert.AreEqual(2, lawyerRow.Entries);
            var designerRow = report.Beneficiaries.Single(row => row.TaxNumber == "508888888");
            Assert.AreEqual((23m, 200m), (designerRow.Withheld, designerRow.IncomeEstimate));
            var unnamed = report.Beneficiaries.Last();
            Assert.AreEqual(string.Empty, unnamed.TaxNumber);
            Assert.AreEqual(300m, unnamed.Withheld);
            Assert.AreEqual(300m, report.WithheldWithoutParty);

            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
