using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>As tarefas atribuíveis do gabinete (F4E-01).</summary>
[TestClass]
public sealed class FirmTaskServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [TestMethod]
    public async Task CreatesAssignsFiltersAndGeneratesFromObligationsWithTheRightAccess()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("FirmTasks"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Tarefas", "gabinete-tarefas"));
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            context.Add(new Company(companyA, firmId, "Empresa A, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(companyB, firmId, "Empresa B, Lda.", "501964843", TaxRegion.Mainland));
            var owner = Guid.NewGuid();
            var accountant = Guid.NewGuid();
            var viewer = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            context.Add(new UserAccount(owner, "Dona Maria", "dona@test.invalid"));
            context.Add(new UserAccount(accountant, "Contabilista Rui", "rui@test.invalid"));
            context.Add(new UserAccount(viewer, "Consulta Ana", "ana@test.invalid"));
            context.Add(new UserAccount(outsider, "Estranho", "estranho@test.invalid"));
            context.Add(new CompanyUser(companyA, owner, CompanyUserRole.Owner));
            context.Add(new CompanyUser(companyB, owner, CompanyUserRole.Owner));
            var responsibleMembership = new CompanyUser(companyA, accountant, CompanyUserRole.Accountant);
            context.Add(responsibleMembership);
            context.Add(new CompanyUser(companyA, viewer, CompanyUserRole.Viewer));
            await context.SaveChangesAsync().ConfigureAwait(false);
            responsibleMembership.SetResponsible(true);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var clock = new FixedClock(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));
            var service = new FirmTaskService(context, new ObligationCalendarService(context), clock);
            var none = CancellationToken.None;

            // Criar: o autor não pode ser só de consulta; o responsável tem de ser da empresa; o título é obrigatório.
            Assert.IsTrue((await service.CreateAsync(viewer, companyA, new FirmTaskInput("Não devia", null, null, null, null, null), none).ConfigureAwait(false)).NotFound, "Só de consulta não cria.");
            Assert.IsTrue((await service.CreateAsync(outsider, companyA, new FirmTaskInput("Alheio", null, null, null, null, null), none).ConfigureAwait(false)).NotFound);
            Assert.IsTrue((await service.CreateAsync(owner, companyA, new FirmTaskInput("  ", null, null, null, null, null), none).ConfigureAwait(false)).Errors.Count > 0);
            Assert.IsTrue((await service.CreateAsync(owner, companyB, new FirmTaskInput("Para o Rui", null, null, null, accountant, null), none).ConfigureAwait(false)).Errors.Count > 0, "O Rui não é membro da empresa B.");
            Assert.IsTrue((await service.CreateAsync(owner, companyA, new FirmTaskInput("X", null, null, "urgentíssima", null, null), none).ConfigureAwait(false)).Errors.Count > 0);

            var late = (await service.CreateAsync(owner, companyA, new FirmTaskInput("Conferir o banco", "Faltam três extratos.", new DateOnly(2026, 10, 1), "high", accountant, null), none).ConfigureAwait(false)).Saved!;
            var soon = (await service.CreateAsync(owner, companyA, new FirmTaskInput("Pedir faturas de 100%_ao cliente", null, new DateOnly(2026, 10, 20), null, null, null), none).ConfigureAwait(false)).Saved!;
            var other = (await service.CreateAsync(owner, companyB, new FirmTaskInput("Abrir exercício", null, null, "low", owner, null), none).ConfigureAwait(false)).Saved!;
            Assert.AreEqual("Contabilista Rui", late.AssigneeName);
            Assert.IsTrue(late.Overdue);
            Assert.IsFalse(soon.Overdue);
            Assert.AreEqual("normal", soon.Priority);
            Assert.AreEqual("Dona Maria", late.CreatedByName);
            Assert.AreEqual("Empresa A, Lda.", late.CompanyName);

            // Listar: quem vê o quê, e a ordem (em atraso, depois por prazo, sem prazo no fim).
            var all = await service.ListAsync(owner, new FirmTaskFilter(), none).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { late.Id, soon.Id, other.Id }, all.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { late.Id, soon.Id }, (await service.ListAsync(accountant, new FirmTaskFilter(), none).ConfigureAwait(false)).Select(item => item.Id).ToArray());
            Assert.AreEqual(0, (await service.ListAsync(outsider, new FirmTaskFilter(), none).ConfigureAwait(false)).Count);
            CollectionAssert.AreEqual(new[] { late.Id }, (await service.ListAsync(accountant, new FirmTaskFilter(Assignee: "me"), none).ConfigureAwait(false)).Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { soon.Id }, (await service.ListAsync(owner, new FirmTaskFilter(CompanyId: companyA, Assignee: "none"), none).ConfigureAwait(false)).Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { other.Id }, (await service.ListAsync(owner, new FirmTaskFilter(CompanyId: companyB), none).ConfigureAwait(false)).Select(item => item.Id).ToArray());
            Assert.AreEqual(1, (await service.ListAsync(owner, new FirmTaskFilter(Search: "100%_ao"), none).ConfigureAwait(false)).Count, "O % e o _ da pesquisa valem como texto.");
            Assert.AreEqual(0, (await service.ListAsync(owner, new FirmTaskFilter(Search: "100%x"), none).ConfigureAwait(false)).Count);
            Assert.AreEqual(1, (await service.ListAsync(owner, new FirmTaskFilter(Search: "faltam TRÊS"), none).ConfigureAwait(false)).Count, "A pesquisa não distingue maiúsculas.");

            // Atualizar: concluir regista quem e quando; reabrir apaga; outra empresa não mexe.
            var done = await service.UpdateAsync(accountant, companyA, late.Id, new FirmTaskInput("Conferir o banco", "Feito.", late.DueDate, "high", accountant, "done"), none).ConfigureAwait(false);
            Assert.AreEqual("done", done.Saved!.Status);
            Assert.AreEqual("Contabilista Rui", done.Saved.CompletedByName);
            Assert.IsNotNull(done.Saved.CompletedAtUtc);
            Assert.IsFalse(done.Saved.Overdue, "Uma tarefa concluída já não está em atraso.");
            CollectionAssert.AreEqual(new[] { soon.Id }, (await service.ListAsync(owner, new FirmTaskFilter(CompanyId: companyA), none).ConfigureAwait(false)).Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { late.Id }, (await service.ListAsync(owner, new FirmTaskFilter(Status: "closed"), none).ConfigureAwait(false)).Select(item => item.Id).ToArray());
            var reopened = await service.UpdateAsync(owner, companyA, late.Id, new FirmTaskInput("Conferir o banco", null, late.DueDate, "high", accountant, "inprogress"), none).ConfigureAwait(false);
            Assert.AreEqual("inprogress", reopened.Saved!.Status);
            Assert.IsNull(reopened.Saved.CompletedAtUtc);
            Assert.IsTrue((await service.UpdateAsync(viewer, companyA, late.Id, new FirmTaskInput("Alterada", null, null, null, null, null), none).ConfigureAwait(false)).NotFound);
            Assert.IsTrue((await service.UpdateAsync(owner, companyB, late.Id, new FirmTaskInput("Noutra empresa", null, null, null, null, null), none).ConfigureAwait(false)).NotFound, "A tarefa é da empresa A.");

            // Apagar: o autor ou um administrador; um contabilista que não a criou não.
            var rui = (await service.CreateAsync(accountant, companyA, new FirmTaskInput("Do Rui", null, null, null, null, null), none).ConfigureAwait(false)).Saved!;
            Assert.IsFalse(await service.DeleteAsync(viewer, companyA, rui.Id, none).ConfigureAwait(false));
            Assert.IsTrue(await service.DeleteAsync(owner, companyA, rui.Id, none).ConfigureAwait(false), "O dono apaga a de qualquer um.");
            var owners = (await service.CreateAsync(owner, companyA, new FirmTaskInput("Do dono", null, null, null, null, null), none).ConfigureAwait(false)).Saved!;
            Assert.IsFalse(await service.DeleteAsync(accountant, companyA, owners.Id, none).ConfigureAwait(false), "Um contabilista não apaga a do dono.");

            // Os membros a quem se atribui.
            var assignees = await service.AssigneesAsync(owner, companyA, none).ConfigureAwait(false);
            CollectionAssert.AreEquivalent(new[] { "Consulta Ana", "Contabilista Rui", "Dona Maria" }, assignees!.Select(item => item.DisplayName).ToArray());
            Assert.IsTrue(assignees!.Single(item => item.UserId == accountant).IsResponsible);
            Assert.IsNull(await service.AssigneesAsync(outsider, companyA, none).ConfigureAwait(false));

            // Das obrigações: as pendentes em atraso ou a vencer, para o responsável, sem repetir.
            var generated = await service.GenerateFromObligationsAsync(owner, companyA, none).ConfigureAwait(false);
            Assert.IsTrue(generated!.Created > 0, "O calendário tem obrigações em atraso ou a vencer.");
            Assert.AreEqual(0, generated.AlreadyExisted);
            var fromObligations = (await service.ListAsync(owner, new FirmTaskFilter(CompanyId: companyA, Status: "all", Take: 500), none).ConfigureAwait(false))
                .Where(item => item.Origin is not null && item.Origin.StartsWith("obrigacao:", StringComparison.Ordinal)).ToList();
            Assert.AreEqual(generated.Created, fromObligations.Count);
            Assert.IsTrue(fromObligations.All(item => item.AssigneeUserId == accountant && item.DueDate is not null), "Vão para o responsável da empresa.");
            Assert.IsTrue(fromObligations.Any(item => item.Overdue && item.Priority == "high"));
            var again = await service.GenerateFromObligationsAsync(owner, companyA, none).ConfigureAwait(false);
            Assert.AreEqual(0, again!.Created);
            Assert.AreEqual(generated.Created, again.AlreadyExisted);
            Assert.IsNull(await service.GenerateFromObligationsAsync(viewer, companyA, none).ConfigureAwait(false), "Só de consulta não gera.");
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
