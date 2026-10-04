using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class FixedAssetServiceTests
{
    [TestMethod]
    public async Task KeepsTheRegisterAndBuildsTheDepreciationSchedule()
    {
        var connectionString = TestDatabase.ConnectionString("FixedAssets");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Ativos", "gabinete-ativos"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Ativos, Lda.", "AF-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Ativos", "af@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name, posting) in new[]
            {
                ("43", "Ativos fixos tangíveis", false), ("433", "Equipamento básico", true), ("4383", "Depreciações acumuladas", true),
                ("6423", "Depreciações — equipamento básico", true),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, posting));
            }

            // Uma depreciação já lançada em 2026: 200 dos 550 que o mapa vai calcular.
            var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, "L-1", new DateOnly(2026, 12, 31), "L-1", "Depreciações");
            entry.AddLine(Guid.NewGuid(), ids["6423"], "Depreciação", 200m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["4383"], "Depreciação", 0m, 200m);
            entry.Complete(JournalEntryStatus.Reviewed);
            context.Add(entry);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new FixedAssetService(context, new JournalPostingService(context));
            FixedAssetInput Input(string code, decimal value, decimal rate, DateOnly start, bool byMonths = false, DateOnly? disposal = null, bool active = true) => new(
                code, $"Ativo {code}", start, start, value, 0m, rate, byMonths, "433", "4383", "6423", disposal, active);

            var machine = await service.CreateAsync(userId, companyId, Input("maq-1", 2000m, 25m, new DateOnly(2025, 3, 1)), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("MAQ-1", machine!.Code);
            Assert.AreEqual("6423", machine.ExpenseAccountCode);
            await service.CreateAsync(userId, companyId, Input("COMP-1", 1200m, 25m, new DateOnly(2026, 11, 1), byMonths: true), CancellationToken.None).ConfigureAwait(false);
            // Alienado em 2025, comprado em 2027 e inativo: nenhum entra no mapa de 2026.
            await service.CreateAsync(userId, companyId, Input("VELHO", 500m, 10m, new DateOnly(2020, 1, 1), disposal: new DateOnly(2025, 5, 1)), CancellationToken.None).ConfigureAwait(false);
            await service.CreateAsync(userId, companyId, Input("FUTURO", 500m, 10m, new DateOnly(2027, 1, 1)), CancellationToken.None).ConfigureAwait(false);
            await service.CreateAsync(userId, companyId, Input("INATIVO", 500m, 10m, new DateOnly(2024, 1, 1), active: false), CancellationToken.None).ConfigureAwait(false);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CreateAsync(userId, companyId, Input("MAQ-1", 1m, 10m, new DateOnly(2025, 1, 1)), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CreateAsync(userId, companyId, Input("X", 1m, 10m, new DateOnly(2025, 1, 1)) with { AssetAccountCode = "43" }, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CreateAsync(userId, companyId, Input("Y", 1m, 10m, new DateOnly(2025, 1, 1)) with { ExpenseAccountCode = "9999" }, CancellationToken.None)).ConfigureAwait(false);

            var active = await service.ListAsync(userId, companyId, includeInactive: false, CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "COMP-1", "FUTURO", "MAQ-1", "VELHO" }, active!.Select(item => item.Code).ToArray());
            Assert.AreEqual(5, (await service.ListAsync(userId, companyId, includeInactive: true, CancellationToken.None).ConfigureAwait(false))!.Count);

            var schedule = await service.ScheduleAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(schedule);
            CollectionAssert.AreEqual(new[] { "COMP-1", "MAQ-1" }, schedule!.Rows.Select(row => row.Code).ToArray());
            var computer = schedule.Rows[0];
            Assert.AreEqual(50m, computer.Depreciation, "Novembro e dezembro: dois duodécimos de 300.");
            Assert.AreEqual(1150m, computer.NetValue);
            var machineRow = schedule.Rows[1];
            Assert.AreEqual(500m, machineRow.AccumulatedAtStart);
            Assert.AreEqual(500m, machineRow.Depreciation);
            Assert.AreEqual(1000m, machineRow.AccumulatedAtEnd);
            Assert.AreEqual(1000m, machineRow.NetValue);
            Assert.AreEqual(550m, schedule.TotalDepreciation);
            Assert.AreEqual(3200m, schedule.TotalAcquisitionValue);
            Assert.AreEqual(200m, schedule.LedgerDepreciation);
            Assert.AreEqual(350m, schedule.Difference, "Faltam lançar 350 de depreciações.");

            // Lançar: recusa enquanto houver gastos já lançados sem confirmação, ou um ativo sem contas.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.PostDepreciationAsync(userId, companyId, 2026, "GERAL", allowExisting: false, CancellationToken.None)).ConfigureAwait(false);
            await service.CreateAsync(
                userId, companyId,
                Input("SEM-CONTAS", 100m, 10m, new DateOnly(2026, 1, 1)) with { ExpenseAccountCode = null, AccumulatedAccountCode = null },
                CancellationToken.None).ConfigureAwait(false);
            var missing = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.PostDepreciationAsync(userId, companyId, 2026, "GERAL", allowExisting: true, CancellationToken.None)).ConfigureAwait(false);
            StringAssert.Contains(missing.Message, "SEM-CONTAS");
            await service.UpdateAsync(
                userId, companyId, "SEM-CONTAS", Input("SEM-CONTAS", 100m, 10m, new DateOnly(2026, 1, 1), active: false), CancellationToken.None).ConfigureAwait(false);

            var posted = await service.PostDepreciationAsync(userId, companyId, 2026, "GERAL", allowExisting: true, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(posted);
            Assert.AreEqual(new DateOnly(2026, 12, 31), posted!.EntryDate);
            Assert.AreEqual("Depreciações 2026", posted.DocumentReference);
            Assert.AreEqual("Por conferir", posted.Status);
            Assert.AreEqual(2, posted.Lines.Count, "As duas depreciações somam-se por conta.");
            Assert.AreEqual(550m, posted.Lines.Single(line => line.AccountCode == "6423").Debit);
            Assert.AreEqual(550m, posted.Lines.Single(line => line.AccountCode == "4383").Credit);
            // Não se lança duas vezes o mesmo exercício.
            var twice = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.PostDepreciationAsync(userId, companyId, 2026, "GERAL", allowExisting: true, CancellationToken.None)).ConfigureAwait(false);
            StringAssert.Contains(twice.Message, posted.Reference);
            Assert.AreEqual(750m, (await service.ScheduleAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false))!.LedgerDepreciation);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.PostDepreciationAsync(userId, companyId, 2019, "GERAL", allowExisting: true, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.PostDepreciationAsync(Guid.NewGuid(), companyId, 2026, "GERAL", true, CancellationToken.None).ConfigureAwait(false));

            // Alterar: a alienação em 2026 tira a quota desse ano (sem duodécimos).
            var updated = await service
                .UpdateAsync(userId, companyId, "maq-1", Input("MAQ-1", 2000m, 25m, new DateOnly(2025, 3, 1), disposal: new DateOnly(2026, 9, 1)), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(new DateOnly(2026, 9, 1), updated!.DisposalDate);
            var after = await service.ScheduleAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0m, after!.Rows.Single(row => row.Code == "MAQ-1").Depreciation);

            Assert.IsNull(await service.UpdateAsync(userId, companyId, "NAO-EXISTE", Input("NAO-EXISTE", 1m, 10m, new DateOnly(2025, 1, 1)), CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, false, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ScheduleAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
