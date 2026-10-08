using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>Grupos de permissões do gabinete e o que cada utilizador pode (ADR-0018).</summary>
[TestClass]
public sealed class PermissionServiceTests
{
    [TestMethod]
    public async Task GroupsDecideWhatEachUserCanDo()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("Permissions"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Permissões", "gabinete-permissoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Permissões, Lda.", "PE-PT-0001", TaxRegion.Mainland));

            Guid User(string name, bool accountant = false)
            {
                var id = Guid.NewGuid();
                var user = new UserAccount(id, name, $"{name.ToLowerInvariant()}@permissoes.invalid");
                user.SetAccountant(accountant);
                context.Add(user);
                return id;
            }

            var owner = User("Dona");
            var toc = User("Toc", accountant: true);
            var clerk = User("Escriturario");
            var viewer = User("Consulta");
            var viewerToc = User("ConsultaToc", accountant: true);
            var stranger = User("Estranho");
            context.Add(new CompanyUser(companyId, owner, CompanyUserRole.Owner));
            context.Add(new CompanyUser(companyId, toc, CompanyUserRole.Administrator));
            context.Add(new CompanyUser(companyId, clerk, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(companyId, viewer, CompanyUserRole.Viewer));
            context.Add(new CompanyUser(companyId, viewerToc, CompanyUserRole.Viewer));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var permissions = new PermissionService(context);
            var admin = new OrganizationAdminService(new CompanyAccountingProvisioner(context), context);
            var none = CancellationToken.None;

            // Sem grupo: o predefinido da função. Sem acesso, nada.
            var ownerPermissions = await permissions.ResolveAsync(owner, companyId, none).ConfigureAwait(false);
            Assert.IsTrue(ownerPermissions.Has("movements.create"));
            Assert.IsFalse(ownerPermissions.Has("at.send_ies"), "Sem ser contabilista, nem o dono tem a senha da AT.");
            Assert.IsFalse(ownerPermissions.Has("at.credentials"));
            var tocPermissions = await permissions.ResolveAsync(toc, companyId, none).ConfigureAwait(false);
            Assert.IsTrue(tocPermissions.Has("at.send_ies") && tocPermissions.Has("at.credentials") && tocPermissions.Has("at.send_modelo22"));
            Assert.AreEqual(PermissionCatalog.All.Count, tocPermissions.Keys.Count, "O contabilista tem tudo.");
            var viewerPermissions = await permissions.ResolveAsync(viewer, companyId, none).ConfigureAwait(false);
            Assert.IsTrue(viewerPermissions.Has("movements.view"));
            Assert.IsFalse(viewerPermissions.Has("movements.create"));
            Assert.IsFalse((await permissions.ResolveAsync(viewerToc, companyId, none).ConfigureAwait(false)).Has("accounts.create"), "A função «Consulta» trava até o contabilista.");
            Assert.IsFalse((await permissions.ResolveAsync(stranger, companyId, none).ConfigureAwait(false)).IsMember);

            // Os grupos de origem existem e só quem administra os vê.
            var groups = await permissions.ListGroupsAsync(owner, firmId, none).ConfigureAwait(false);
            CollectionAssert.AreEquivalent(new[] { "Consulta", "Lançamentos", "Completo" }, groups.Select(item => item.Name).ToArray());
            Assert.IsTrue(groups.All(item => item.IsSystem));
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() => permissions.ListGroupsAsync(clerk, firmId, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => permissions.ListGroupsAsync(stranger, firmId, none)).ConfigureAwait(false);

            // Um grupo próprio, atribuído a um utilizador.
            var custom = await permissions.CreateGroupAsync(owner, firmId, "Só movimentos", null, ["movements.view", "movements.create"], none).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => permissions.CreateGroupAsync(owner, firmId, "Só movimentos", null, [], none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => permissions.CreateGroupAsync(owner, firmId, "Inventado", null, ["nao.existe"], none)).ConfigureAwait(false);
            await admin.SetMembershipAsync(owner, companyId, clerk, CompanyUserRole.Accountant, true, none, permissionGroupId: custom.Id, changePermissionGroup: true).ConfigureAwait(false);
            var clerkPermissions = await permissions.ResolveAsync(clerk, companyId, none).ConfigureAwait(false);
            Assert.IsTrue(clerkPermissions.Has("movements.create"));
            Assert.IsFalse(clerkPermissions.Has("accounts.create"));
            Assert.IsFalse(clerkPermissions.Has("vat.view"));

            // Acrescentar uma permissão ao grupo vale logo; um grupo em uso não se apaga; os de origem não se mexem.
            await permissions.UpdateGroupAsync(owner, custom.Id, "Só movimentos", "Para o escriturário", ["movements.view", "movements.create", "accounts.create"], none).ConfigureAwait(false);
            Assert.IsTrue((await permissions.ResolveAsync(clerk, companyId, none).ConfigureAwait(false)).Has("accounts.create"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => permissions.DeleteGroupAsync(owner, custom.Id, none)).ConfigureAwait(false);
            var system = groups.First(item => item.IsSystem);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => permissions.UpdateGroupAsync(owner, system.Id, "Outro", null, [], none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => permissions.DeleteGroupAsync(owner, system.Id, none)).ConfigureAwait(false);

            // Mesmo com um grupo que as inclua, as permissões da senha do TOC não passam a quem não é contabilista.
            await permissions.UpdateGroupAsync(owner, custom.Id, "Só movimentos", null, ["at.send_ies", "at.credentials", "at.view"], none).ConfigureAwait(false);
            var tampered = await permissions.ResolveAsync(clerk, companyId, none).ConfigureAwait(false);
            Assert.IsTrue(tampered.Has("at.view"));
            Assert.IsFalse(tampered.Has("at.send_ies") || tampered.Has("at.credentials"));

            // Um grupo de outro gabinete não se atribui.
            var otherFirm = Guid.NewGuid();
            context.Add(new AccountingFirm(otherFirm, "Outro Gabinete", "outro-gabinete"));
            var foreign = new PermissionGroup(Guid.NewGuid(), otherFirm, "Alheio", null, ["movements.view"]);
            context.Add(foreign);
            await context.SaveChangesAsync().ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                admin.SetMembershipAsync(owner, companyId, clerk, CompanyUserRole.Accountant, true, none, permissionGroupId: foreign.Id, changePermissionGroup: true)).ConfigureAwait(false);

            // Retirar o grupo volta ao predefinido; e apaga-se quando já ninguém o usa.
            await admin.SetMembershipAsync(owner, companyId, clerk, CompanyUserRole.Accountant, true, none, permissionGroupId: null, changePermissionGroup: true).ConfigureAwait(false);
            Assert.IsTrue((await permissions.ResolveAsync(clerk, companyId, none).ConfigureAwait(false)).Has("accounts.create"));
            await permissions.DeleteGroupAsync(owner, custom.Id, none).ConfigureAwait(false);

            // Só um contabilista dá ou tira a qualidade de contabilista.
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                admin.UpdateUserAsync(owner, clerk, "Escriturario", "escriturario@permissoes.invalid", true, isAccountant: true, cancellationToken: none)).ConfigureAwait(false);
            var promoted = await admin.UpdateUserAsync(toc, clerk, "Escriturario", "escriturario@permissoes.invalid", true, isAccountant: true, cancellationToken: none).ConfigureAwait(false);
            Assert.IsTrue(promoted.IsAccountant);
            Assert.IsTrue((await permissions.ResolveAsync(clerk, companyId, none).ConfigureAwait(false)).Has("at.send_ies"));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void TheSystemGroupsOnlyUseCatalogPermissions()
    {
        foreach (var (systemKey, _, _) in PermissionCatalog.SystemGroups)
        {
            var keys = PermissionCatalog.SystemGroupPermissions(systemKey);
            Assert.IsTrue(keys.Count > 0);
            Assert.IsTrue(keys.All(PermissionCatalog.Exists), systemKey);
        }

        Assert.IsFalse(PermissionCatalog.SystemGroupPermissions(PermissionCatalog.FullSystemKey).Any(PermissionCatalog.IsAccountantOnly));
    }
}
