using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>
/// Quem pode alterar o cadastro: antes, qualquer utilizador com sessão podia
/// dar-se a si próprio a função de dono de qualquer empresa.
/// </summary>
[TestClass]
public sealed class OrganizationAdminAccessTests
{
    [TestMethod]
    public async Task OnlyOwnersAndAdministratorsManageACompany()
    {
        var connectionString = TestDatabase.ConnectionString("OrganizationAccess");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Acessos", "gabinete-acessos"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Acessos, Lda.", "AC-PT-0001", TaxRegion.Mainland));
            var otherCompanyId = Guid.NewGuid();
            context.Add(new Company(otherCompanyId, firmId, "Empresa Alheia, Lda.", "AC-PT-0002", TaxRegion.Mainland));

            Guid User(string name)
            {
                var id = Guid.NewGuid();
                context.Add(new UserAccount(id, name, $"{name.ToLowerInvariant()}@acessos.invalid"));
                return id;
            }

            var owner = User("Dona");
            var admin = User("Administrador");
            var accountant = User("Contabilista");
            var outsider = User("Estranho");
            context.Add(new CompanyUser(companyId, owner, CompanyUserRole.Owner));
            context.Add(new CompanyUser(companyId, admin, CompanyUserRole.Administrator));
            context.Add(new CompanyUser(companyId, accountant, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(otherCompanyId, outsider, CompanyUserRole.Owner));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new OrganizationAdminService(new CompanyAccountingProvisioner(context), context);
            var none = CancellationToken.None;

            // A falha antiga: um utilizador sem acesso a dar-se a função de dono.
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
                service.SetMembershipAsync(outsider, companyId, outsider, CompanyUserRole.Owner, true, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.SetMembershipAsync(accountant, companyId, accountant, CompanyUserRole.Owner, true, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.UpdateCompanyAsync(accountant, companyId, "Outro nome", TaxRegion.Mainland, "EUR", true, null, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
                service.UpdateCompanyAsync(owner, otherCompanyId, "Outro nome", TaxRegion.Mainland, "EUR", true, null, none)).ConfigureAwait(false);

            // O administrador gere os outros, mas não os donos nem a função de dono.
            var promoted = await service.SetMembershipAsync(admin, companyId, outsider, CompanyUserRole.Accountant, true, none).ConfigureAwait(false);
            Assert.AreEqual("Accountant", promoted.Role, "Antes, o administrador recebia 403 ao gerir outra pessoa.");
            var responsible = await service.SetMembershipAsync(admin, companyId, outsider, CompanyUserRole.Accountant, true, none, isResponsible: true).ConfigureAwait(false);
            Assert.IsTrue(responsible.IsResponsible, "F3-28C: o responsável indica-se no acesso à empresa.");
            var kept = await service.SetMembershipAsync(admin, companyId, outsider, CompanyUserRole.Viewer, true, none).ConfigureAwait(false);
            Assert.IsTrue(kept.IsResponsible, "Omitido, mantém-se.");
            var removed = await service.SetMembershipAsync(admin, companyId, outsider, CompanyUserRole.Viewer, false, none).ConfigureAwait(false);
            Assert.IsFalse(removed.IsResponsible, "Sem acesso ativo, deixa de ser responsável.");
            await service.SetMembershipAsync(admin, companyId, outsider, CompanyUserRole.Accountant, true, none).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.SetMembershipAsync(admin, companyId, accountant, CompanyUserRole.Owner, true, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.SetMembershipAsync(admin, companyId, owner, CompanyUserRole.Viewer, true, none)).ConfigureAwait(false);

            // A empresa nunca fica sem dono ativo.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.SetMembershipAsync(owner, companyId, owner, CompanyUserRole.Administrator, true, none)).ConfigureAwait(false);
            await service.SetMembershipAsync(owner, companyId, admin, CompanyUserRole.Owner, true, none).ConfigureAwait(false);
            var stepDown = await service.SetMembershipAsync(owner, companyId, owner, CompanyUserRole.Administrator, true, none).ConfigureAwait(false);
            Assert.AreEqual("Administrator", stepDown.Role, "Com outro dono, já pode deixar de o ser.");

            // Utilizadores: o próprio muda o nome mas não se desativa; os outros, quem os administra em todas as empresas.
            var renamed = await service.UpdateUserAsync(accountant, accountant, "Contabilista Sénior", "contabilista@acessos.invalid", true, none).ConfigureAwait(false);
            Assert.AreEqual("Contabilista Sénior", renamed.DisplayName);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.UpdateUserAsync(accountant, accountant, "Contabilista", "contabilista@acessos.invalid", false, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.UpdateUserAsync(admin, outsider, "Estranho", "estranho@acessos.invalid", false, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.UpdateUserAsync(owner, admin, "Administrador", "admin@acessos.invalid", true, none)).ConfigureAwait(false);
            await service.UpdateUserAsync(admin, accountant, "Contabilista", "contabilista@acessos.invalid", false, none).ConfigureAwait(false);

            // Criar: só quem administra; quem cria a empresa fica dono dela.
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() =>
                service.CreateUserAsync(accountant, "Novo", "novo@acessos.invalid", none)).ConfigureAwait(false);
            var created = await service.CreateUserAsync(owner, "Novo", "novo@acessos.invalid", none).ConfigureAwait(false);
            Assert.IsTrue(created.IsActive);
            var newCompany = await service.CreateCompanyAsync(owner, firmId, "Empresa Nova, Lda.", "515333000", TaxRegion.Mainland, AccountingStandard.SncGeneral, "EUR", none).ConfigureAwait(false);
            Assert.AreEqual(CompanyUserRole.Owner, await service.ActiveRoleAsync(owner, newCompany.Id, none).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
