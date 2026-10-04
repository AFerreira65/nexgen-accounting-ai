using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatSettlementEntryServiceTests
{
    [TestMethod]
    public async Task SettlesTheVatAccountsPeriodAfterPeriod()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_VatSettlementEntry_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Apuramento", "gabinete-apuramento-iva"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Apuramento, Lda.", "AI-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Apuramento", "ai@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("1201", "Depósito à ordem"), ("2111", "Clientes"), ("311", "Compras"), ("7111", "Vendas"),
                ("24321", "IVA dedutível"), ("24331", "IVA liquidado"), ("24341", "IVA — regularizações"),
                ("2435", "IVA — apuramento"), ("2436", "IVA a pagar"), ("2437", "IVA a recuperar"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var number = 0;
            void Entry(DateOnly date, params (string Account, decimal Debit, decimal Credit)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // 1.º trimestre: liquidado 230, dedutível 92, regularização a favor do sujeito passivo 10 → a pagar 128.
            Entry(new DateOnly(2026, 2, 10), ("1201", 1230m, 0m), ("7111", 0m, 1000m), ("24331", 0m, 230m));
            Entry(new DateOnly(2026, 2, 12), ("311", 400m, 0m), ("24321", 92m, 0m), ("1201", 0m, 492m));
            Entry(new DateOnly(2026, 3, 5), ("24341", 10m, 0m), ("2111", 0m, 10m));
            // 2.º trimestre: só dedutível 300 → a recuperar 300. 3.º trimestre: liquidado 100.
            Entry(new DateOnly(2026, 5, 10), ("311", 1304.35m, 0m), ("24321", 300m, 0m), ("1201", 0m, 1604.35m));
            Entry(new DateOnly(2026, 8, 10), ("1201", 534.78m, 0m), ("7111", 0m, 434.78m), ("24331", 0m, 100m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VatSettlementEntryService(context, new JournalPostingService(context));
            var accounts = new VatSettlementAccounts();
            (DateOnly From, DateOnly To) Quarter(int quarter) =>
                (new DateOnly(2026, (quarter * 3) - 2, 1), new DateOnly(2026, quarter * 3, DateTime.DaysInMonth(2026, quarter * 3)));

            var (from, to) = Quarter(1);
            var preview = await service.PreviewAsync(userId, companyId, from, to, accounts, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(preview);
            Assert.IsNull(preview!.Blocker);
            Assert.AreEqual("03T", preview.Period);
            Assert.AreEqual(128m, preview.Payable);
            Assert.AreEqual(0m, preview.Recoverable);
            Assert.AreEqual(preview.Lines.Sum(line => line.Debit), preview.Lines.Sum(line => line.Credit));
            Assert.AreEqual(92m, preview.Lines.Single(line => line.AccountCode == "24321").Credit);
            Assert.AreEqual(230m, preview.Lines.Single(line => line.AccountCode == "24331").Debit);
            Assert.AreEqual(10m, preview.Lines.Single(line => line.AccountCode == "24341").Credit);
            Assert.AreEqual(128m, preview.Lines.Single(line => line.AccountCode == "2436").Credit);
            Assert.AreEqual(0m, preview.Lines.Where(line => line.AccountCode == "2435").Sum(line => line.Debit - line.Credit), "A conta de apuramento fica saldada.");

            var posted = await service.PostAsync(userId, companyId, from, to, accounts, "GERAL", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(to, posted!.EntryDate);
            Assert.AreEqual("Apuramento IVA 2026 03T", posted.DocumentReference);
            Assert.AreEqual("Por conferir", posted.Status);

            // Não se apura duas vezes, e o apuramento contabilístico do período continua a mostrar os valores.
            var again = await service.PreviewAsync(userId, companyId, from, to, accounts, CancellationToken.None).ConfigureAwait(false);
            StringAssert.Contains(again!.Blocker, posted.Reference);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.PostAsync(userId, companyId, from, to, accounts, "GERAL", CancellationToken.None)).ConfigureAwait(false);
            var settlement = await new VatReturnService(context).BuildAsync(userId, companyId, from, to, null, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(230m, settlement!.OutputVat, "O lançamento de apuramento não apaga o IVA liquidado do período.");
            Assert.AreEqual(128m, settlement.NetPayable);
            var declaration = await new VatDeclarationService(context, new VatSummaryService(context), new VatReturnService(context))
                .BuildAsync(userId, companyId, from, to, null, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(128m, declaration!.AccountingPayable);

            // 2.º trimestre: crédito de 300 para a conta 2437.
            (from, to) = Quarter(2);
            var second = await service.PreviewAsync(userId, companyId, from, to, accounts, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(300m, second!.Recoverable);
            Assert.AreEqual(300m, second.Lines.Single(line => line.AccountCode == "2437").Debit);
            await service.PostAsync(userId, companyId, from, to, accounts, "GERAL", CancellationToken.None).ConfigureAwait(false);

            // 3.º trimestre: o crédito anterior entra no apuramento e sobram 200 a recuperar.
            (from, to) = Quarter(3);
            var third = await service.PreviewAsync(userId, companyId, from, to, accounts, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(300m, third!.PriorCredit);
            Assert.AreEqual(200m, third.Recoverable);
            Assert.AreEqual(preview.Lines.Sum(line => line.Debit), preview.Lines.Sum(line => line.Credit));
            await service.PostAsync(userId, companyId, from, to, accounts, "GERAL", CancellationToken.None).ConfigureAwait(false);
            var recoverable = await context.JournalEntryLines
                .Where(line => line.CompanyId == companyId && line.AccountId == ids["2437"])
                .SumAsync(line => line.Debit - line.Credit).ConfigureAwait(false);
            Assert.AreEqual(200m, recoverable);

            // Sem movimento, período irregular e contas em falta: não gera.
            (from, to) = Quarter(4);
            StringAssert.Contains((await service.PreviewAsync(userId, companyId, from, to, accounts, CancellationToken.None).ConfigureAwait(false))!.Blocker, "Não há IVA por apurar");
            var missing = await service.PreviewAsync(
                userId, companyId, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), accounts with { Payable = "24361" }, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(missing!.Blocker);

            Assert.IsNull(await service.PreviewAsync(Guid.NewGuid(), companyId, from, to, accounts, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
