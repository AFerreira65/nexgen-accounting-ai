using AccountingAI.Application.Auditing;
using AccountingAI.Application.Reporting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class SavedAnalysisTests
{
    [TestMethod]
    public async Task SavesValidatesAndAuditsAnalyses()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Analysis_{Guid.NewGuid():N}";
        var configuredServer = Environment.GetEnvironmentVariable("ACCOUNTINGAI_TEST_SQL_CONNECTION");
        var connectionBuilder = string.IsNullOrWhiteSpace(configuredServer)
            ? new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            }
            : new SqlConnectionStringBuilder(configuredServer);
        connectionBuilder.InitialCatalog = databaseName;
        var connectionString = connectionBuilder.ConnectionString;

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
            context.Add(new AccountingFirm(firmId, "Gabinete de Análises", "gabinete-analises"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa das Análises, Lda.", "ANALYSIS-PT-0001", TaxRegion.Mainland));
            context.Add(new UserAccount(currentUser.UserId!.Value, "Utilizador das Análises", "analises@test.invalid"));
            context.Add(new CompanyUser(companyId, currentUser.UserId.Value, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new SavedAnalysisService(context);
            var userId = currentUser.UserId.Value;

            var definition = new AnalysisDefinition(
                AnalysisSource.Movimentos,
                ["entryDate", "accountCode", "accountName", "debit", "credit"],
                [new AnalysisFilter("status", "eq", "Conferido")],
                ["accountCode"],
                [new AnalysisSort("entryDate", Descending: false)],
                [new AnalysisAggregate("debit", AnalysisAggregateFunction.Sum)],
                IncludeGrandTotal: true);

            var invalid = await service
                .CreateAsync(userId, companyId, "Inválida", null,
                    definition with { Columns = ["naoExiste"] }, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsFalse(invalid.NotFound);
            Assert.IsTrue(invalid.Errors.Count > 0);
            Assert.IsNull(invalid.Saved);

            var created = await service
                .CreateAsync(userId, companyId, "Movimentos por conta", "Débito por conta, conferidos", definition, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(created.Saved);
            Assert.AreEqual(0, created.Errors.Count);

            var duplicate = await service
                .CreateAsync(userId, companyId, "Movimentos por conta", null, definition, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsTrue(duplicate.Errors.Any(message => message.Contains("Já existe")));

            context.ChangeTracker.Clear();
            var stored = await context.SavedAnalyses
                .SingleAsync(item => item.Id == created.Saved!.Id).ConfigureAwait(false);
            var storedEntry = context.Entry(stored);
            Assert.AreNotEqual(
                default(DateTimeOffset), storedEntry.Property("CreatedAtUtc").CurrentValue,
                "O interceptor deve carimbar CreatedAtUtc.");
            Assert.AreEqual(userId, storedEntry.Property("CreatedBy").CurrentValue);
            StringAssert.Contains(stored.DefinitionJson, "\"source\":\"movimentos\"");

            var list = await service.ListAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(list);
            Assert.AreEqual(1, list!.Count);
            Assert.AreEqual(AnalysisSource.Movimentos, list[0].Definition.Source);

            var updated = await service
                .UpdateAsync(userId, companyId, created.Saved!.Id, "Movimentos por conta (2026)", "Actualizada", definition, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(updated.Saved);

            Assert.IsTrue(await service.DeleteAsync(userId, companyId, created.Saved!.Id, CancellationToken.None).ConfigureAwait(false));
            Assert.IsFalse(await service.DeleteAsync(userId, companyId, created.Saved!.Id, CancellationToken.None).ConfigureAwait(false));

            var noAccess = await service.ListAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNull(noAccess);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
