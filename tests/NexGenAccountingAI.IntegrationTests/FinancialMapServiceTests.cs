using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>Os mapas desenhados pelo utilizador: guardar, correr com os valores da empresa, explicar uma linha.</summary>
[TestClass]
public sealed class FinancialMapServiceTests
{
    [TestMethod]
    public async Task SavesRunsAndExplainsMaps()
    {
        var currentUser = new CurrentUserAccessor { UserId = Guid.NewGuid() };
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .AddInterceptors(new AuditStampingInterceptor(currentUser))
            .UseNpgsql(TestDatabase.ConnectionString("FinancialMaps"), TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete dos Mapas", "gabinete-mapas-servico"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa dos Mapas, Lda.", "MAPS-PT-0001", TaxRegion.Mainland));
            var userId = currentUser.UserId!.Value;
            context.Add(new UserAccount(userId, "Utilizador dos Mapas", "mapas@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("111", "Caixa"), ("511", "Capital"), ("6221", "Trabalhos especializados"), ("6222", "Publicidade"), ("7211", "Serviços") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, "M-1", new DateOnly(2026, 3, 5), "D-1", "Lançamento");
            entry.AddLine(Guid.NewGuid(), ids["111"], "Linha", 1000m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["511"], "Linha", 0m, 1000m);
            entry.AddLine(Guid.NewGuid(), ids["6221"], "Linha", 150m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["6222"], "Linha", 50m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["7211"], "Linha", 0m, 200m);
            entry.Complete(JournalEntryStatus.Reviewed);
            context.Add(entry);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new FinancialMapService(context, new MapDataProvider(context));
            var definition = new MapDefinition(
                [
                    new("L1", "Rendimentos", Accounts: "7", Sign: MapSign.Inverted),
                    new("L2", "Gastos", Accounts: "62"),
                    new("L3", "Resultado", MapRowKind.Formula, Formula: "L1 - L2", Bold: true),
                ],
                MapColumnSets.Find(MapColumnSets.MonthAndYearToDate)!.Columns);

            var invalid = await service.CreateAsync(userId, companyId, "Inválido", null, definition with { Rows = [new("L1", "Sem contas")] }).ConfigureAwait(false);
            Assert.IsTrue(invalid.Errors.Any(error => error.Contains("não indica contas", StringComparison.Ordinal)), string.Join(" | ", invalid.Errors));

            var created = await service.CreateAsync(userId, companyId, "Resultado simples", "Rendimentos menos gastos", definition).ConfigureAwait(false);
            Assert.IsNotNull(created.Saved);
            var duplicate = await service.CreateAsync(userId, companyId, "resultado simples", null, definition).ConfigureAwait(false);
            Assert.IsTrue(duplicate.Errors.Single().Contains("Já existe", StringComparison.Ordinal), "O nome é único sem distinguir maiúsculas.");

            var listed = await service.ListAsync(userId, companyId).ConfigureAwait(false);
            Assert.IsNotNull(listed);
            Assert.AreEqual("Resultado simples", listed.Single().Name);
            Assert.AreEqual(3, listed[0].Definition.Rows.Count);
            Assert.IsNotNull(listed[0].CreatedAtUtc);
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId).ConfigureAwait(false), "Sem acesso não há mapas.");

            var result = await service.RunAsync(userId, companyId, listed[0].Definition, new MapRequest(2026, 3, 3)).ConfigureAwait(false);
            Assert.IsNotNull(result);
            CollectionAssert.AreEqual(new decimal?[] { 200m, 200m }, result.Rows.Single(row => row.Code == "L1").Values.ToArray());
            Assert.AreEqual(0m, result.Rows.Single(row => row.Code == "L3").Values[0]);
            Assert.AreEqual(0, result.Warnings.Count, string.Join(" | ", result.Warnings));

            var partial = await service.RunAsync(userId, companyId, definition with { Rows = [definition.Rows[0], new("L2", "Só especializados", Accounts: "6221")] }, new MapRequest(2026, 1, 12)).ConfigureAwait(false);
            Assert.IsNotNull(partial);
            Assert.IsTrue(partial.Warnings.Single().Contains("6222", StringComparison.Ordinal), "Avisa da conta de gastos que ficou de fora.");

            var explained = await service.ExplainAsync(userId, companyId, definition, new MapRequest(2026, 1, 12), "L2").ConfigureAwait(false);
            Assert.IsNotNull(explained);
            CollectionAssert.AreEqual(new[] { "6221", "6222" }, explained.Select(item => item.AccountCode).ToArray());
            Assert.AreEqual("Publicidade", explained[1].AccountName);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.RunAsync(userId, companyId, definition, new MapRequest(2026, 4, 2))).ConfigureAwait(false);

            var updated = await service.UpdateAsync(userId, companyId, created.Saved!.Id, "Resultado", null, definition with { Columns = MapColumnSets.Find(MapColumnSets.Monthly)!.Columns }).ConfigureAwait(false);
            Assert.IsNotNull(updated.Saved);
            Assert.AreEqual(13, updated.Saved.Definition.Columns.Count);
            Assert.IsTrue(await service.DeleteAsync(userId, companyId, created.Saved.Id).ConfigureAwait(false));
            Assert.IsFalse(await service.DeleteAsync(userId, companyId, created.Saved.Id).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
