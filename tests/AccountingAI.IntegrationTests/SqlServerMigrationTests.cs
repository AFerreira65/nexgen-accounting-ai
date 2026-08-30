using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class SqlServerMigrationTests
{
    [TestMethod]
    public async Task MigrationsAndAccountingSeedCanBeAppliedTwiceToLocalDb()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
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
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260830115718_AccountingLedgerTestData");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260830122003_AccountingStandardsAndProvisioning");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260830182151_AccountingTaxonomies");

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

            var services = new ServiceCollection()
                .AddDbContext<AccountingDbContext>(builder => builder.UseSqlServer(
                    connectionString,
                    sqlOptions =>
                    {
                        sqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            "infrastructure");
                        sqlOptions.EnableRetryOnFailure();
                    }))
                .AddScoped<CompanyAccountingProvisioner>()
                .AddScoped<AccountingMasterDataService>()
                .AddScoped<TaxonomyReviewService>()
                .AddScoped<SaftCompanyCreationService>()
                .BuildServiceProvider();
            await services.SeedDevelopmentOrganizationAsync().ConfigureAwait(false);
            await services.SeedDevelopmentAccountingAsync().ConfigureAwait(false);
            await services.SeedDevelopmentOrganizationAsync().ConfigureAwait(false);
            await services.SeedDevelopmentAccountingAsync().ConfigureAwait(false);

            var esnlCompanyId = Guid.NewGuid();
            await using (var provisioningScope = services.CreateAsyncScope())
            {
                var provisioner = provisioningScope.ServiceProvider
                    .GetRequiredService<CompanyAccountingProvisioner>();
                await provisioner.ProvisionAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                await provisioner.ProvisionAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                await provisioner.ProvisionAsync(DevelopmentDataSeeder.DemoIslandCompanyId)
                    .ConfigureAwait(false);
                await provisioner.CreateCompanyAsync(
                    esnlCompanyId,
                    firmId,
                    "Associação Sintética",
                    "TEST-PT-ESNL-0001",
                    TaxRegion.Mainland,
                    AccountingStandard.SncNonProfit)
                    .ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            var testEntries = await context.JournalEntries
                .AsNoTracking()
                .Include(item => item.Lines)
                .OrderBy(item => item.CompanyId)
                .ToListAsync()
                .ConfigureAwait(false);

            Assert.HasCount(4, testEntries);
            Assert.HasCount(
                3,
                testEntries.Where(item => item.CompanyId == DevelopmentDataSeeder.DemoCompanyId));
            Assert.HasCount(
                1,
                testEntries.Where(item => item.CompanyId == DevelopmentDataSeeder.DemoIslandCompanyId));
            Assert.IsTrue(testEntries.All(item =>
                item.Lines.Sum(line => line.Debit) == item.Lines.Sum(line => line.Credit)));

            var provisionedCompanies = await context.Companies
                .AsNoTracking()
                .Where(item => item.Id == DevelopmentDataSeeder.DemoCompanyId ||
                    item.Id == DevelopmentDataSeeder.DemoIslandCompanyId ||
                    item.Id == esnlCompanyId)
                .ToDictionaryAsync(item => item.Id)
                .ConfigureAwait(false);
            Assert.AreEqual(
                AccountingStandard.SncGeneral,
                provisionedCompanies[DevelopmentDataSeeder.DemoCompanyId].AccountingStandard);
            Assert.AreEqual(
                AccountingStandard.SncMicroEntities,
                provisionedCompanies[DevelopmentDataSeeder.DemoIslandCompanyId].AccountingStandard);
            Assert.AreEqual(
                AccountingStandard.SncNonProfit,
                provisionedCompanies[esnlCompanyId].AccountingStandard);
            StringAssert.Contains(
                provisionedCompanies[DevelopmentDataSeeder.DemoCompanyId].AccountingSetupVersion!,
                "NCRF-v1");
            StringAssert.Contains(
                provisionedCompanies[DevelopmentDataSeeder.DemoIslandCompanyId].AccountingSetupVersion!,
                "NCRF-ME-v1");

            Assert.HasCount(
                AccountingTemplateCatalog.For(AccountingStandard.SncNonProfit).Accounts.Count,
                await context.Accounts.AsNoTracking()
                    .Where(item => item.CompanyId == esnlCompanyId)
                    .ToListAsync()
                    .ConfigureAwait(false));
            Assert.HasCount(
                AccountingTemplateCatalog.For(AccountingStandard.SncNonProfit).Journals.Count,
                await context.Journals.AsNoTracking()
                    .Where(item => item.CompanyId == esnlCompanyId)
                    .ToListAsync()
                    .ConfigureAwait(false));
            Assert.HasCount(
                AccountingTemplateCatalog.For(AccountingStandard.SncNonProfit).DocumentTypes.Count,
                await context.AccountingDocumentTypes.AsNoTracking()
                    .Where(item => item.CompanyId == esnlCompanyId)
                    .ToListAsync()
                    .ConfigureAwait(false));
            Assert.HasCount(
                AccountingTemplateCatalog.For(AccountingStandard.SncGeneral).DocumentTypes.Count,
                await context.AccountingDocumentTypes.AsNoTracking()
                    .Where(item => item.CompanyId == DevelopmentDataSeeder.DemoCompanyId)
                    .ToListAsync()
                    .ConfigureAwait(false));

            var microAccount14 = await context.Accounts
                .AsNoTracking()
                .SingleAsync(item =>
                    item.CompanyId == DevelopmentDataSeeder.DemoIslandCompanyId &&
                    item.Code == "14")
                .ConfigureAwait(false);
            var esnlAccount21 = await context.Accounts
                .AsNoTracking()
                .SingleAsync(item => item.CompanyId == esnlCompanyId && item.Code == "21")
                .ConfigureAwait(false);
            Assert.AreEqual("Outros", microAccount14.Name);
            Assert.AreEqual("Clientes e utentes", esnlAccount21.Name);

            var taxonomyAccounts = await context.Accounts
                .AsNoTracking()
                .Where(item => item.Code == "12" &&
                    (item.CompanyId == DevelopmentDataSeeder.DemoCompanyId ||
                     item.CompanyId == DevelopmentDataSeeder.DemoIslandCompanyId))
                .ToDictionaryAsync(item => item.CompanyId)
                .ConfigureAwait(false);
            Assert.AreEqual(
                "S",
                taxonomyAccounts[DevelopmentDataSeeder.DemoCompanyId].TaxonomyReference);
            Assert.AreEqual(
                "M",
                taxonomyAccounts[DevelopmentDataSeeder.DemoIslandCompanyId].TaxonomyReference);
            Assert.AreEqual(2, taxonomyAccounts[DevelopmentDataSeeder.DemoCompanyId].TaxonomyCode);

            await using (var maintenanceScope = services.CreateAsyncScope())
            {
                var masterData = maintenanceScope.ServiceProvider
                    .GetRequiredService<AccountingMasterDataService>();
                var taxonomyReview = maintenanceScope.ServiceProvider
                    .GetRequiredService<TaxonomyReviewService>();

                var createdAccount = await masterData
                    .CreateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6811",
                        "Donativos e liberalidades",
                        "6",
                        allowsPosting: true)
                    .ConfigureAwait(false);
                Assert.AreEqual("6811", createdAccount.Code);
                Assert.IsNull(createdAccount.SourceTemplateVersion);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await masterData.CreateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6811",
                        "Duplicado",
                        "6",
                        allowsPosting: true).ConfigureAwait(false));

                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await masterData.CreateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "9002",
                        "Filha de uma conta de movimento",
                        "11",
                        allowsPosting: true).ConfigureAwait(false));

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await masterData.UpdateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6221",
                        "Material de escritório",
                        isActive: false,
                        taxonomyReference: null,
                        taxonomyCode: null).ConfigureAwait(false));

                var classified = await masterData
                    .UpdateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6221",
                        "Material de escritório e economato",
                        isActive: true,
                        taxonomyReference: "S",
                        taxonomyCode: 61)
                    .ConfigureAwait(false);
                Assert.AreEqual("Material de escritório e economato", classified.Name);
                Assert.AreEqual(61, classified.TaxonomyCode);

                var secondApply = await masterData
                    .ApplyTemplateTaxonomySuggestionsAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                Assert.AreEqual(0, secondApply);

                var deactivated = await masterData
                    .UpdateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6811",
                        "Donativos e liberalidades",
                        isActive: false,
                        taxonomyReference: null,
                        taxonomyCode: null)
                    .ConfigureAwait(false);
                Assert.IsFalse(deactivated.IsActive);

                var reactivated = await masterData
                    .UpdateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6811",
                        "Donativos e liberalidades",
                        isActive: true,
                        taxonomyReference: null,
                        taxonomyCode: null)
                    .ConfigureAwait(false);
                Assert.IsTrue(reactivated.IsActive);

                var cleared = await masterData
                    .UpdateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "6221",
                        "Material de escritório e economato",
                        isActive: true,
                        taxonomyReference: null,
                        taxonomyCode: null)
                    .ConfigureAwait(false);
                Assert.IsNull(cleared.TaxonomyCode);

                var review = await taxonomyReview
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                Assert.IsNotNull(review);
                Assert.AreEqual("S", review!.Reference);
                Assert.IsTrue(review.Accounts.Any(item => item.Code == "6221" && item.TaxonomyCode is null));
                Assert.IsTrue(review.ClassifiedCount >= 2);

                var auxJournal = await masterData
                    .CreateJournalAsync(DevelopmentDataSeeder.DemoCompanyId, "AUX", "Diário auxiliar")
                    .ConfigureAwait(false);
                Assert.AreEqual("AUX", auxJournal.Code);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await masterData.CreateJournalAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "AUX", "Duplicado").ConfigureAwait(false));

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await masterData.UpdateJournalAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "COMPRAS",
                        "Diário de compras",
                        isActive: false).ConfigureAwait(false));

                var auxDocument = await masterData
                    .CreateDocumentTypeAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "FTX", "Documento auxiliar", "AUX")
                    .ConfigureAwait(false);
                Assert.AreEqual("AUX", auxDocument.DefaultJournalCode);

                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await masterData.CreateDocumentTypeAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "FTY", "Documento", "NAOEXISTE").ConfigureAwait(false));

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await masterData.UpdateJournalAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "AUX",
                        "Diário auxiliar",
                        isActive: false).ConfigureAwait(false));

                var reroutedDocument = await masterData
                    .UpdateDocumentTypeAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "FTX", "Documento auxiliar", "GERAL", isActive: true)
                    .ConfigureAwait(false);
                Assert.AreEqual("GERAL", reroutedDocument.DefaultJournalCode);
            }

            context.ChangeTracker.Clear();

            await using (var creationScope = services.CreateAsyncScope())
            {
                var creation = creationScope.ServiceProvider
                    .GetRequiredService<SaftCompanyCreationService>();

                var created = await creation
                    .CreateCompanyAsync(
                        DevelopmentDataSeeder.DemoUserId,
                        "515000000",
                        "Cliente do SAF-T, Lda.",
                        TaxRegion.Azores,
                        AccountingStandard.SncSmallEntities,
                        "EUR")
                    .ConfigureAwait(false);

                var template = AccountingTemplateCatalog.For(AccountingStandard.SncSmallEntities);
                Assert.AreEqual("515000000", created.TaxRegistrationNumber);
                Assert.AreEqual("SncSmallEntities", created.AccountingStandard);
                Assert.AreEqual(template.Accounts.Count, created.AccountCount);
                Assert.AreEqual(template.Journals.Count, created.JournalCount);
                Assert.AreEqual(template.DocumentTypes.Count, created.DocumentTypeCount);
                StringAssert.Contains(created.AccountingSetupVersion!, "NCRF-PE-v1");

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await creation.CreateCompanyAsync(
                        DevelopmentDataSeeder.DemoUserId,
                        "515000000",
                        "Duplicada",
                        TaxRegion.Azores,
                        AccountingStandard.SncSmallEntities,
                        "EUR").ConfigureAwait(false));
            }

            context.ChangeTracker.Clear();
            var saftCompany = await context.Companies
                .AsNoTracking()
                .SingleAsync(item => item.TaxRegistrationNumber == "515000000")
                .ConfigureAwait(false);
            var saftMembership = await context.CompanyUsers
                .AsNoTracking()
                .SingleAsync(item =>
                    item.CompanyId == saftCompany.Id &&
                    item.UserId == DevelopmentDataSeeder.DemoUserId)
                .ConfigureAwait(false);
            Assert.AreEqual(CompanyUserRole.Accountant, saftMembership.Role);
            Assert.AreEqual(TaxRegion.Azores, saftCompany.TaxRegion);

            await context.Database.OpenConnectionAsync().ConfigureAwait(false);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.tables t " +
                "INNER JOIN sys.schemas s ON s.schema_id = t.schema_id " +
                "WHERE (s.name = 'infrastructure' AND t.name = 'FoundationState') " +
                "OR (s.name = 'organization' AND t.name IN ('AccountingFirms', 'Companies')) " +
                "OR (s.name = 'identity' AND t.name IN ('UserAccounts', 'CompanyUsers', 'UserCompanySettings')) " +
                "OR (s.name = 'accounting' AND t.name IN ('Accounts', 'Journals', 'AccountingDocumentTypes', 'JournalEntries', 'JournalEntryLines'));";

            var tableCount = Convert.ToInt32(
                await command.ExecuteScalarAsync().ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual(11, tableCount);
        }
        catch (SqlException exception) when (IsLocalDbUnavailable(exception))
        {
            Assert.Inconclusive($"SQL Server LocalDB is unavailable: {exception.Message}");
        }
        finally
        {
            try
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
            }
            catch (SqlException exception) when (IsLocalDbUnavailable(exception))
            {
            }
        }
    }

    private static bool IsLocalDbUnavailable(SqlException exception) =>
        exception.Message.Contains("Local Database Runtime", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("automatic instance", StringComparison.OrdinalIgnoreCase);

}
