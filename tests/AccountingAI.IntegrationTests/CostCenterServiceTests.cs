using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class CostCenterServiceTests
{
    [TestMethod]
    public async Task MaintainsCostCentersPostsWithThemAndBuildsTheAnalyticReport()
    {
        var connectionString = TestDatabase.ConnectionString("CostCenters");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Analítica", "gabinete-analitica"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Analítica, Lda.", "AN-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Analítica", "analitica@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "GERAL", "Diário Geral"));
            foreach (var (code, name) in new[] { ("6221", "Trabalhos especializados"), ("6231", "Ferramentas"), ("7211", "Prestações de serviços"), ("1201", "Banco") })
            {
                context.Add(new LedgerAccount(Guid.NewGuid(), companyId, code, name));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new CostCenterService(context);

            // Cadastro.
            var works = await service.CreateAsync(userId, companyId, " obra.01 ", "Obra da Rua Nova", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("OBRA.01", works!.Code);
            await service.CreateAsync(userId, companyId, "ADM", "Administração", CancellationToken.None).ConfigureAwait(false);
            await service.CreateAsync(userId, companyId, "VELHO", "Centro antigo", CancellationToken.None).ConfigureAwait(false);
            await service.UpdateAsync(userId, companyId, "velho", "Centro antigo", isActive: false, CancellationToken.None).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .CreateAsync(userId, companyId, "ADM", "Duplicado", CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .CreateAsync(userId, companyId, "COM ESPAÇO", "Inválido", CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            CollectionAssert.AreEqual(
                new[] { "ADM", "OBRA.01" },
                (await service.ListAsync(userId, companyId, false, CancellationToken.None).ConfigureAwait(false))!.Select(row => row.Code).ToArray());
            Assert.AreEqual(3, (await service.ListAsync(userId, companyId, true, CancellationToken.None).ConfigureAwait(false))!.Count);

            // Lançamentos com centro de custo.
            var posting = new JournalPostingService(context);
            var date = new DateOnly(2026, 3, 10);
            var posted = await posting.PostAsync(
                companyId, "GERAL", date, "FT 1", "Serviço na obra", reviewed: true,
                [
                    new JournalEntryLineInput("6221", "Subempreitada", 300m, 0m, CostCenter: " obra.01 "),
                    new JournalEntryLineInput("6231", "Ferramentas", 50m, 0m, CostCenter: "ADM"),
                    new JournalEntryLineInput("1201", "Banco", 0m, 350m),
                ]).ConfigureAwait(false);
            Assert.AreEqual("OBRA.01", posted.Lines[0].CostCenter);
            Assert.IsNull(posted.Lines[2].CostCenter);

            await posting.PostAsync(
                companyId, "GERAL", date, "FT 2", "Fatura da obra", reviewed: true,
                [
                    new JournalEntryLineInput("1201", "Banco", 1000m, 0m),
                    new JournalEntryLineInput("7211", "Serviço", 0m, 1000m, CostCenter: "OBRA.01"),
                ]).ConfigureAwait(false);
            await posting.PostAsync(
                companyId, "GERAL", date, "FT 3", "Gasto sem centro", reviewed: true,
                [
                    new JournalEntryLineInput("6231", "Ferramentas", 20m, 0m),
                    new JournalEntryLineInput("1201", "Banco", 0m, 20m),
                ]).ConfigureAwait(false);
            var voided = await posting.PostAsync(
                companyId, "GERAL", date, "FT 4", "Anulado", reviewed: false,
                [
                    new JournalEntryLineInput("6221", "Subempreitada", 999m, 0m, CostCenter: "ADM"),
                    new JournalEntryLineInput("1201", "Banco", 0m, 999m),
                ]).ConfigureAwait(false);
            await posting.VoidAsync(companyId, voided.Reference).ConfigureAwait(false);

            // Centro desconhecido ou inativo recusa o lançamento.
            foreach (var code in new[] { "NAO-HA", "VELHO" })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => posting.PostAsync(
                    companyId, "GERAL", date, "FT 5", "Recusado", reviewed: false,
                    [
                        new JournalEntryLineInput("6221", "Gasto", 10m, 0m, CostCenter: code),
                        new JournalEntryLineInput("1201", "Banco", 0m, 10m),
                    ])).ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();

            // Mapa analítico: por centro, com a linha «sem centro» no fim e sem o anulado.
            var report = await service.ReportAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(report);
            CollectionAssert.AreEqual(new[] { "ADM", "OBRA.01", null }, report!.Centers.Select(center => center.Code).ToArray());

            var site = report.Centers.Single(center => center.Code == "OBRA.01");
            Assert.AreEqual(300m, site.Expenses);
            Assert.AreEqual(1000m, site.Income);
            Assert.AreEqual(700m, site.Result);
            CollectionAssert.AreEqual(new[] { "6221", "7211" }, site.Accounts.Select(account => account.AccountCode).ToArray());

            Assert.AreEqual(50m, report.Centers.Single(center => center.Code == "ADM").Expenses);
            var none = report.Centers.Single(center => center.Code is null);
            Assert.AreEqual("Sem centro de custo", none.Name);
            Assert.AreEqual(20m, none.Expenses);

            Assert.AreEqual(370m, report.TotalExpenses);
            Assert.AreEqual(1000m, report.TotalIncome);
            Assert.AreEqual(630m, report.TotalResult);

            // Fora do período não há nada; sem acesso, nada.
            var empty = await service.ReportAsync(userId, companyId, new DateOnly(2027, 1, 1), null, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, empty!.Centers.Count);
            Assert.IsNull(await service.ReportAsync(Guid.NewGuid(), companyId, null, null, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.UpdateAsync(userId, companyId, "NAO-HA", "X", true, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
