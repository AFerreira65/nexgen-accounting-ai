using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class SqlServerMigrationTests
{
    [TestMethod]
    public async Task FoundationMigrationCanBeAppliedTwiceToLocalDb()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server LocalDB validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Foundation_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;" +
            "TrustServerCertificate=true;MultipleActiveResultSets=true";

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(connectionString, sqlOptions =>
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure"))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var appliedMigrations = await context.Database
                .GetAppliedMigrationsAsync()
                .ConfigureAwait(false);

            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "202608290001_Foundation");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "202608290002_LocalOrganization");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "202608300001_MultiCompanyUserSettings");

            var firmId = Guid.NewGuid();
            var firm = new AccountingFirm(firmId, "Gabinete de Teste", "gabinete-teste");
            var companyId = Guid.NewGuid();
            var company = new Company(
                companyId,
                firmId,
                "Empresa Sintética, Lda.",
                "TEST-PT-0001",
                TaxRegion.Madeira);
            var secondCompanyId = Guid.NewGuid();
            var secondCompany = new Company(
                secondCompanyId,
                firmId,
                "Segunda Empresa Sintética, Lda.",
                "TEST-PT-0002",
                TaxRegion.Azores);
            var userId = Guid.NewGuid();
            var user = new UserAccount(userId, "Utilizador Sintético", "user@test.invalid");
            var membership = new CompanyUser(companyId, userId, CompanyUserRole.Accountant);
            var settings = new UserCompanySettings(companyId, userId);
            settings.Update("pt-PT", "Europe/Lisbon", 2026, InterfaceTheme.Light, false, 100);

            context.AddRange(firm, company, secondCompany, user, membership, settings);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var storedCompany = await context.Companies
                .AsNoTracking()
                .SingleAsync(item => item.Id == companyId)
                .ConfigureAwait(false);
            var storedSettings = await context.UserCompanySettings
                .AsNoTracking()
                .SingleAsync(item => item.CompanyId == companyId && item.UserId == userId)
                .ConfigureAwait(false);

            Assert.AreEqual(TaxRegion.Madeira, storedCompany.TaxRegion);
            Assert.AreEqual("EUR", storedCompany.BaseCurrency);
            Assert.AreEqual(100, storedSettings.PageSize);

            context.Add(new UserCompanySettings(secondCompanyId, userId));
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
                await context.SaveChangesAsync().ConfigureAwait(false));
            context.ChangeTracker.Clear();

            await context.Database.OpenConnectionAsync().ConfigureAwait(false);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.tables t " +
                "INNER JOIN sys.schemas s ON s.schema_id = t.schema_id " +
                "WHERE (s.name = 'infrastructure' AND t.name = 'FoundationState') " +
                "OR (s.name = 'organization' AND t.name IN ('AccountingFirms', 'Companies')) " +
                "OR (s.name = 'identity' AND t.name IN ('UserAccounts', 'CompanyUsers', 'UserCompanySettings'));";

            var tableCount = Convert.ToInt32(
                await command.ExecuteScalarAsync().ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual(6, tableCount);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
