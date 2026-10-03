using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class PartyServiceTests
{
    [TestMethod]
    public async Task CreatesListsAndUpdatesPartiesWithValidation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Parties_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Terceiros", "gabinete-terceiros"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Terceiros, Lda.", "TR-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Terceiros", "tr@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "21", "Clientes", allowsPosting: false));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "21111", "Cliente A"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "22111", "Fornecedor B"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new PartyService(context);

            // Criação, com a subconta de cliente ligada.
            var customer = await service
                .CreateAsync(userId, companyId, null, "500 584 095", new PartyInput("Cliente A, Lda.", true, false, "21111", null), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(customer);
            Assert.AreEqual("PT", customer!.CountryCode);
            Assert.AreEqual("500584095", customer.TaxRegistrationNumber);
            Assert.AreEqual("21111", customer.CustomerAccountCode);
            Assert.IsNull(customer.SupplierAccountCode);

            // Um contribuinte estrangeiro não passa pelo dígito de controlo português.
            var supplier = await service
                .CreateAsync(userId, companyId, "ES", "B12345678", new PartyInput("Proveedor B, S.L.", false, true, null, "22111"), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(supplier);

            // NIF português inválido, duplicado, conta desconhecida e conta de integração.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .CreateAsync(userId, companyId, "PT", "123456780", new PartyInput("Inválido", true, false, null, null), CancellationToken.None))
                .ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .CreateAsync(userId, companyId, "PT", "500584095", new PartyInput("Duplicado", true, false, null, null), CancellationToken.None))
                .ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .CreateAsync(userId, companyId, "ES", "A00000001", new PartyInput("Conta desconhecida", true, false, "21999", null), CancellationToken.None))
                .ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .CreateAsync(userId, companyId, "ES", "A00000002", new PartyInput("Conta de integração", true, false, "21", null), CancellationToken.None))
                .ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Lista: ordem por nome, filtro por função, pesquisa e paginação.
            var all = await service.ListAsync(userId, companyId, null, PartyRoleFilter.Todos, false, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(all);
            Assert.AreEqual(2, all!.Total);
            CollectionAssert.AreEqual(
                new[] { "Cliente A, Lda.", "Proveedor B, S.L." },
                all.Items.Select(row => row.Name).ToArray());

            var suppliers = await service.ListAsync(userId, companyId, null, PartyRoleFilter.Fornecedores, false, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("B12345678", suppliers!.Items.Single().TaxRegistrationNumber);

            var byTaxNumber = await service.ListAsync(userId, companyId, "5005", PartyRoleFilter.Todos, false, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(customer.Id, byTaxNumber!.Items.Single().Id);

            var secondPage = await service.ListAsync(userId, companyId, null, PartyRoleFilter.Todos, false, 2, 1, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(2, secondPage!.Total);
            Assert.AreEqual(supplier!.Id, secondPage.Items.Single().Id);

            // Edição: passa a fornecedor também, desliga a conta e fica inativo.
            var updated = await service
                .UpdateAsync(userId, companyId, customer.Id, new PartyInput("Cliente A, S.A.", true, true, null, "22111", IsActive: false), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(updated);
            Assert.AreEqual("Cliente A, S.A.", updated!.Name);
            Assert.IsTrue(updated.IsSupplier);
            Assert.IsNull(updated.CustomerAccountCode);
            Assert.AreEqual("22111", updated.SupplierAccountCode);
            Assert.IsFalse(updated.IsActive);

            var active = await service.ListAsync(userId, companyId, null, PartyRoleFilter.Todos, false, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(1, active!.Total, "Os inativos só aparecem a pedido.");
            var withInactive = await service.ListAsync(userId, companyId, null, PartyRoleFilter.Todos, true, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(2, withInactive!.Total);

            // Terceiro desconhecido / sem acesso.
            Assert.IsNull(await service.UpdateAsync(userId, companyId, Guid.NewGuid(), new PartyInput("X", true, false, null, null), CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, null, PartyRoleFilter.Todos, false, 1, 50, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.CreateAsync(Guid.NewGuid(), companyId, "PT", "500584095", new PartyInput("X", true, false, null, null), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
