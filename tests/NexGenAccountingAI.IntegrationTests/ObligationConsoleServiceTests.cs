using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A consola de obrigações em lote (F4E-02) e as permissões de cada empresa (ADR-0018).</summary>
[TestClass]
public sealed class ObligationConsoleServiceTests
{
    [TestMethod]
    public async Task TheConsoleListsEveryCompanyAndUpdatesInBatchWithinPermissions()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("ObligationConsole"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Consola", "gabinete-consola"));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Consola", "consola@test.invalid"));
            Guid Company(string name, string nif, CompanyUserRole role, bool responsible = false)
            {
                var id = Guid.NewGuid();
                context.Add(new Company(id, firmId, name, nif, TaxRegion.Mainland));
                var membership = new CompanyUser(id, userId, role);
                if (responsible)
                {
                    membership.SetResponsible(true);
                }

                context.Add(membership);
                return id;
            }

            var editable = Company("Empresa A, Lda.", "CO-PT-0001", CompanyUserRole.Accountant, responsible: true);
            var readOnly = Company("Empresa B, Lda.", "CO-PT-0002", CompanyUserRole.Viewer);
            var hidden = Company("Empresa C, Lda.", "CO-PT-0003", CompanyUserRole.Accountant);
            await context.SaveChangesAsync().ConfigureAwait(false);

            // A empresa C fica com um grupo sem acesso ao calendário de obrigações.
            var permissions = new PermissionService(context);
            var admin = new OrganizationAdminService(new CompanyAccountingProvisioner(context), context);
            var owner = Guid.NewGuid();
            context.Add(new UserAccount(owner, "Dona Consola", "dona-consola@test.invalid"));
            context.Add(new CompanyUser(editable, owner, CompanyUserRole.Owner));
            context.Add(new CompanyUser(hidden, owner, CompanyUserRole.Owner));
            await context.SaveChangesAsync().ConfigureAwait(false);
            var group = await permissions.CreateGroupAsync(owner, firmId, "Sem obrigações", null, ["movements.view"], CancellationToken.None).ConfigureAwait(false);
            await admin.SetMembershipAsync(owner, hidden, userId, CompanyUserRole.Accountant, true, CancellationToken.None, permissionGroupId: group.Id, changePermissionGroup: true).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var console = new ObligationConsoleService(context, new ObligationCalendarService(context), permissions);
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 12, 31);
            var result = await console.BuildAsync(userId, from, to, null).ConfigureAwait(false);

            Assert.AreEqual(2, result.CompanyCount, "A empresa sem acesso ao calendário não entra.");
            Assert.IsTrue(result.Rows.Count > 0);
            Assert.IsTrue(result.Rows.All(row => row.CompanyId != hidden));
            Assert.IsTrue(result.Rows.Where(row => row.CompanyId == editable).All(row => row.CanEdit));
            Assert.IsTrue(result.Rows.Where(row => row.CompanyId == readOnly).All(row => !row.CanEdit), "«Consulta» vê mas não altera.");
            CollectionAssert.AreEqual(result.Rows.Select(row => row.Obligation.DueDate).Order().ToList(), result.Rows.Select(row => row.Obligation.DueDate).ToList(), "Por prazo.");

            // Só as empresas de que o utilizador é responsável.
            var mine = await console.BuildAsync(userId, from, to, userId).ConfigureAwait(false);
            Assert.IsTrue(mine.Rows.All(row => row.CompanyId == editable));

            // Em lote: recusa tudo se uma empresa não permite, e atualiza as que permitem.
            var first = result.Rows.First(row => row.CompanyId == editable);
            var second = result.Rows.First(row => row.CompanyId == editable && row.Obligation.Code != first.Obligation.Code);
            var blocked = result.Rows.First(row => row.CompanyId == readOnly);
            var update = new ObligationUpdate("Entregue", new DateOnly(2026, 2, 1), null, null);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() => console.UpdateBatchAsync(
                userId,
                [new ObligationTarget(editable, first.Obligation.Code, first.Obligation.PeriodKey), new ObligationTarget(readOnly, blocked.Obligation.Code, blocked.Obligation.PeriodKey)],
                update)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() => console.UpdateBatchAsync(
                userId, [new ObligationTarget(hidden, first.Obligation.Code, first.Obligation.PeriodKey)], update)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => console.UpdateBatchAsync(userId, [], update)).ConfigureAwait(false);

            var updated = await console.UpdateBatchAsync(
                userId,
                [new ObligationTarget(editable, first.Obligation.Code, first.Obligation.PeriodKey), new ObligationTarget(editable, second.Obligation.Code, second.Obligation.PeriodKey)],
                update).ConfigureAwait(false);
            Assert.AreEqual(2, updated);

            var after = await console.BuildAsync(userId, from, to, userId).ConfigureAwait(false);
            Assert.AreEqual(2, after.Rows.Count(row => row.Obligation.State == "Entregue"));
            Assert.AreEqual(0, (await console.BuildAsync(userId, from, to, null).ConfigureAwait(false)).Rows.Count(row => row.CompanyId == readOnly && row.Obligation.State == "Entregue"), "Nada mudou na empresa recusada.");
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
