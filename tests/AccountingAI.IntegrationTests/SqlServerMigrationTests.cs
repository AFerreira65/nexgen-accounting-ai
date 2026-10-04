using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Efatura;
using AccountingAI.Domain.Organizations;
using AccountingAI.Domain.Saft;
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
    public async Task MigrationsAndAccountingSeedCanBeAppliedTwiceToSqlServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Foundation_{Guid.NewGuid():N}";
        var configuredServer = Environment.GetEnvironmentVariable("ACCOUNTINGAI_TEST_SQL_CONNECTION");
        var connectionBuilder = string.IsNullOrWhiteSpace(configuredServer)
            ? new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true
            }
            : new SqlConnectionStringBuilder(configuredServer);
        connectionBuilder.InitialCatalog = databaseName;
        var connectionString = connectionBuilder.ConnectionString;

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
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260830234235_JournalEntryDocumentType");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260831215048_DocumentNumberSeries");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260903124046_DocumentNumberSeriesWidth");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260903131441_CompanyPostingLockDate");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260903134406_JournalEntryNotes");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260903151623_UserCompanySettingsTextSize");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260903154133_ShortAccountCodesAreIntegration");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260903161831_AssociateDemoUserWithMemberlessCompanies");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260904102947_JournalEntrySource");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260908092224_SaftValidationRun");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260908120313_EFaturaPurchase");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260908145523_AuditColumns");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260908154347_SavedAnalysis");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260908161754_ReportTemplate");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260909180838_ManagementReports");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260909185631_VatReturnFiling");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260910200132_CompanyAtCredentials");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260910202125_AccountVatClassification");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260911151602_IrcEstimateFiling");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260911180622_AtSettingsPerCompany");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20260917171335_UserAccountPasswordHash");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003144942_Parties");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003174527_JournalEntryLineParty");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003175412_JournalEntryDocumentDate");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003180058_VatCodes");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003180432_JournalEntryLineVatCode");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003183508_EntryTemplates");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003202303_CostCenters");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261003205709_CompanyAndPartyAddress");
            CollectionAssert.Contains(
                appliedMigrations.ToArray(),
                "20261004070149_FixedAssets");

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
            settings.Update("pt-PT", "Europe/Lisbon", 2026, InterfaceTheme.Light, false, 100, InterfaceTextSize.Larger);

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
            Assert.AreEqual(InterfaceTextSize.Larger, storedSettings.TextSize);

            var validationRun = SaftValidationRun.Create(
                companyId, userId, "saft.xml", "1.04_01",
                schemaAvailable: true, schemaValid: false,
                errorCount: 2, warningCount: 1, informationCount: 0, truncated: false,
                issuesJson: "[{\"ruleId\":\"ENTRY-COUNT\"}]");
            context.Add(validationRun);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var storedRun = await context.SaftValidationRuns
                .AsNoTracking()
                .SingleAsync(item => item.Id == validationRun.Id)
                .ConfigureAwait(false);
            Assert.AreEqual("saft.xml", storedRun.FileName);
            Assert.AreEqual(2, storedRun.ErrorCount);
            Assert.IsFalse(storedRun.SchemaValid);
            Assert.AreEqual(companyId, storedRun.CompanyId);
            StringAssert.Contains(storedRun.IssuesJson, "ENTRY-COUNT");
            Assert.AreNotEqual(default, storedRun.CreatedAtUtc);

            var purchase = EFaturaPurchase.Create(
                companyId, Guid.NewGuid(), "500000001", "Fornecedor A", "FT A/1", null,
                new DateOnly(2026, 7, 1), "FT", "Registada", "Outros", 100m, 23m, 123m, "efatura.csv");
            context.EFaturaPurchases.Add(purchase);
            await context.SaveChangesAsync().ConfigureAwait(false);
            // A chave (NIF|nº|data) é única por empresa.
            context.EFaturaPurchases.Add(EFaturaPurchase.Create(
                companyId, Guid.NewGuid(), "500000001", "Fornecedor A", "FT A/1", null,
                new DateOnly(2026, 7, 1), "FT", "Registada", "Outros", 100m, 23m, 123m, "outro.csv"));
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
                await context.SaveChangesAsync().ConfigureAwait(false));
            context.ChangeTracker.Clear();

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
                .AddScoped<OrganizationAdminService>()
                .AddScoped<JournalPostingService>()
                .AddScoped<DocumentNumberSeriesService>()
                .AddScoped<YearEndClosingService>()
                .AddScoped<YearOpeningService>()
                .AddScoped<AccountLedgerService>()
                .AddScoped<JournalReportService>()
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

            // A conta de razão 12 é de integração; a subconta de movimento 121 herda
            // a taxonomia (código 2) do prefixo.
            var taxonomyAccounts = await context.Accounts
                .AsNoTracking()
                .Where(item => item.Code == "121" &&
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
                        "6221",
                        allowsPosting: true).ConfigureAwait(false));

                // Contas de 1 ou 2 dígitos não podem ser de movimento.
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await masterData.CreateAccountAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "77",
                        "Razão a lançar diretamente",
                        null,
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

                var firstApply = await masterData
                    .ApplyTemplateTaxonomySuggestionsAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                var secondApply = await masterData
                    .ApplyTemplateTaxonomySuggestionsAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                Assert.IsTrue(firstApply >= 1, "A primeira aplicação deve classificar contas pelo catálogo completo.");
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

                // Importar o plano de contas de um SAF-T: cria as novas, liga à conta-mãe
                // pelo prefixo e respeita a regra dos códigos curtos; é idempotente.
                await using var importScope = services.CreateAsyncScope();
                var importContext = importScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                var reader = new FixedAccountsReader(
                [
                    new SaftAccount("11", "Caixa (já existe)"),
                    new SaftAccount("13", "Outros meios monetários"),
                    new SaftAccount("131", "Cofre"),
                    new SaftAccount("11", "Duplicado a ignorar")
                ]);
                var importService = new SaftAccountsImportService(reader, importContext);

                var import = await importService
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsNotNull(import);
                Assert.AreEqual(2, import!.CreatedCount);
                Assert.AreEqual(1, import.AlreadyExistedCount);
                var razao13 = import.Created.Single(item => item.Code == "13");
                Assert.IsFalse(razao13.AllowsPosting); // 2 dígitos: integração
                Assert.AreEqual("1", razao13.ParentCode);
                var cofre = import.Created.Single(item => item.Code == "131");
                Assert.IsTrue(cofre.AllowsPosting); // 3 dígitos: movimento
                Assert.AreEqual("13", cofre.ParentCode);

                var reimport = await importService
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.AreEqual(0, reimport!.CreatedCount);

                var noAccess = await importService
                    .ImportAsync(Guid.NewGuid(), created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsNull(noAccess);

                // Pré-visualização dos movimentos: resume o ficheiro e marca os diários
                // que já existem na empresa (o SncSmallEntities provisiona GERAL).
                var entriesReader = new FixedEntriesReader(new SaftEntriesSummary(
                    HasEntries: true,
                    TransactionCount: 4,
                    DeclaredEntryCount: 4,
                    TotalDebit: 500m,
                    TotalCredit: 500m,
                    FromDate: new DateOnly(2026, 1, 10),
                    ToDate: new DateOnly(2026, 11, 3),
                    Journals:
                    [
                        new SaftJournalSummary("GERAL", "Operações diversas", 3),
                        new SaftJournalSummary("NOVOD", "Diário só do ficheiro", 1)
                    ]));
                var entriesPreview = new SaftEntriesPreviewService(entriesReader, importContext);
                var entries = await entriesPreview
                    .PreviewAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsNotNull(entries);
                Assert.IsTrue(entries!.HasEntries);
                Assert.AreEqual(4, entries.TransactionCount);
                Assert.IsTrue(entries.Balanced);
                Assert.AreEqual(new DateOnly(2026, 1, 10), entries.FromDate);
                Assert.IsTrue(entries.Journals.Single(j => j.Code == "GERAL").Exists);
                Assert.IsFalse(entries.Journals.Single(j => j.Code == "NOVOD").Exists);

                Assert.IsNull(await entriesPreview
                    .PreviewAsync(Guid.NewGuid(), created.CompanyId, Stream.Null)
                    .ConfigureAwait(false));

                // Importar os movimentos de um SAF-T: cria os diários em falta, usa o
                // TransactionID como referência (idempotente) e salta as transações que
                // não equilibram, referem contas inválidas ou têm menos de duas linhas.
                var importTransactions = new List<SaftTransaction>
                {
                    new("GERAL", "Operações diversas", "T 1", new DateOnly(2026, 2, 10), "Depósito", "T 1",
                    [
                        new SaftTransactionLine("121", "Depósito", 100m, 0m),
                        new SaftTransactionLine("111", "Saída de caixa", 0m, 100m)
                    ]),
                    new("CX", "Diário de caixa do ficheiro", "T 2", new DateOnly(2026, 2, 15), "Levantamento", "T 2",
                    [
                        new SaftTransactionLine("111", "Entrada de caixa", 50m, 0m),
                        new SaftTransactionLine("121", "Levantamento", 0m, 50m)
                    ]),
                    new("GERAL", "Operações diversas", "T 3", new DateOnly(2026, 3, 1), "Desequilibrado", "T 3",
                    [
                        new SaftTransactionLine("121", "D", 30m, 0m),
                        new SaftTransactionLine("111", "C", 0m, 20m)
                    ]),
                    new("GERAL", "Operações diversas", "T 4", new DateOnly(2026, 3, 2), "Conta inexistente", "T 4",
                    [
                        new SaftTransactionLine("999", "D", 10m, 0m),
                        new SaftTransactionLine("111", "C", 0m, 10m)
                    ]),
                    new("GERAL", "Operações diversas", "T 5", new DateOnly(2026, 3, 3), "Uma só linha", "T 5",
                    [
                        new SaftTransactionLine("121", "D", 10m, 0m)
                    ])
                };
                var importReader = new FixedEntriesReader(
                    new SaftEntriesSummary(true, 5, 5, null, null, null, null, []),
                    new SaftTransactions(5, 150m, 150m, importTransactions));
                var entriesImport = new SaftEntriesImportService(importReader, importContext);

                // Análise (sem escrever): prevê 2 importados, 3 saltados, o diário CX a
                // criar, e que os totais previstos (0 + 150) baterão certo com os declarados.
                var dryRun = await entriesImport
                    .AnalyzeAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsNotNull(dryRun);
                Assert.AreEqual(2, dryRun!.WouldImportCount);
                Assert.AreEqual(3, dryRun.WouldSkipCount);
                CollectionAssert.AreEquivalent(new[] { "CX" }, dryRun.JournalsToCreate.ToArray());
                Assert.IsTrue(dryRun.Skipped.Any(item => item.TransactionId == "T 3" && item.Reason.Contains("não coincidem")));
                Assert.AreEqual(150m, dryRun.ProjectedPeriodDebit);
                Assert.AreEqual(true, dryRun.WouldMatchDeclaredTotals);
                Assert.IsNull(await entriesImport
                    .AnalyzeAsync(Guid.NewGuid(), created.CompanyId, Stream.Null)
                    .ConfigureAwait(false));

                var importResult = await entriesImport
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsNotNull(importResult);
                Assert.AreEqual(2, importResult!.ImportedCount);
                Assert.AreEqual(3, importResult.SkippedCount);
                CollectionAssert.AreEquivalent(new[] { "CX" }, importResult.JournalsCreated.ToArray());
                Assert.IsTrue(importResult.Skipped.Any(item => item.TransactionId == "T 3" && item.Reason.Contains("não coincidem")));
                Assert.IsTrue(importResult.Skipped.Any(item => item.TransactionId == "T 4" && item.Reason.Contains("999")));
                Assert.IsTrue(importResult.Skipped.Any(item => item.TransactionId == "T 5" && item.Reason.Contains("duas linhas")));

                // Reconciliação: os 150 importados (T 1 + T 2) cobrem os totais declarados
                // no período do ficheiro (2026-02-10 a 2026-03-03).
                Assert.AreEqual(new DateOnly(2026, 2, 10), importResult.FilePeriodFrom);
                Assert.AreEqual(new DateOnly(2026, 3, 3), importResult.FilePeriodTo);
                Assert.AreEqual(150m, importResult.CompanyPeriodDebit);
                Assert.AreEqual(150m, importResult.CompanyPeriodCredit);
                Assert.AreEqual(5, importResult.DeclaredEntryCount);
                Assert.AreEqual(true, importResult.MatchesDeclaredTotals);

                // Analisar depois de importar: nada entraria (T 1/T 2 já existem), mas
                // a reconciliação prevista continua a bater certo.
                var dryRunAfter = await entriesImport
                    .AnalyzeAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.AreEqual(0, dryRunAfter!.WouldImportCount);
                Assert.AreEqual(5, dryRunAfter.WouldSkipCount);
                Assert.AreEqual(0, dryRunAfter.JournalsToCreate.Count);
                Assert.AreEqual(150m, dryRunAfter.ProjectedPeriodDebit);
                Assert.AreEqual(true, dryRunAfter.WouldMatchDeclaredTotals);

                // Reimportar não duplica: T 1 e T 2 passam a "já existe".
                var reimportResult = await entriesImport
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.AreEqual(0, reimportResult!.ImportedCount);
                Assert.AreEqual(5, reimportResult.SkippedCount);
                Assert.AreEqual(0, reimportResult.JournalsCreated.Count);
                Assert.AreEqual(true, reimportResult.MatchesDeclaredTotals);

                // Totais declarados diferentes do que a empresa tem no período: não reconcilia.
                var mismatchImport = new SaftEntriesImportService(
                    new FixedEntriesReader(
                        new SaftEntriesSummary(true, 5, 5, null, null, null, null, []),
                        new SaftTransactions(5, 999m, 0m, importTransactions)),
                    importContext);
                var mismatch = await mismatchImport
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, Stream.Null)
                    .ConfigureAwait(false);
                Assert.AreEqual(0, mismatch!.ImportedCount);
                Assert.AreEqual(false, mismatch.MatchesDeclaredTotals);

                Assert.IsNull(await entriesImport
                    .ImportAsync(Guid.NewGuid(), created.CompanyId, Stream.Null)
                    .ConfigureAwait(false));

                context.ChangeTracker.Clear();
                var importedEntries = await importContext.JournalEntries
                    .AsNoTracking()
                    .Include(item => item.Journal)
                    .Include(item => item.Lines)
                    .Where(item => item.CompanyId == created.CompanyId)
                    .ToListAsync()
                    .ConfigureAwait(false);
                Assert.HasCount(2, importedEntries);
                CollectionAssert.AreEquivalent(
                    new[] { "T 1", "T 2" },
                    importedEntries.Select(item => item.Reference).ToArray());
                Assert.IsTrue(importedEntries.All(item => item.Status == JournalEntryStatus.Reviewed));
                Assert.IsTrue(importedEntries.All(item => item.Source == EntrySource.SaftImport));
                Assert.IsTrue(importedEntries.All(item =>
                    item.Lines.Sum(line => line.Debit) == item.Lines.Sum(line => line.Credit)));
                Assert.AreEqual("CX", importedEntries.Single(item => item.Reference == "T 2").Journal.Code);
                Assert.IsTrue(await importContext.Journals.AsNoTracking()
                    .AnyAsync(item => item.CompanyId == created.CompanyId && item.Code == "CX")
                    .ConfigureAwait(false));

                // Saldos de abertura do plano de contas: entram como um lançamento no
                // diário de abertura, datado de 1 de janeiro, só para contas de movimento.
                var openingReader = new FixedAccountsReader(
                [
                    new SaftAccount("111", "Caixa geral", 1500m, 0m),
                    new SaftAccount("121", "Depósito à ordem", 0m, 900m),
                    new SaftAccount("221", "Fornecedores", 0m, 600m),
                    new SaftAccount("11", "Caixa (agregadora, ignorada)", 1500m, 0m)
                ]);
                var openingImport = new SaftOpeningBalancesImportService(
                    openingReader, new JournalPostingService(importContext), importContext);

                var openingResult = await openingImport
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, 2026, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsNotNull(openingResult);
                Assert.AreEqual(3, openingResult!.LineCount);
                Assert.AreEqual(1500m, openingResult.TotalDebit);
                Assert.AreEqual(1500m, openingResult.TotalCredit);
                Assert.IsFalse(openingResult.AlreadyImported);
                Assert.IsNotNull(openingResult.EntryReference);

                var openingAgain = await openingImport
                    .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, 2026, Stream.Null)
                    .ConfigureAwait(false);
                Assert.IsTrue(openingAgain!.AlreadyImported);
                Assert.AreEqual(openingResult.EntryReference, openingAgain.EntryReference);

                Assert.IsNull(await openingImport
                    .ImportAsync(Guid.NewGuid(), created.CompanyId, 2026, Stream.Null)
                    .ConfigureAwait(false));

                importContext.ChangeTracker.Clear();
                var abEntry = await importContext.JournalEntries
                    .AsNoTracking()
                    .Include(item => item.Lines)
                    .Include(item => item.DocumentType)
                    .SingleAsync(item => item.CompanyId == created.CompanyId && item.DocumentType!.Code == "AB")
                    .ConfigureAwait(false);
                Assert.HasCount(3, abEntry.Lines);
                Assert.AreEqual(new DateOnly(2026, 1, 1), abEntry.EntryDate);
                Assert.AreEqual(abEntry.Lines.Sum(line => line.Debit), abEntry.Lines.Sum(line => line.Credit));

                // Saldos de abertura que não equilibram: recusa com mensagem em pt-PT.
                var unbalancedOpening = new SaftOpeningBalancesImportService(
                    new FixedAccountsReader([new SaftAccount("622", "Só débito", 100m, 0m)]),
                    new JournalPostingService(importContext), importContext);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await unbalancedOpening
                        .ImportAsync(DevelopmentDataSeeder.DemoUserId, created.CompanyId, 2027, Stream.Null)
                        .ConfigureAwait(false));
            }

            await using (var adminScope = services.CreateAsyncScope())
            {
                var admin = adminScope.ServiceProvider.GetRequiredService<OrganizationAdminService>();

                var adminCompany = await admin
                    .CreateCompanyAsync(firmId, "Empresa de Gabinete, Lda.", "515222000", TaxRegion.Mainland,
                        AccountingStandard.SncGeneral, "EUR", associateUserId: DevelopmentDataSeeder.DemoUserId)
                    .ConfigureAwait(false);
                Assert.AreEqual("515222000", adminCompany.TaxRegistrationNumber);
                var adminCompanyMembership = await context.CompanyUsers.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == adminCompany.Id && item.UserId == DevelopmentDataSeeder.DemoUserId)
                    .ConfigureAwait(false);
                Assert.AreEqual(CompanyUserRole.Owner, adminCompanyMembership.Role);
                Assert.IsTrue(adminCompanyMembership.IsActive);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await admin.CreateCompanyAsync(firmId, "Duplicada", "515222000", TaxRegion.Mainland,
                        AccountingStandard.SncGeneral, "EUR").ConfigureAwait(false));

                var newUser = await admin.CreateUserAsync("Utilizador de Gabinete", "gabinete@teste.invalid").ConfigureAwait(false);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await admin.CreateUserAsync("Outro", "GABINETE@teste.invalid").ConfigureAwait(false));

                var adminMembership = await admin
                    .SetMembershipAsync(adminCompany.Id, newUser.Id, CompanyUserRole.Accountant, isActive: true)
                    .ConfigureAwait(false);
                Assert.AreEqual("Accountant", adminMembership.Role);
                var rerole = await admin
                    .SetMembershipAsync(adminCompany.Id, newUser.Id, CompanyUserRole.Viewer, isActive: true)
                    .ConfigureAwait(false);
                Assert.AreEqual("Viewer", rerole.Role);

                var updated = await admin
                    .UpdateCompanyAsync(adminCompany.Id, "Empresa de Gabinete II, Lda.", TaxRegion.Azores, "USD", isActive: false, postingLockDate: new DateOnly(2025, 12, 31))
                    .ConfigureAwait(false);
                Assert.AreEqual("Empresa de Gabinete II, Lda.", updated.LegalName);
                Assert.IsFalse(updated.IsActive);
                Assert.AreEqual(new DateOnly(2025, 12, 31), updated.PostingLockDate);

                var provisionedAccounts = await context.Accounts.AsNoTracking()
                    .CountAsync(a => a.CompanyId == adminCompany.Id).ConfigureAwait(false);
                Assert.AreEqual(
                    AccountingTemplateCatalog.For(AccountingStandard.SncGeneral).Accounts.Count,
                    provisionedAccounts);
            }

            await using (var postingScope = services.CreateAsyncScope())
            {
                var posting = postingScope.ServiceProvider.GetRequiredService<JournalPostingService>();

                var movement = await posting
                    .PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId,
                        "GERAL",
                        new DateOnly(2026, 3, 15),
                        "OD 2026/1 · lançamento de teste",
                        "Lançamento de teste",
                        reviewed: false,
                        [
                            new JournalEntryLineInput("6221", "Débito", 100m, 0m),
                            new JournalEntryLineInput("2211001", "Crédito", 0m, 100m)
                        ],
                        notes: "  Conferir contra o extrato  ")
                    .ConfigureAwait(false);
                Assert.AreEqual("GERAL-2026-00001", movement.Reference);
                Assert.AreEqual(100m, movement.Amount);
                Assert.AreEqual("Conferir contra o extrato", movement.Notes);
                Assert.HasCount(2, movement.Lines);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await posting.PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 3, 16),
                        "OD", "Desequilibrado", false,
                        [
                            new JournalEntryLineInput("6221", "D", 100m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 90m)
                        ]).ConfigureAwait(false));

                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await posting.PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 3, 16),
                        "OD", "Conta inexistente", false,
                        [
                            new JournalEntryLineInput("9999999", "D", 100m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 100m)
                        ]).ConfigureAwait(false));

                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await posting.PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 3, 16),
                        "OD", "Conta agregadora", false,
                        [
                            new JournalEntryLineInput("6", "D", 100m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 100m)
                        ]).ConfigureAwait(false));

                var stored = await context.JournalEntries.AsNoTracking()
                    .Include(entry => entry.Lines)
                    .SingleAsync(entry => entry.CompanyId == DevelopmentDataSeeder.DemoCompanyId &&
                        entry.Reference == "GERAL-2026-00001")
                    .ConfigureAwait(false);
                Assert.AreEqual(2, stored.Lines.Count);
                Assert.AreEqual("Conferir contra o extrato", stored.Notes);
                Assert.AreEqual(stored.Lines.Sum(line => line.Debit), stored.Lines.Sum(line => line.Credit));

                var reviewedMovement = await posting
                    .MarkReviewedAsync(DevelopmentDataSeeder.DemoCompanyId, "GERAL-2026-00001")
                    .ConfigureAwait(false);
                Assert.AreEqual("Conferido", reviewedMovement.Status);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await posting.ReviseAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL-2026-00001", "GERAL",
                        new DateOnly(2026, 3, 15), "OD", "x", false,
                        [
                            new JournalEntryLineInput("6221", "D", 10m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 10m)
                        ]).ConfigureAwait(false));

                // Reabrir devolve o lançamento a "por conferir" e volta a permitir corrigir.
                var reopened = await posting
                    .ReopenAsync(DevelopmentDataSeeder.DemoCompanyId, "GERAL-2026-00001")
                    .ConfigureAwait(false);
                Assert.AreEqual("Por conferir", reopened.Status);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await posting.ReopenAsync(DevelopmentDataSeeder.DemoCompanyId, "GERAL-2026-00001").ConfigureAwait(false));
                var correctedAfterReopen = await posting
                    .ReviseAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL-2026-00001", "GERAL",
                        new DateOnly(2026, 3, 15), "OD corrigido", "Corrigido após reabrir", reviewed: true,
                        [
                            new JournalEntryLineInput("6221", "D", 100m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 100m)
                        ])
                    .ConfigureAwait(false);
                Assert.AreEqual("Conferido", correctedAfterReopen.Status);
                Assert.AreEqual("Corrigido após reabrir", correctedAfterReopen.Description);

                var voidedMovement = await posting
                    .VoidAsync(DevelopmentDataSeeder.DemoCompanyId, "GERAL-2026-00001")
                    .ConfigureAwait(false);
                Assert.AreEqual("Anulado", voidedMovement.Status);

                var second = await posting
                    .PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 3, 20),
                        "OD 2026/2", "Segundo lançamento", reviewed: false,
                        [
                            new JournalEntryLineInput("6221", "D", 30m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 30m)
                        ])
                    .ConfigureAwait(false);
                Assert.AreEqual("GERAL-2026-00002", second.Reference);

                var revisedSecond = await posting
                    .ReviseAsync(
                        DevelopmentDataSeeder.DemoCompanyId, second.Reference, "CAIXA",
                        new DateOnly(2026, 3, 21), "OD 2026/2 corrigido", "Segundo corrigido", reviewed: true,
                        [
                            new JournalEntryLineInput("6221", "D", 45m, 0m),
                            new JournalEntryLineInput("111", "C", 0m, 45m)
                        ],
                        notes: "Corrigido após revisão")
                    .ConfigureAwait(false);
                Assert.AreEqual("CAIXA", revisedSecond.JournalCode);
                Assert.AreEqual("Conferido", revisedSecond.Status);
                Assert.AreEqual(45m, revisedSecond.Amount);
                Assert.AreEqual("Corrigido após revisão", revisedSecond.Notes);

                var invoice = await posting
                    .PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, journalCode: string.Empty, new DateOnly(2026, 4, 1),
                        "FT 2026/1", "Venda a cliente", reviewed: false,
                        [
                            new JournalEntryLineInput("2111001", "Cliente", 120m, 0m),
                            new JournalEntryLineInput("7211", "Serviço", 0m, 120m)
                        ],
                        documentTypeCode: "FTV")
                    .ConfigureAwait(false);
                Assert.AreEqual("FTV-2026-00001", invoice.Reference);
                Assert.AreEqual("VENDAS", invoice.JournalCode);
                Assert.AreEqual("FTV", invoice.DocumentTypeCode);

                var storedInvoice = await context.JournalEntries
                    .AsNoTracking()
                    .Include(entry => entry.DocumentType)
                    .SingleAsync(entry => entry.CompanyId == DevelopmentDataSeeder.DemoCompanyId &&
                        entry.Reference == "FTV-2026-00001")
                    .ConfigureAwait(false);
                Assert.IsNotNull(storedInvoice.DocumentTypeId);
                Assert.AreEqual("FTV", storedInvoice.DocumentType!.Code);

                var invoice2 = await posting
                    .PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, string.Empty, new DateOnly(2026, 4, 2),
                        "FT 2026/2", "Venda a cliente 2", reviewed: false,
                        [
                            new JournalEntryLineInput("2111001", "Cliente", 50m, 0m),
                            new JournalEntryLineInput("7211", "Serviço", 0m, 50m)
                        ],
                        documentTypeCode: "FTV")
                    .ConfigureAwait(false);
                Assert.AreEqual("FTV-2026-00002", invoice2.Reference);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await posting.PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, string.Empty, new DateOnly(2026, 4, 3),
                        "X", "Y", false,
                        [
                            new JournalEntryLineInput("2111001", "D", 10m, 0m),
                            new JournalEntryLineInput("7211", "C", 0m, 10m)
                        ],
                        documentTypeCode: "NAO-EXISTE").ConfigureAwait(false));

                // A numeração vem de um contador persistido por série e exercício.
                context.ChangeTracker.Clear();
                var ftvSeries = await context.DocumentNumberSeries.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == DevelopmentDataSeeder.DemoCompanyId &&
                        item.Series == "FTV" && item.Year == 2026)
                    .ConfigureAwait(false);
                Assert.AreEqual(3, ftvSeries.NextNumber); // duas faturas emitidas, próxima é a 3
                Assert.IsFalse(await context.DocumentNumberSeries.AsNoTracking()
                    .AnyAsync(item => item.Series == "NAO-EXISTE").ConfigureAwait(false));

                // Corrigir mantém a referência e não gasta número da série.
                var revisedInvoice2 = await posting
                    .ReviseAsync(
                        DevelopmentDataSeeder.DemoCompanyId, invoice2.Reference, string.Empty,
                        new DateOnly(2026, 4, 2), "FT 2026/2 corrigido", "Venda a cliente 2 corrigida", reviewed: false,
                        [
                            new JournalEntryLineInput("2111001", "Cliente", 50m, 0m),
                            new JournalEntryLineInput("7211", "Serviço", 0m, 50m)
                        ],
                        documentTypeCode: "FTV")
                    .ConfigureAwait(false);
                Assert.AreEqual("FTV-2026-00002", revisedInvoice2.Reference);
                context.ChangeTracker.Clear();
                Assert.AreEqual(3, (await context.DocumentNumberSeries.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == DevelopmentDataSeeder.DemoCompanyId &&
                        item.Series == "FTV" && item.Year == 2026).ConfigureAwait(false)).NextNumber);
            }

            // Várias sessões a emitir na mesma série ficam serializadas no SQL
            // Server e recebem referências únicas e consecutivas.
            var concurrentPosts = Enumerable.Range(1, 6).Select(async sequence =>
            {
                await using var scope = services.CreateAsyncScope();
                var posting = scope.ServiceProvider.GetRequiredService<JournalPostingService>();
                return await posting.PostAsync(
                    esnlCompanyId,
                    "GERAL",
                    new DateOnly(2026, 6, sequence),
                    $"TESTE {sequence}",
                    "Emissão concorrente",
                    reviewed: false,
                    [
                        new JournalEntryLineInput("111", "Débito", 10m, 0m),
                        new JournalEntryLineInput("121", "Crédito", 0m, 10m)
                    ]).ConfigureAwait(false);
            });
            var concurrentMovements = await Task.WhenAll(concurrentPosts).ConfigureAwait(false);
            CollectionAssert.AreEquivalent(
                Enumerable.Range(1, 6).Select(number => $"GERAL-2026-{number:D5}").ToArray(),
                concurrentMovements.Select(item => item.Reference).ToArray());

            await using (var seriesScope = services.CreateAsyncScope())
            {
                var seriesService = seriesScope.ServiceProvider.GetRequiredService<DocumentNumberSeriesService>();
                var series = await seriesService
                    .ListAsync(DevelopmentDataSeeder.DemoCompanyId, 2026)
                    .ConfigureAwait(false);
                var ftv = series.Single(item => item.Series == "FTV");
                Assert.AreEqual(2, ftv.HighestIssuedNumber);
                Assert.AreEqual(3, ftv.NextNumber);
                Assert.AreEqual(5, ftv.Width);
                Assert.AreEqual("Tipo de documento", ftv.Origin);

                var advanced = await seriesService
                    .AdvanceAsync(DevelopmentDataSeeder.DemoCompanyId, "ftv", 2026, 10, width: 6)
                    .ConfigureAwait(false);
                Assert.AreEqual(10, advanced.NextNumber);
                Assert.AreEqual(6, advanced.Width);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await seriesService.AdvanceAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "FTV", 2026, 9).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await seriesService.AdvanceAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "DESCONHECIDA", 2026, 1).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await seriesService.AdvanceAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "FTV", 2026, 10, width: 99).ConfigureAwait(false));

                // O próximo lançamento FTV usa a nova largura; anula-se logo para não
                // alterar os totais dos blocos seguintes.
                var posting = seriesScope.ServiceProvider.GetRequiredService<JournalPostingService>();
                var wide = await posting.PostAsync(
                    DevelopmentDataSeeder.DemoCompanyId, string.Empty, new DateOnly(2026, 5, 5),
                    "FT larga", "Fatura com largura 6", reviewed: false,
                    [
                        new JournalEntryLineInput("2111001", "Cliente", 10m, 0m),
                        new JournalEntryLineInput("7211", "Serviço", 0m, 10m)
                    ],
                    documentTypeCode: "FTV").ConfigureAwait(false);
                Assert.AreEqual("FTV-2026-000010", wide.Reference);
                await posting.VoidAsync(DevelopmentDataSeeder.DemoCompanyId, wide.Reference).ConfigureAwait(false);
            }

            await using (var ledgerScope = services.CreateAsyncScope())
            {
                var ledger = ledgerScope.ServiceProvider.GetRequiredService<AccountLedgerService>();

                var trial = await ledger
                    .TrialBalanceAsync(DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                Assert.AreEqual(trial.TotalDebit, trial.TotalCredit);
                Assert.AreEqual(0m, trial.TotalOpeningBalance);
                Assert.IsTrue(trial.Rows.All(row => row.OpeningBalance == 0m)); // sem data inicial, não há abertura
                var row6221 = trial.Rows.Single(row => row.AccountCode == "6221");
                Assert.AreEqual(1245m, row6221.Debit); // 1200 do seed + 45 da correção; os 100 anulados ficam de fora

                var accountLedger = await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "6221")
                    .ConfigureAwait(false);
                Assert.IsNotNull(accountLedger);
                Assert.AreEqual(2, accountLedger!.Entries.Count);
                Assert.IsFalse(accountLedger.IsAggregate);
                Assert.IsTrue(accountLedger.Entries.All(entry => entry.AccountCode == "6221"));
                Assert.AreEqual(0m, accountLedger.OpeningBalance);
                Assert.AreEqual(1245m, accountLedger.Balance);
                Assert.IsFalse(accountLedger.Entries.Any(entry => entry.Reference == "GERAL-2026-00001"));
                Assert.AreEqual(
                    "Corrigido após revisão",
                    accountLedger.Entries.Single(entry => entry.Reference == "GERAL-2026-00002").Notes);
                Assert.IsNull(await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "NAO-EXISTE")
                    .ConfigureAwait(false));

                // Filtro de origem: só restringe as linhas mostradas — o saldo de
                // abertura e o saldo final continuam a somar todas as origens.
                var manualLedger = await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "6221", null, null, default, "Manual")
                    .ConfigureAwait(false);
                Assert.AreEqual(2, manualLedger!.Entries.Count);
                Assert.AreEqual(accountLedger.Balance, manualLedger.Balance);
                var openingLedger = await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "6221", null, null, default, "Opening")
                    .ConfigureAwait(false);
                Assert.AreEqual(0, openingLedger!.Entries.Count);
                Assert.AreEqual(accountLedger.OpeningBalance, openingLedger.OpeningBalance);
                Assert.AreEqual(accountLedger.Balance, openingLedger.Balance);

                // Conta de integração: a razão soma as contas de movimento com o mesmo prefixo.
                var classSixLedger = await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "6")
                    .ConfigureAwait(false);
                Assert.IsNotNull(classSixLedger);
                Assert.IsTrue(classSixLedger!.IsAggregate);
                Assert.IsTrue(classSixLedger.Entries.Count >= accountLedger.Entries.Count);
                Assert.IsTrue(classSixLedger.Entries.All(entry => entry.AccountCode.StartsWith("6", StringComparison.Ordinal)));
                Assert.IsTrue(classSixLedger.Entries.Any(entry => entry.AccountCode == "6221"));

                // Intervalo de datas: o período do balancete só apanha os lançamentos do seed
                // (agosto de 2026); a correção de março fica no saldo de abertura de cada conta.
                var trialFromAugust = await ledger
                    .TrialBalanceAsync(DevelopmentDataSeeder.DemoCompanyId, new DateOnly(2026, 8, 1), null)
                    .ConfigureAwait(false);
                var augustRow6221 = trialFromAugust.Rows.Single(row => row.AccountCode == "6221");
                Assert.AreEqual(45m, augustRow6221.OpeningBalance);
                Assert.AreEqual(1200m, augustRow6221.Debit);
                Assert.AreEqual(1245m, augustRow6221.Balance);
                Assert.AreEqual(0m, trialFromAugust.TotalOpeningBalance); // a abertura também equilibra
                // A conta 111 só tem movimento anterior a agosto: aparece apenas com saldo de abertura.
                var augustRow111 = trialFromAugust.Rows.Single(row => row.AccountCode == "111");
                Assert.AreEqual(-45m, augustRow111.OpeningBalance);
                Assert.AreEqual(0m, augustRow111.Debit);
                Assert.AreEqual(0m, augustRow111.Credit);
                Assert.AreEqual(-45m, augustRow111.Balance);

                var trialUntilJuly = await ledger
                    .TrialBalanceAsync(DevelopmentDataSeeder.DemoCompanyId, null, new DateOnly(2026, 7, 31))
                    .ConfigureAwait(false);
                Assert.AreEqual(45m, trialUntilJuly.Rows.Single(row => row.AccountCode == "6221").Debit);
                Assert.AreEqual(0m, trialUntilJuly.TotalOpeningBalance); // sem data inicial, não há abertura

                // Razão por período: os 45 da correção ficam no saldo de abertura, o seed é o único movimento.
                var ledgerFromAugust = await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "6221", new DateOnly(2026, 8, 1), null)
                    .ConfigureAwait(false);
                Assert.IsNotNull(ledgerFromAugust);
                Assert.AreEqual(45m, ledgerFromAugust!.OpeningBalance);
                Assert.HasCount(1, ledgerFromAugust.Entries);
                Assert.AreEqual(1245m, ledgerFromAugust.Balance);
                Assert.AreEqual(1245m, ledgerFromAugust.Entries[0].RunningBalance);

                var report = ledgerScope.ServiceProvider.GetRequiredService<JournalReportService>();

                var vendas = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, "VENDAS", null, null)
                    .ConfigureAwait(false);
                Assert.AreEqual(vendas.TotalDebit, vendas.TotalCredit);
                Assert.IsTrue(vendas.Entries.Any(entry => entry.Reference == "FTV-2026-00001"));
                Assert.IsTrue(vendas.Entries.All(entry => entry.JournalCode == "VENDAS"));

                var invoices = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, "FTV", null)
                    .ConfigureAwait(false);
                Assert.HasCount(2, invoices.Entries);
                Assert.IsTrue(invoices.Entries.All(entry => entry.DocumentType == "FTV"));

                var noneInOldYear = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, 2020)
                    .ConfigureAwait(false);
                Assert.HasCount(0, noneInOldYear.Entries);

                var full = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null)
                    .ConfigureAwait(false);
                Assert.IsFalse(full.Entries.Any(entry => entry.Reference == "GERAL-2026-00001"));
                Assert.IsTrue(full.Entries.All(entry => entry.Source == EntrySource.Manual));

                // Filtro de origem: só manuais (todos, aqui) e nenhum importado de SAF-T.
                var manualOnly = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null, null, null, default, null, "Manual")
                    .ConfigureAwait(false);
                Assert.AreEqual(full.Entries.Count, manualOnly.Entries.Count);
                var saftOnly = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null, null, null, default, null, "SaftImport")
                    .ConfigureAwait(false);
                Assert.HasCount(0, saftOnly.Entries);
                var ignoredSource = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null, null, null, default, null, "desconhecida")
                    .ConfigureAwait(false);
                Assert.AreEqual(full.Entries.Count, ignoredSource.Entries.Count);

                // Livro diário por intervalo de datas.
                var august = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null,
                        new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))
                    .ConfigureAwait(false);
                Assert.IsTrue(august.Entries.Count >= 3);
                Assert.IsTrue(august.Entries.All(entry => entry.EntryDate.Month == 8 && entry.EntryDate.Year == 2026));
                Assert.IsTrue(august.Entries.Any(entry => entry.Reference == "COMP-2026-00842"));

                var afterYear = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null,
                        new DateOnly(2027, 1, 1), null)
                    .ConfigureAwait(false);
                Assert.HasCount(0, afterYear.Entries);

                // Livro diário por conta movimentada.
                var byAccount = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null, null, null, default, "6221")
                    .ConfigureAwait(false);
                Assert.IsTrue(byAccount.Entries.Count >= 1);
                Assert.IsTrue(byAccount.Entries.All(entry => entry.Lines.Any(line => line.AccountCode == "6221")));
                Assert.IsTrue(byAccount.Entries.Any(entry => entry.Reference == "COMP-2026-00842"));

                var byMissingAccount = await report
                    .BuildAsync(DevelopmentDataSeeder.DemoCompanyId, null, null, null, null, null, default, "NAO-EXISTE")
                    .ConfigureAwait(false);
                Assert.HasCount(0, byMissingAccount.Entries);
            }

            await using (var closingScope = services.CreateAsyncScope())
            {
                var closing = closingScope.ServiceProvider.GetRequiredService<YearEndClosingService>();

                var preview = await closing.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false);
                Assert.IsFalse(preview.AlreadyClosed);
                Assert.AreEqual(1425m, preview.NetResult); // rendimentos 2670 − gastos 1245
                Assert.AreEqual(preview.Lines.Sum(line => line.Debit), preview.Lines.Sum(line => line.Credit));
                var resultLine = preview.Lines.Single(line => line.AccountCode == "81");
                Assert.AreEqual(1425m, resultLine.Credit);
                Assert.IsTrue(preview.Lines.Any(line => line.AccountCode == "6221" && line.Credit == 1245m));
                Assert.IsTrue(preview.Lines.Any(line => line.AccountCode == "7211" && line.Debit == 2670m));

                var registered = await closing.RegisterAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false);
                Assert.AreEqual("AP-2026-00001", registered.Reference);
                Assert.AreEqual("APURAMENTO", registered.JournalCode);
                Assert.AreEqual("AP", registered.DocumentTypeCode);
                Assert.AreEqual(EntrySource.Closing, registered.Source);
                Assert.AreEqual(new DateOnly(2026, 12, 31), registered.EntryDate);

                var afterClosing = await closing.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false);
                Assert.IsTrue(afterClosing.AlreadyClosed);
                Assert.AreEqual("AP-2026-00001", afterClosing.ClosingReference);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await closing.RegisterAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false));

                // Exercício apurado: não se lançam, corrigem nem anulam movimentos de 2026.
                var blockedPosting = closingScope.ServiceProvider.GetRequiredService<JournalPostingService>();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await blockedPosting.PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 6, 1),
                        "OD tardio", "Depois do apuramento", reviewed: false,
                        [
                            new JournalEntryLineInput("6221", "D", 5m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 5m)
                        ]).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await blockedPosting.VoidAsync(DevelopmentDataSeeder.DemoCompanyId, "COMP-2026-00842").ConfigureAwait(false));
            }

            await using (var openingScope = services.CreateAsyncScope())
            {
                var opening = openingScope.ServiceProvider.GetRequiredService<YearOpeningService>();

                // Abrir 2027 antes de apurar 2026 é impossível; aqui 2026 já foi apurado.
                var preview = await opening.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2027).ConfigureAwait(false);
                Assert.IsTrue(preview.PreviousYearClosed);
                Assert.IsFalse(preview.AlreadyOpened);
                Assert.AreEqual(preview.Lines.Sum(line => line.Debit), preview.Lines.Sum(line => line.Credit));
                // O resultado de 2026 (81 credor 1425) é transportado para 56.
                var retained = preview.Lines.Single(line => line.AccountCode == "56");
                Assert.AreEqual(1425m, retained.Credit);
                Assert.IsFalse(preview.Lines.Any(line => line.AccountCode == "81"));
                Assert.IsFalse(preview.Lines.Any(line => line.AccountCode.StartsWith("6") || line.AccountCode.StartsWith("7")));

                var registered = await opening.RegisterAsync(DevelopmentDataSeeder.DemoCompanyId, 2027).ConfigureAwait(false);
                Assert.AreEqual("AB-2027-00001", registered.Reference);
                Assert.AreEqual(EntrySource.Opening, registered.Source);
                Assert.AreEqual("ABERTURA", registered.JournalCode);
                Assert.AreEqual("AB", registered.DocumentTypeCode);
                Assert.AreEqual(new DateOnly(2027, 1, 1), registered.EntryDate);

                var afterOpening = await opening.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2027).ConfigureAwait(false);
                Assert.IsTrue(afterOpening.AlreadyOpened);
                Assert.AreEqual("AB-2027-00001", afterOpening.OpeningReference);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await opening.RegisterAsync(DevelopmentDataSeeder.DemoCompanyId, 2027).ConfigureAwait(false));

                // 2026 não pode abrir: 2025 não está apurado.
                var earlyPreview = await opening.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false);
                Assert.IsFalse(earlyPreview.PreviousYearClosed);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await opening.RegisterAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false));

                // Reabertura: não se pode reabrir o apuramento de 2026 enquanto 2027 está aberto.
                var closing = openingScope.ServiceProvider.GetRequiredService<YearEndClosingService>();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await closing.ReopenAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false));

                var reopenedOpening = await opening.ReopenAsync(DevelopmentDataSeeder.DemoCompanyId, 2027).ConfigureAwait(false);
                Assert.AreEqual("Anulado", reopenedOpening.Status);
                Assert.IsFalse((await opening.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2027).ConfigureAwait(false)).AlreadyOpened);

                var reopenedClosing = await closing.ReopenAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false);
                Assert.AreEqual("Anulado", reopenedClosing.Status);
                Assert.IsFalse((await closing.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false)).AlreadyClosed);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await closing.ReopenAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false));

                // Depois de reaberto, volta a aceitar movimentos com data de 2026.
                var postingAfterReopen = openingScope.ServiceProvider.GetRequiredService<JournalPostingService>();
                var lateEntry = await postingAfterReopen.PostAsync(
                    DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 6, 1),
                    "OD tardio", "Depois de reabrir", reviewed: false,
                    [
                        new JournalEntryLineInput("6221", "D", 5m, 0m),
                        new JournalEntryLineInput("2211001", "C", 0m, 5m)
                    ]).ConfigureAwait(false);
                Assert.IsTrue(lateEntry.Reference.StartsWith("GERAL-2026-", StringComparison.Ordinal));
            }

            await using (var lockScope = services.CreateAsyncScope())
            {
                var lockContext = lockScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                var demo = await lockContext.Companies
                    .SingleAsync(item => item.Id == DevelopmentDataSeeder.DemoCompanyId)
                    .ConfigureAwait(false);
                demo.SetPostingLockDate(new DateOnly(2026, 6, 30));
                await lockContext.SaveChangesAsync().ConfigureAwait(false);

                var posting = lockScope.ServiceProvider.GetRequiredService<JournalPostingService>();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await posting.PostAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "GERAL", new DateOnly(2026, 6, 15),
                        "OD bloqueado", "Período bloqueado", reviewed: false,
                        [
                            new JournalEntryLineInput("6221", "D", 1m, 0m),
                            new JournalEntryLineInput("2211001", "C", 0m, 1m)
                        ]).ConfigureAwait(false));

                demo.SetPostingLockDate(null);
                await lockContext.SaveChangesAsync().ConfigureAwait(false);
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

    private sealed class FixedAccountsReader(IReadOnlyList<SaftAccount> accounts) : ISaftAccountsReader
    {
        public Task<IReadOnlyList<SaftAccount>> ReadAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts);
    }

    private sealed class FixedEntriesReader(
        SaftEntriesSummary summary,
        SaftTransactions? transactions = null) : ISaftEntriesReader
    {
        public Task<SaftEntriesSummary> ReadSummaryAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(summary);

        public Task<SaftTransactions> ReadTransactionsAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(transactions ?? new SaftTransactions(null, null, null, []));
    }
}
