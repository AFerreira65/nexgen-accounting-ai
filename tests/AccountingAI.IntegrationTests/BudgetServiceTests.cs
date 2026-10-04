using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class BudgetServiceTests
{
    [TestMethod]
    public void AnAnnualAmountIsSpreadOverTwelveMonths()
    {
        var months = BudgetLine.SpreadEvenly(100m);

        Assert.AreEqual(12, months.Count);
        Assert.AreEqual(8.33m, months[0]);
        Assert.AreEqual(8.37m, months[11], "O acerto do arredondamento fica em dezembro.");
        Assert.AreEqual(100m, months.Sum());
        Assert.ThrowsExactly<ArgumentException>(() => BudgetLine.NormalizeAccountCode("211"));
        Assert.ThrowsExactly<ArgumentException>(() => BudgetLine.NormalizeAccountCode("6a"));
    }

    [TestMethod]
    public async Task ComparesTheBudgetWithTheActualFigures()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Budget_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Orçamento", "gabinete-orcamento"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Orçamento, Lda.", "OR-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Orçamento", "or@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name, posting) in new[]
            {
                ("1201", "Banco", true), ("62", "Fornecimentos e serviços externos", false), ("6221", "Trabalhos especializados", true),
                ("6231", "Ferramentas", true), ("6311", "Remunerações", true), ("7211", "Serviços", true),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, posting));
            }

            var site = CostCenter.Create(companyId, "OBRA", "Obra");
            context.Add(site);

            var number = 0;
            void Entry(DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit, CostCenter? Center)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                entry.SetSource(source);
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, costCenterId: line.Center?.Id);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry(new DateOnly(2026, 1, 15), EntrySource.Manual, ("6221", 300m, 0m, site), ("1201", 0m, 300m, null));
            Entry(new DateOnly(2026, 2, 15), EntrySource.Manual, ("6231", 100m, 0m, null), ("1201", 0m, 100m, null));
            Entry(new DateOnly(2026, 2, 28), EntrySource.Manual, ("6311", 500m, 0m, null), ("1201", 0m, 500m, null));
            Entry(new DateOnly(2026, 3, 10), EntrySource.Manual, ("1201", 2000m, 0m, null), ("7211", 0m, 2000m, null));
            // Depois de março: não entra no realizado até março.
            Entry(new DateOnly(2026, 4, 10), EntrySource.Manual, ("6221", 50m, 0m, null), ("1201", 0m, 50m, null));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new BudgetService(context);
            Assert.IsTrue(await service.SetAsync(userId, companyId, 2026, "62", null, 1200m, CancellationToken.None).ConfigureAwait(false));
            await service.SetAsync(userId, companyId, 2026, "7211", null, 6000m, CancellationToken.None).ConfigureAwait(false);
            await service.SetAsync(userId, companyId, 2026, "6221", " obra ", 600m, CancellationToken.None).ConfigureAwait(false);

            var report = await service.ReportAsync(userId, companyId, 2026, 3, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(report);
            CollectionAssert.AreEqual(new[] { "7211", "62", "6221" }, report!.Lines.Select(line => line.AccountCode).ToArray());

            var income = report.Lines[0];
            Assert.AreEqual((6000m, 1500m, 2000m, 500m), (income.AnnualBudget, income.Budget, income.Actual, income.Deviation));
            Assert.IsTrue(income.Favourable, "Mais rendimento do que o orçamentado.");
            var supplies = report.Lines[1];
            Assert.AreEqual("Fornecimentos e serviços externos", supplies.AccountName);
            Assert.AreEqual((300m, 400m, 100m), (supplies.Budget, supplies.Actual, supplies.Deviation), "O grupo 62 soma as subcontas 6221 e 6231 até março.");
            Assert.AreEqual(33.3m, supplies.DeviationPercent);
            Assert.IsFalse(supplies.Favourable);
            var siteLine = report.Lines[2];
            Assert.AreEqual("OBRA", siteLine.CostCenterCode);
            Assert.AreEqual((150m, 300m), (siteLine.Budget, siteLine.Actual), "Só as linhas do centro de custo.");

            // Os totais contam cada conta uma vez; o que não tem orçamento aparece à parte.
            Assert.AreEqual(1500m, report.BudgetIncome);
            Assert.AreEqual(300m, report.BudgetExpenses);
            Assert.AreEqual(400m, report.ActualExpenses);
            Assert.AreEqual(1200m, report.BudgetResult);
            Assert.AreEqual(1600m, report.ActualResult);
            Assert.AreEqual(500m, report.UnbudgetedExpenses, "As remunerações não têm orçamento.");
            Assert.AreEqual(0m, report.UnbudgetedIncome);

            // O exercício inteiro inclui abril.
            var full = await service.ReportAsync(userId, companyId, 2026, 12, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual((1200m, 450m), (full!.Lines[1].Budget, full.Lines[1].Actual));

            // Sobreposição, conta inexistente e centro de custo inexistente são recusados; zero retira a linha.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.SetAsync(userId, companyId, 2026, "6221", null, 100m, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.SetAsync(userId, companyId, 2026, "6999", null, 100m, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.SetAsync(userId, companyId, 2026, "6231", "NAO-EXISTE", 100m, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetAsync(userId, companyId, 2026, "1201", null, 100m, CancellationToken.None)).ConfigureAwait(false);
            await service.SetAsync(userId, companyId, 2026, "62", null, 0m, CancellationToken.None).ConfigureAwait(false);
            var without = await service.ReportAsync(userId, companyId, 2026, 3, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(without!.Lines.Any(line => line.AccountCode == "62"));
            Assert.AreEqual(150m, without.BudgetExpenses, "Sem o orçamento geral, a linha do centro de custo passa a contar.");
            Assert.AreEqual(24, await context.BudgetLines.CountAsync(line => line.CompanyId == companyId).ConfigureAwait(false));

            // Valores por mês: a sazonalidade muda o orçamentado até ao mês pedido.
            var seasonal = new[] { 0m, 0m, 900m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 300m };
            await service.SetAsync(userId, companyId, 2026, "62", null, 0m, CancellationToken.None, seasonal).ConfigureAwait(false);
            var february = await service.ReportAsync(userId, companyId, 2026, 2, CancellationToken.None).ConfigureAwait(false);
            var seasonalLine = february!.Lines.Single(line => line.AccountCode == "62");
            Assert.AreEqual((1200m, 0m), (seasonalLine.AnnualBudget, seasonalLine.Budget));
            Assert.IsNull(seasonalLine.DeviationPercent);
            CollectionAssert.AreEqual(seasonal, seasonalLine.MonthlyBudget.ToArray());
            var march = await service.ReportAsync(userId, companyId, 2026, 3, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(900m, march!.Lines.Single(line => line.AccountCode == "62").Budget);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetAsync(userId, companyId, 2026, "62", null, 0m, CancellationToken.None, new[] { 1m, 2m })).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetAsync(userId, companyId, 2026, "62", null, 0m, CancellationToken.None, seasonal.Select(amount => amount - 1m).ToArray())).ConfigureAwait(false);

            // Copiar o orçamento para o exercício seguinte, com mais 10 %: as mesmas linhas, mês a mês.
            Assert.AreEqual(3, await service.CopyAsync(userId, companyId, 2027, BudgetCopySource.Budget, 2026, 10m, CancellationToken.None).ConfigureAwait(false));
            var next = await service.ReportAsync(userId, companyId, 2027, 12, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(6600m, next!.Lines.Single(line => line.AccountCode == "7211").AnnualBudget);
            Assert.AreEqual(990m, next.Lines.Single(line => line.AccountCode == "62").MonthlyBudget[2]);
            var nextSite = next.Lines.Single(line => line.AccountCode == "6221");
            Assert.AreEqual(("OBRA", 660m), (nextSite.CostCenterCode, nextSite.AnnualBudget));

            // Um exercício que já tem orçamento não é copiado por cima.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CopyAsync(userId, companyId, 2027, BudgetCopySource.Actual, 2026, 0m, CancellationToken.None)).ConfigureAwait(false);

            // A partir do realizado: o grupo de dois dígitos quando existe no plano (62), senão a própria conta.
            Assert.AreEqual(3, await service.CopyAsync(userId, companyId, 2028, BudgetCopySource.Actual, 2026, 0m, CancellationToken.None).ConfigureAwait(false));
            var fromActual = await service.ReportAsync(userId, companyId, 2028, 12, CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "7211", "62", "6311" }, fromActual!.Lines.Select(line => line.AccountCode).ToArray());
            CollectionAssert.AreEqual(
                new[] { 300m, 100m, 0m, 50m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m },
                fromActual.Lines[1].MonthlyBudget.ToArray(),
                "O realizado de cada mês de 2026 no grupo 62.");
            Assert.AreEqual(2000m, fromActual.Lines[0].MonthlyBudget[2]);
            Assert.AreEqual(500m, fromActual.Lines[2].AnnualBudget);

            // Origem sem nada, a própria origem e um ajuste impossível são recusados.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CopyAsync(userId, companyId, 2030, BudgetCopySource.Budget, 2029, 0m, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CopyAsync(userId, companyId, 2030, BudgetCopySource.Actual, 2029, 0m, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.CopyAsync(userId, companyId, 2030, BudgetCopySource.Budget, 2030, 0m, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.CopyAsync(userId, companyId, 2030, BudgetCopySource.Budget, 2026, -100m, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.CopyAsync(Guid.NewGuid(), companyId, 2030, BudgetCopySource.Budget, 2026, 0m, CancellationToken.None).ConfigureAwait(false));

            Assert.IsNull(await service.ReportAsync(Guid.NewGuid(), companyId, 2026, 3, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.SetAsync(Guid.NewGuid(), companyId, 2026, "62", null, 1m, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
