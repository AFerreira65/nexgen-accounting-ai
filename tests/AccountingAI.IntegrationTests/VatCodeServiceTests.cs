using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatCodeServiceTests
{
    [TestMethod]
    public async Task CreatesDefaultsForTheCompanyRegionAndMaintainsCodes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_VatCodes_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Códigos", "gabinete-codigos"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Açores, Lda.", "VC-AZ-0001", TaxRegion.Azores));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Códigos", "codigos@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VatCodeService(context);

            // Um código próprio da empresa, criado antes dos de omissão.
            var own = await service
                .CreateAsync(userId, companyId, " v-nor ", new VatCodeInput("Vendas da casa", VatRate.Normal, 16m, VatOperationNature.TransmissoesInternas, VatRegion.Acores), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual("V-NOR", own!.Code);

            // Os de omissão respeitam a região da empresa e não tocam no que já existe.
            var created = await service.EnsureDefaultsAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(created > 10, $"Esperava mais de dez códigos, obtive {created}.");
            Assert.AreEqual(0, await service.EnsureDefaultsAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false));
            context.ChangeTracker.Clear();

            var list = await service.ListAsync(userId, companyId, includeInactive: false, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(list);
            Assert.AreEqual(created + 1, list!.Count);
            Assert.AreEqual("Vendas da casa", list.Single(row => row.Code == "V-NOR").Name);
            Assert.IsTrue(list.All(row => row.Region == "Acores"));
            Assert.AreEqual(9m, list.Single(row => row.Code == "S-INT").Percentage);
            Assert.AreEqual(4m, list.Single(row => row.Code == "C-EXI-RED").Percentage);
            Assert.AreEqual(0m, list.Single(row => row.Code == "V-EXP").Percentage);
            CollectionAssert.AreEqual(
                list.Select(row => row.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray(),
                list.Select(row => row.Code).ToArray());

            // Edição e desativação.
            var updated = await service
                .UpdateAsync(userId, companyId, "v-nor", new VatCodeInput("Vendas", VatRate.Normal, 16m, VatOperationNature.TransmissoesInternas, VatRegion.Acores, IsActive: false), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsFalse(updated!.IsActive);
            var active = await service.ListAsync(userId, companyId, false, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(active!.Any(row => row.Code == "V-NOR"));
            var all = await service.ListAsync(userId, companyId, true, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(all!.Any(row => row.Code == "V-NOR"));

            // Regras: duplicado, isento com percentagem, taxa sem percentagem.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .CreateAsync(userId, companyId, "S-NOR", new VatCodeInput("Duplicado", VatRate.Normal, 16m, VatOperationNature.PrestacoesServicosInternas, VatRegion.Acores), CancellationToken.None))
                .ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .CreateAsync(userId, companyId, "X-ISE", new VatCodeInput("Isento com taxa", VatRate.Isento, 6m, VatOperationNature.OperacoesIsentasSemDeducao, VatRegion.Acores), CancellationToken.None))
                .ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UpdateAsync(userId, companyId, "S-NOR", new VatCodeInput("Sem percentagem", VatRate.Normal, 0m, VatOperationNature.PrestacoesServicosInternas, VatRegion.Acores), CancellationToken.None))
                .ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Código desconhecido / sem acesso.
            Assert.IsNull(await service.UpdateAsync(userId, companyId, "NAO-HA", new VatCodeInput("X", VatRate.Normal, 16m, VatOperationNature.TransmissoesInternas, VatRegion.Acores), CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, false, CancellationToken.None).ConfigureAwait(false));
            Assert.AreEqual(-1, await service.EnsureDefaultsAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
