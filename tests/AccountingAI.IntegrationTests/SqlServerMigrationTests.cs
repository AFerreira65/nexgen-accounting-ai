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
            }

            await using (var adminScope = services.CreateAsyncScope())
            {
                var admin = adminScope.ServiceProvider.GetRequiredService<OrganizationAdminService>();

                var adminCompany = await admin
                    .CreateCompanyAsync(firmId, "Empresa de Gabinete, Lda.", "515222000", TaxRegion.Mainland,
                        AccountingStandard.SncGeneral, "EUR")
                    .ConfigureAwait(false);
                Assert.AreEqual("515222000", adminCompany.TaxRegistrationNumber);

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
                    .UpdateCompanyAsync(adminCompany.Id, "Empresa de Gabinete II, Lda.", TaxRegion.Azores, "USD", isActive: false)
                    .ConfigureAwait(false);
                Assert.AreEqual("Empresa de Gabinete II, Lda.", updated.LegalName);
                Assert.IsFalse(updated.IsActive);

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
                        ])
                    .ConfigureAwait(false);
                Assert.AreEqual("GERAL-2026-00001", movement.Reference);
                Assert.AreEqual(100m, movement.Amount);
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
                            new JournalEntryLineInput("11", "C", 0m, 45m)
                        ])
                    .ConfigureAwait(false);
                Assert.AreEqual("CAIXA", revisedSecond.JournalCode);
                Assert.AreEqual("Conferido", revisedSecond.Status);
                Assert.AreEqual(45m, revisedSecond.Amount);

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
                        new JournalEntryLineInput("11", "Débito", 10m, 0m),
                        new JournalEntryLineInput("12", "Crédito", 0m, 10m)
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
                Assert.AreEqual("Tipo de documento", ftv.Origin);

                var advanced = await seriesService
                    .AdvanceAsync(DevelopmentDataSeeder.DemoCompanyId, "ftv", 2026, 10)
                    .ConfigureAwait(false);
                Assert.AreEqual(10, advanced.NextNumber);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await seriesService.AdvanceAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "FTV", 2026, 9).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await seriesService.AdvanceAsync(
                        DevelopmentDataSeeder.DemoCompanyId, "DESCONHECIDA", 2026, 1).ConfigureAwait(false));
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
                Assert.AreEqual(0m, accountLedger.OpeningBalance);
                Assert.AreEqual(1245m, accountLedger.Balance);
                Assert.IsFalse(accountLedger.Entries.Any(entry => entry.Reference == "GERAL-2026-00001"));
                Assert.IsNull(await ledger
                    .AccountLedgerAsync(DevelopmentDataSeeder.DemoCompanyId, "NAO-EXISTE")
                    .ConfigureAwait(false));

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
                // A conta 11 só tem movimento anterior a agosto: aparece apenas com saldo de abertura.
                var augustRow11 = trialFromAugust.Rows.Single(row => row.AccountCode == "11");
                Assert.AreEqual(-45m, augustRow11.OpeningBalance);
                Assert.AreEqual(0m, augustRow11.Debit);
                Assert.AreEqual(0m, augustRow11.Credit);
                Assert.AreEqual(-45m, augustRow11.Balance);

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
                Assert.AreEqual(new DateOnly(2026, 12, 31), registered.EntryDate);

                var afterClosing = await closing.PreviewAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false);
                Assert.IsTrue(afterClosing.AlreadyClosed);
                Assert.AreEqual("AP-2026-00001", afterClosing.ClosingReference);

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await closing.RegisterAsync(DevelopmentDataSeeder.DemoCompanyId, 2026).ConfigureAwait(false));
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
