using AccountingAI.Application.Auditing;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class ReportTemplateTests
{
    [TestMethod]
    public async Task DefaultsThenUpsertsAndCarriesIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Template_{Guid.NewGuid():N}";
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
        var connectionString = builder.ConnectionString;

        var currentUser = new CurrentUserAccessor { UserId = Guid.NewGuid() };
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .AddInterceptors(new AuditStampingInterceptor(currentUser))
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure"))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete de Impressão", "gabinete-impressao"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Impressa, Lda.", "PRINT-PT-0001", TaxRegion.Mainland));
            context.Add(new UserAccount(currentUser.UserId!.Value, "Utilizador", "print@test.invalid"));
            context.Add(new CompanyUser(companyId, currentUser.UserId.Value, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new ReportTemplateService(context);
            var userId = currentUser.UserId.Value;

            Assert.IsNull(await service.GetAsync(userId, companyId, "inexistente", CancellationToken.None).ConfigureAwait(false));

            var byDefault = await service.GetAsync(userId, companyId, "balancete", CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(byDefault);
            Assert.AreEqual("balancete", byDefault!.ReportKey);
            Assert.AreEqual(0, byDefault.HeaderLines.Count);
            Assert.IsTrue(byDefault.ShowFirmName);
            Assert.AreEqual("Portrait", byDefault.Orientation);
            Assert.AreEqual("Empresa Impressa, Lda.", byDefault.CompanyName);
            Assert.AreEqual("Gabinete de Impressão", byDefault.FirmName);

            var saved = await service
                .SaveAsync(userId, companyId, "balancete", ["Documento de trabalho", "  ", "Confidencial"], showFirmName: false, showCompanyIdentity: true, "Landscape", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(saved);
            CollectionAssert.AreEqual(new[] { "Documento de trabalho", "Confidencial" }, saved!.HeaderLines.ToArray());
            Assert.IsFalse(saved.ShowFirmName);
            Assert.AreEqual("Landscape", saved.Orientation);

            var reloaded = await service.GetAsync(userId, companyId, "balancete", CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "Documento de trabalho", "Confidencial" }, reloaded!.HeaderLines.ToArray());

            // Upsert (segunda gravação actualiza a mesma linha).
            var updated = await service
                .SaveAsync(userId, companyId, "balancete", ["Só uma linha"], showFirmName: true, showCompanyIdentity: true, "Portrait", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(1, updated!.HeaderLines.Count);
            Assert.AreEqual(1, await context.ReportTemplates.CountAsync(item => item.CompanyId == companyId).ConfigureAwait(false));

            context.ChangeTracker.Clear();
            var stored = await context.ReportTemplates.SingleAsync(item => item.CompanyId == companyId).ConfigureAwait(false);
            Assert.AreEqual(userId, context.Entry(stored).Property("CreatedBy").CurrentValue);
            Assert.IsNotNull(context.Entry(stored).Property("ModifiedAtUtc").CurrentValue);

            Assert.IsNull(await service.GetAsync(Guid.NewGuid(), companyId, "balancete", CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
