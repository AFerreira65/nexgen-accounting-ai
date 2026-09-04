using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AccountingAI.Api.Tests;

[TestClass]
public sealed class FoundationApiTests
{
    [TestMethod]
    public async Task LiveHealthEndpointIsAvailableAndReturnsCorrelationId()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        const string correlationId = "f1-01-api-test";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        using var response = await client.GetAsync("/health/live").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.AreEqual(correlationId, values.Single());
    }

    [TestMethod]
    public async Task FoundationEndpointIdentifiesCurrentPhase()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/foundation").ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(payload, "F1-71");
        StringAssert.Contains(payload, "ready");
    }

    [TestMethod]
    public async Task WorkContextSettingsRejectAnUnknownTextSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        var body = new StringContent(
            """
            {"culture":"pt-PT","timeZone":"Europe/Lisbon","defaultFiscalYear":2026,"theme":"Light","compactMode":false,"pageSize":50,"textSize":"Gigante"}
            """,
            Encoding.UTF8,
            "application/json");
        using var response = await client
            .PutAsync(
                "/api/v1/development/work-context/33333333-3333-3333-3333-333333333333/" +
                "companies/22222222-2222-2222-2222-222222222222/settings",
                body)
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(payload, "tamanho do texto");
    }

    [TestMethod]
    public async Task AccountingTestDataIsReadFromTheSelectedCompanyOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        using var mainlandResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/movements")
            .ConfigureAwait(false);
        var mainlandPayload = await mainlandResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var islandResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/44444444-4444-4444-4444-444444444444/accounting/movements")
            .ConfigureAwait(false);
        var islandPayload = await islandResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorizedResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/99999999-9999-9999-9999-999999999999/accounting/movements")
            .ConfigureAwait(false);

        using var mainlandSetupResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/setup")
            .ConfigureAwait(false);
        var mainlandSetupPayload = await mainlandSetupResponse.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);

        using var islandSetupResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/44444444-4444-4444-4444-444444444444/accounting/setup")
            .ConfigureAwait(false);
        var islandSetupPayload = await islandSetupResponse.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);

        using var filteredResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/movements?documentType=FTV")
            .ConfigureAwait(false);
        var filteredPayload = await filteredResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var sourceFilteredResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/movements?source=SaftImport")
            .ConfigureAwait(false);
        var sourceFilteredPayload = await sourceFilteredResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var seriesResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/number-series?year=2026")
            .ConfigureAwait(false);
        var seriesPayload = await seriesResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorizedSeriesResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/99999999-9999-9999-9999-999999999999/accounting/number-series?year=2026")
            .ConfigureAwait(false);

        using var closedYearsResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/closed-years")
            .ConfigureAwait(false);
        var closedYearsPayload = await closedYearsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var lockDateResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/posting-lock-date")
            .ConfigureAwait(false);
        var lockDatePayload = await lockDateResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorizedLockDateResponse = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/99999999-9999-9999-9999-999999999999/accounting/posting-lock-date")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, mainlandResponse.StatusCode);
        StringAssert.Contains(mainlandPayload, "COMP-2026-00842");
        StringAssert.Contains(mainlandPayload, "\"documentType\":null");
        StringAssert.Contains(mainlandPayload, "\"notes\":");
        Assert.AreEqual(HttpStatusCode.OK, filteredResponse.StatusCode);
        Assert.IsFalse(filteredPayload.Contains("COMP-2026-00842", StringComparison.Ordinal));
        StringAssert.Contains(mainlandPayload, "\"source\":\"Manual\"");
        Assert.AreEqual(HttpStatusCode.OK, sourceFilteredResponse.StatusCode);
        Assert.IsFalse(sourceFilteredPayload.Contains("COMP-2026-00842", StringComparison.Ordinal));
        Assert.IsFalse(mainlandPayload.Contains("VEND-AZO-2026-00014", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.OK, islandResponse.StatusCode);
        StringAssert.Contains(islandPayload, "VEND-AZO-2026-00014");
        Assert.IsFalse(islandPayload.Contains("COMP-2026-00842", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorizedResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, mainlandSetupResponse.StatusCode);
        StringAssert.Contains(mainlandSetupPayload, "\"standard\":\"SncGeneral\"");
        StringAssert.Contains(mainlandSetupPayload, "\"code\":\"FTF\"");
        StringAssert.Contains(mainlandSetupPayload, "Outros instrumentos financeiros");
        StringAssert.Contains(mainlandSetupPayload, "\"taxonomyReference\":\"S\"");
        StringAssert.Contains(mainlandSetupPayload, "\"taxonomyCode\":2");
        Assert.AreEqual(HttpStatusCode.OK, islandSetupResponse.StatusCode);
        StringAssert.Contains(islandSetupPayload, "\"standard\":\"SncMicroEntities\"");
        StringAssert.Contains(islandSetupPayload, "\"code\":\"FTF\"");
        StringAssert.Contains(islandSetupPayload, "\"name\":\"Outros\"");
        StringAssert.Contains(islandSetupPayload, "\"taxonomyReference\":\"M\"");
        Assert.AreEqual(HttpStatusCode.OK, seriesResponse.StatusCode);
        StringAssert.Contains(seriesPayload, "\"series\":\"FTV\"");
        StringAssert.Contains(seriesPayload, "\"origin\":\"Tipo de documento\"");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorizedSeriesResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, closedYearsResponse.StatusCode);
        StringAssert.StartsWith(closedYearsPayload.Trim(), "[");
        Assert.AreEqual(HttpStatusCode.OK, lockDateResponse.StatusCode);
        StringAssert.Contains(lockDatePayload, "\"lockDate\"");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorizedLockDateResponse.StatusCode);
    }

    [TestMethod]
    public async Task TaxonomyReviewIsScopedToTheActiveCompanyAndClassifiesAccounts()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";

        using var mainlandResponse = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/accounting/taxonomy-review")
            .ConfigureAwait(false);
        var mainlandPayload = await mainlandResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var islandResponse = await client
            .GetAsync($"{basePath}44444444-4444-4444-4444-444444444444/accounting/taxonomy-review")
            .ConfigureAwait(false);
        var islandPayload = await islandResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorizedResponse = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/accounting/taxonomy-review")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, mainlandResponse.StatusCode);
        StringAssert.Contains(mainlandPayload, "\"reference\":\"S\"");
        StringAssert.Contains(mainlandPayload, "\"status\":\"Assigned\"");
        StringAssert.Contains(mainlandPayload, "\"status\":\"Unclassified\"");
        StringAssert.Contains(mainlandPayload, "\"code\":\"6221\"");
        Assert.AreEqual(HttpStatusCode.OK, islandResponse.StatusCode);
        StringAssert.Contains(islandPayload, "\"reference\":\"M\"");
        Assert.IsFalse(islandPayload.Contains("\"code\":\"6221\"", StringComparison.Ordinal) &&
            islandPayload.Contains("Material de escritório", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorizedResponse.StatusCode);
    }

    [TestMethod]
    public async Task TrialBalanceAndAccountLedgerAreScopedAndExcludeVoided()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string mainland = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting";

        using var trialBalance = await client.GetAsync($"{mainland}/trial-balance").ConfigureAwait(false);
        var trialBalancePayload = await trialBalance.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var ledger = await client.GetAsync($"{mainland}/accounts/6221/ledger").ConfigureAwait(false);
        var ledgerPayload = await ledger.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var aggregateLedger = await client.GetAsync($"{mainland}/accounts/6/ledger").ConfigureAwait(false);
        var aggregateLedgerPayload = await aggregateLedger.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknownAccount = await client.GetAsync($"{mainland}/accounts/NAO-EXISTE/ledger").ConfigureAwait(false);

        using var unknownCompany = await client.GetAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/99999999-9999-9999-9999-999999999999/accounting/trial-balance")
            .ConfigureAwait(false);

        using var journalReport = await client.GetAsync($"{mainland}/journal-report").ConfigureAwait(false);
        var journalReportPayload = await journalReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var filteredReport = await client.GetAsync($"{mainland}/journal-report?journal=VENDAS").ConfigureAwait(false);
        var filteredReportPayload = await filteredReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var oldYearReport = await client.GetAsync($"{mainland}/journal-report?year=2020").ConfigureAwait(false);
        var oldYearReportPayload = await oldYearReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var datedReport = await client
            .GetAsync($"{mainland}/journal-report?from=2026-08-01&to=2026-08-31").ConfigureAwait(false);
        var datedReportPayload = await datedReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var afterDateReport = await client
            .GetAsync($"{mainland}/journal-report?from=2099-01-01").ConfigureAwait(false);
        var afterDateReportPayload = await afterDateReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badDateReport = await client
            .GetAsync($"{mainland}/journal-report?from=ontem").ConfigureAwait(false);
        var badDateReportPayload = await badDateReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var accountReport = await client
            .GetAsync($"{mainland}/journal-report?account=6221").ConfigureAwait(false);
        var accountReportPayload = await accountReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var missingAccountReport = await client
            .GetAsync($"{mainland}/journal-report?account=NAO-EXISTE").ConfigureAwait(false);
        var missingAccountReportPayload = await missingAccountReport.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var datedLedger = await client
            .GetAsync($"{mainland}/accounts/6221/ledger?from=2026-08-01").ConfigureAwait(false);
        var datedLedgerPayload = await datedLedger.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var earlyTrialBalance = await client
            .GetAsync($"{mainland}/trial-balance?to=2020-12-31").ConfigureAwait(false);
        var earlyTrialBalancePayload = await earlyTrialBalance.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var datedTrialBalance = await client
            .GetAsync($"{mainland}/trial-balance?from=2026-08-01").ConfigureAwait(false);
        var datedTrialBalancePayload = await datedTrialBalance.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var closingPreview = await client
            .GetAsync($"{mainland}/year-end-closing?year=2026").ConfigureAwait(false);
        var closingPreviewPayload = await closingPreview.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var closingBadYear = await client
            .GetAsync($"{mainland}/year-end-closing?year=1000").ConfigureAwait(false);

        using var openingPreview = await client
            .GetAsync($"{mainland}/year-opening?year=2027").ConfigureAwait(false);
        var openingPreviewPayload = await openingPreview.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, trialBalance.StatusCode);
        StringAssert.Contains(trialBalancePayload, "\"totalOpeningBalance\"");
        StringAssert.Contains(trialBalancePayload, "\"totalDebit\"");
        StringAssert.Contains(trialBalancePayload, "\"accountCode\":\"6221\"");
        StringAssert.Contains(trialBalancePayload, "\"openingBalance\":0");
        Assert.AreEqual(HttpStatusCode.OK, ledger.StatusCode);
        StringAssert.Contains(ledgerPayload, "\"accountCode\":\"6221\"");
        StringAssert.Contains(ledgerPayload, "\"openingBalance\"");
        StringAssert.Contains(ledgerPayload, "\"runningBalance\"");
        StringAssert.Contains(ledgerPayload, "\"notes\"");
        StringAssert.Contains(ledgerPayload, "\"isAggregate\":false");
        Assert.AreEqual(HttpStatusCode.OK, aggregateLedger.StatusCode);
        StringAssert.Contains(aggregateLedgerPayload, "\"accountCode\":\"6\"");
        StringAssert.Contains(aggregateLedgerPayload, "\"isAggregate\":true");
        StringAssert.Contains(aggregateLedgerPayload, "\"accountCode\":\"6221\"");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownAccount.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownCompany.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, journalReport.StatusCode);
        StringAssert.Contains(journalReportPayload, "COMP-2026-00842");
        Assert.AreEqual(HttpStatusCode.OK, filteredReport.StatusCode);
        Assert.IsFalse(filteredReportPayload.Contains("COMP-2026-00842", StringComparison.Ordinal));
        StringAssert.Contains(oldYearReportPayload, "\"entries\":[]");
        Assert.AreEqual(HttpStatusCode.OK, datedReport.StatusCode);
        StringAssert.Contains(datedReportPayload, "COMP-2026-00842");
        StringAssert.Contains(afterDateReportPayload, "\"entries\":[]");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDateReport.StatusCode);
        StringAssert.Contains(badDateReportPayload, "Data inicial");
        Assert.AreEqual(HttpStatusCode.OK, accountReport.StatusCode);
        StringAssert.Contains(accountReportPayload, "COMP-2026-00842");
        StringAssert.Contains(missingAccountReportPayload, "\"entries\":[]");
        Assert.AreEqual(HttpStatusCode.OK, datedLedger.StatusCode);
        StringAssert.Contains(datedLedgerPayload, "\"openingBalance\"");
        Assert.AreEqual(HttpStatusCode.OK, earlyTrialBalance.StatusCode);
        StringAssert.Contains(earlyTrialBalancePayload, "\"rows\":[]");
        Assert.AreEqual(HttpStatusCode.OK, datedTrialBalance.StatusCode);
        StringAssert.Contains(datedTrialBalancePayload, "\"openingBalance\"");
        Assert.IsFalse(datedTrialBalancePayload.Contains("\"rows\":[]", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.OK, closingPreview.StatusCode);
        StringAssert.Contains(closingPreviewPayload, "\"netResult\"");
        StringAssert.Contains(closingPreviewPayload, "\"lines\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, closingBadYear.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, openingPreview.StatusCode);
        StringAssert.Contains(openingPreviewPayload, "\"previousYear\":2026");
        StringAssert.Contains(openingPreviewPayload, "\"previousYearClosed\"");
    }

    [TestMethod]
    public async Task MovementPostingRejectsUnbalancedAndUnknownReferences()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string movements = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/movements";

        static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        using var unbalanced = await client.PostAsync(movements, Json(
            """{"journalCode":"GERAL","entryDate":"2026-03-20","documentReference":"OD","description":"Desequilibrado","reviewed":false,"lines":[{"accountCode":"6221","description":"D","debit":100,"credit":0},{"accountCode":"2211001","description":"C","debit":0,"credit":90}]}"""))
            .ConfigureAwait(false);
        var unbalancedPayload = await unbalanced.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknownJournal = await client.PostAsync(movements, Json(
            """{"journalCode":"ZZZ","entryDate":"2026-03-20","documentReference":"OD","description":"X","reviewed":false,"lines":[{"accountCode":"6221","description":"D","debit":100,"credit":0},{"accountCode":"2211001","description":"C","debit":0,"credit":100}]}"""))
            .ConfigureAwait(false);

        using var unknownAccount = await client.PostAsync(movements, Json(
            """{"journalCode":"GERAL","entryDate":"2026-03-20","documentReference":"OD","description":"X","reviewed":false,"lines":[{"accountCode":"9999999","description":"D","debit":100,"credit":0},{"accountCode":"2211001","description":"C","debit":0,"credit":100}]}"""))
            .ConfigureAwait(false);

        using var unknownDocumentType = await client.PostAsync(movements, Json(
            """{"documentTypeCode":"NAO-EXISTE","entryDate":"2026-03-20","documentReference":"FT","description":"X","reviewed":false,"lines":[{"accountCode":"6221","description":"D","debit":10,"credit":0},{"accountCode":"2211001","description":"C","debit":0,"credit":10}]}"""))
            .ConfigureAwait(false);

        using var noMembership = await client.PostAsync(
            "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/99999999-9999-9999-9999-999999999999/accounting/movements", Json(
            """{"journalCode":"GERAL","entryDate":"2026-03-20","documentReference":"OD","description":"X","reviewed":false,"lines":[]}"""))
            .ConfigureAwait(false);

        using var reviewAlreadyReviewed = await client
            .PostAsync($"{movements}/COMP-2026-00842/review", content: null)
            .ConfigureAwait(false);

        using var voidUnknown = await client
            .PostAsync($"{movements}/NAO-EXISTE/void", content: null)
            .ConfigureAwait(false);

        using var reviseUnknown = await client
            .PutAsync($"{movements}/NAO-EXISTE", Json(
                """{"journalCode":"GERAL","entryDate":"2026-03-20","documentReference":"OD","description":"X","reviewed":false,"lines":[{"accountCode":"6221","description":"D","debit":10,"credit":0},{"accountCode":"2211001","description":"C","debit":0,"credit":10}]}"""))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, unbalanced.StatusCode);
        StringAssert.Contains(unbalancedPayload, "movement");
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownJournal.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownAccount.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownDocumentType.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, noMembership.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, reviewAlreadyReviewed.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, voidUnknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, reviseUnknown.StatusCode);
    }

    [TestMethod]
    public async Task OrganizationAdminRejectsInvalidCompanyAndUserChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        using var duplicateCompany = await client.PostAsync("/api/v1/development/organization/companies", Json(
            """{"accountingFirmId":"11111111-1111-1111-1111-111111111111","legalName":"Duplicada","taxRegistrationNumber":"DEMO-PT-0001","taxRegion":"Mainland","accountingStandard":"SncGeneral"}"""))
            .ConfigureAwait(false);
        var duplicateCompanyPayload = await duplicateCompany.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badRegion = await client.PostAsync("/api/v1/development/organization/companies", Json(
            """{"accountingFirmId":"11111111-1111-1111-1111-111111111111","legalName":"X","taxRegistrationNumber":"599111222","taxRegion":"Nowhere","accountingStandard":"SncGeneral"}"""))
            .ConfigureAwait(false);

        using var duplicateUser = await client.PostAsync("/api/v1/development/organization/users", Json(
            """{"displayName":"Outro","email":"antonio.demo@accountingai.local"}"""))
            .ConfigureAwait(false);

        using var unknownCompany = await client.PutAsync(
            "/api/v1/development/organization/companies/99999999-9999-9999-9999-999999999999", Json(
            """{"legalName":"X","taxRegion":"Mainland","baseCurrency":"EUR","isActive":true}"""))
            .ConfigureAwait(false);

        using var badRole = await client.PutAsync(
            "/api/v1/development/organization/companies/22222222-2222-2222-2222-222222222222/members/33333333-3333-3333-3333-333333333333",
            Json("""{"role":"Supreme","isActive":true}"""))
            .ConfigureAwait(false);

        using var badLockDate = await client.PutAsync(
            "/api/v1/development/organization/companies/22222222-2222-2222-2222-222222222222", Json(
            """{"legalName":"X","taxRegion":"Mainland","baseCurrency":"EUR","isActive":true,"postingLockDate":"ontem"}"""))
            .ConfigureAwait(false);
        var badLockDatePayload = await badLockDate.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, duplicateCompany.StatusCode);
        StringAssert.Contains(duplicateCompanyPayload, "NIF DEMO-PT-0001");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRegion.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, duplicateUser.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownCompany.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badLockDate.StatusCode);
        StringAssert.Contains(badLockDatePayload, "Data de bloqueio");
    }

    [TestMethod]
    public async Task TaxonomyCatalogEndpointServesTheFullPortariaLists()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();

        using var sResponse = await client.GetAsync("/api/v1/development/accounting/taxonomies/S").ConfigureAwait(false);
        var sPayload = await sResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var mResponse = await client.GetAsync("/api/v1/development/accounting/taxonomies/M").ConfigureAwait(false);

        using var oResponse = await client.GetAsync("/api/v1/development/accounting/taxonomies/O").ConfigureAwait(false);
        var oPayload = await oResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badResponse = await client.GetAsync("/api/v1/development/accounting/taxonomies/X").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, sResponse.StatusCode);
        StringAssert.Contains(sPayload, "\"reference\":\"S\"");
        StringAssert.Contains(sPayload, "302/2016");
        StringAssert.Contains(sPayload, "\"code\":1");
        StringAssert.Contains(sPayload, "Caixa");
        Assert.AreEqual(HttpStatusCode.OK, mResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, oResponse.StatusCode);
        StringAssert.Contains(oPayload, "\"code\":1");
        Assert.AreEqual(HttpStatusCode.NotFound, badResponse.StatusCode);
    }

    [TestMethod]
    public async Task JournalAndDocumentTypeMaintenanceRejectsInvalidChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string accounting = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting";

        static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        using var duplicateJournal = await client
            .PostAsync($"{accounting}/journals", Json("""{"code":"GERAL","name":"Duplicado"}"""))
            .ConfigureAwait(false);

        using var deactivatePostedJournal = await client
            .PutAsync($"{accounting}/journals/COMPRAS", Json("""{"name":"Diário de compras","isActive":false}"""))
            .ConfigureAwait(false);
        var deactivatePayload = await deactivatePostedJournal.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var duplicateDocument = await client
            .PostAsync($"{accounting}/documents", Json("""{"code":"FTF","name":"x","defaultJournalCode":"COMPRAS"}"""))
            .ConfigureAwait(false);

        using var unknownJournalForDocument = await client
            .PostAsync($"{accounting}/documents", Json("""{"code":"NOVOX","name":"x","defaultJournalCode":"NAOEXISTE"}"""))
            .ConfigureAwait(false);

        using var unknownCompany = await client
            .PostAsync(
                "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
                "companies/99999999-9999-9999-9999-999999999999/accounting/journals",
                Json("""{"code":"X","name":"x"}"""))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, duplicateJournal.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, deactivatePostedJournal.StatusCode);
        StringAssert.Contains(deactivatePayload, "tem movimentos");
        Assert.AreEqual(HttpStatusCode.BadRequest, duplicateDocument.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownJournalForDocument.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownCompany.StatusCode);
    }

    [TestMethod]
    public async Task SaftHeaderInspectionMatchesAnExistingCompanyByTaxNumber()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string userPath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/saft/inspect";

        using var matchResponse = await client
            .PostAsync(userPath, new StringContent(SaftDocument("DEMO-PT-0001", "I"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var matchPayload = await matchResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var proposeResponse = await client
            .PostAsync(userPath, new StringContent(SaftDocument("599999999", "F"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var proposePayload = await proposeResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var malformedResponse = await client
            .PostAsync(userPath, new StringContent("<nope/>", Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        using var unknownUserResponse = await client
            .PostAsync(
                "/api/v1/development/users/00000000-0000-0000-0000-000000000000/saft/inspect",
                new StringContent(SaftDocument("DEMO-PT-0001", "I"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, matchResponse.StatusCode);
        StringAssert.Contains(matchPayload, "\"outcome\":\"ExistingCompany\"");
        StringAssert.Contains(matchPayload, "Almeida");
        StringAssert.Contains(matchPayload, "\"accountingStandard\":\"SncGeneral\"");
        StringAssert.Contains(matchPayload, "\"includesAccounting\":true");
        Assert.AreEqual(HttpStatusCode.OK, proposeResponse.StatusCode);
        StringAssert.Contains(proposePayload, "\"outcome\":\"NewCompanyProposed\"");
        StringAssert.Contains(proposePayload, "\"includesAccounting\":false");
        Assert.AreEqual(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownUserResponse.StatusCode);
    }

    [TestMethod]
    public async Task SaftCompanyCreationRejectsDuplicatesAndUnknownUsers()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        static StringContent Body(string taxNumber, string region = "Mainland", string standard = "SncGeneral") =>
            new(
                $$"""
                {"taxRegistrationNumber":"{{taxNumber}}","legalName":"Empresa do Ficheiro, Lda.","taxRegion":"{{region}}","accountingStandard":"{{standard}}"}
                """,
                Encoding.UTF8,
                "application/json");

        using var duplicateResponse = await client
            .PostAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/saft/companies", Body("DEMO-PT-0001"))
            .ConfigureAwait(false);
        var duplicatePayload = await duplicateResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badRegionResponse = await client
            .PostAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/saft/companies", Body("599999901", region: "Nowhere"))
            .ConfigureAwait(false);
        var badRegionPayload = await badRegionResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknownUserResponse = await client
            .PostAsync("/api/v1/development/users/00000000-0000-0000-0000-000000000000/saft/companies", Body("599999902"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        StringAssert.Contains(duplicatePayload, "NIF DEMO-PT-0001");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRegionResponse.StatusCode);
        StringAssert.Contains(badRegionPayload, "Mainland, Azores");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownUserResponse.StatusCode);
    }

    [TestMethod]
    public async Task SaftAccountsPreviewClassifiesTheFileAccountsAgainstTheCompanyChart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string previewPath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/saft/accounts-preview";

        using var previewResponse = await client
            .PostAsync(previewPath, new StringContent(SaftDocumentWithAccounts("11", "6221", "ZZZ999"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var previewPayload = await previewResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var malformedResponse = await client
            .PostAsync(previewPath, new StringContent("<AuditFile><broken", Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        using var noAccountsResponse = await client
            .PostAsync(previewPath, new StringContent(SaftDocument("DEMO-PT-0001", "F"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var noAccountsPayload = await noAccountsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var forbiddenResponse = await client
            .PostAsync(
                "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
                "companies/99999999-9999-9999-9999-999999999999/saft/accounts-preview",
                new StringContent(SaftDocumentWithAccounts("11"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        StringAssert.Contains(previewPayload, "\"fileAccountCount\":3");
        StringAssert.Contains(previewPayload, "\"existingCount\":2");
        StringAssert.Contains(previewPayload, "\"newCount\":1");
        StringAssert.Contains(previewPayload, "\"state\":\"New\"");
        StringAssert.Contains(previewPayload, "\"state\":\"Existing\"");
        StringAssert.Contains(previewPayload, "ZZZ999");
        Assert.AreEqual(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, noAccountsResponse.StatusCode);
        StringAssert.Contains(noAccountsPayload, "\"fileAccountCount\":0");
        Assert.AreEqual(HttpStatusCode.NotFound, forbiddenResponse.StatusCode);

        const string entriesPath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/saft/entries-preview";
        using var entriesResponse = await client
            .PostAsync(entriesPath, new StringContent(SaftDocumentWithEntries(), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var entriesPayload = await entriesResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, entriesResponse.StatusCode);
        StringAssert.Contains(entriesPayload, "\"hasEntries\":true");
        StringAssert.Contains(entriesPayload, "\"transactionCount\":2");
        StringAssert.Contains(entriesPayload, "\"balanced\":true");
        StringAssert.Contains(entriesPayload, "\"code\":\"COMPRAS\"");
        StringAssert.Contains(entriesPayload, "\"exists\":true");
        StringAssert.Contains(entriesPayload, "\"code\":\"NOVO\"");
        StringAssert.Contains(entriesPayload, "\"exists\":false");
    }

    private static string SaftDocument(string taxNumber, string basis) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01">
          <Header>
            <AuditFileVersion>1.04_01</AuditFileVersion>
            <TaxRegistrationNumber>{taxNumber}</TaxRegistrationNumber>
            <TaxAccountingBasis>{basis}</TaxAccountingBasis>
            <CompanyName>Empresa do Ficheiro, Lda.</CompanyName>
            <FiscalYear>2026</FiscalYear>
            <StartDate>2026-01-01</StartDate>
            <EndDate>2026-12-31</EndDate>
            <CurrencyCode>EUR</CurrencyCode>
          </Header>
        </AuditFile>
        """;

    private static string SaftDocumentWithAccounts(params string[] codes)
    {
        var accounts = string.Concat(codes.Select(code =>
            $"<Account><AccountID>{code}</AccountID><AccountDescription>Conta {code}</AccountDescription></Account>"));
        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01">
          <Header>
            <AuditFileVersion>1.04_01</AuditFileVersion>
            <TaxRegistrationNumber>DEMO-PT-0001</TaxRegistrationNumber>
            <TaxAccountingBasis>I</TaxAccountingBasis>
            <CompanyName>Empresa do Ficheiro, Lda.</CompanyName>
            <FiscalYear>2026</FiscalYear>
            <StartDate>2026-01-01</StartDate>
            <EndDate>2026-12-31</EndDate>
            <CurrencyCode>EUR</CurrencyCode>
          </Header>
          <MasterFiles>
            <GeneralLedgerAccounts>{accounts}</GeneralLedgerAccounts>
          </MasterFiles>
        </AuditFile>
        """;
    }

    private static string SaftDocumentWithEntries() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01">
          <Header><AuditFileVersion>1.04_01</AuditFileVersion></Header>
          <MasterFiles />
          <GeneralLedgerEntries>
            <NumberOfEntries>2</NumberOfEntries>
            <TotalDebit>300.00</TotalDebit>
            <TotalCredit>300.00</TotalCredit>
            <Journal>
              <JournalID>COMPRAS</JournalID>
              <Description>Diário de compras</Description>
              <Transaction TransactionID="C 1">
                <TransactionDate>2026-02-10</TransactionDate>
                <Lines>
                  <DebitLine><AccountID>6221</AccountID><DebitAmount><Amount>200.00</Amount></DebitAmount></DebitLine>
                  <CreditLine><AccountID>2211001</AccountID><CreditAmount><Amount>200.00</Amount></CreditAmount></CreditLine>
                </Lines>
              </Transaction>
            </Journal>
            <Journal>
              <JournalID>NOVO</JournalID>
              <Description>Diário só do ficheiro</Description>
              <Transaction TransactionID="N 1">
                <TransactionDate>2026-09-01</TransactionDate>
                <Lines>
                  <DebitLine><AccountID>6221</AccountID><DebitAmount><Amount>100.00</Amount></DebitAmount></DebitLine>
                  <CreditLine><AccountID>2211001</AccountID><CreditAmount><Amount>100.00</Amount></CreditAmount></CreditLine>
                </Lines>
              </Transaction>
            </Journal>
          </GeneralLedgerEntries>
        </AuditFile>
        """;

    private sealed class FoundationApiFactory(
        string? connectionString = null,
        bool applyMigrations = false,
        bool seedDemoData = false,
        bool useConfiguredDatabase = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrationsOnStartup"] = applyMigrations.ToString(),
                    ["Database:SeedDemoData"] = seedDemoData.ToString()
                };
                if (!useConfiguredDatabase)
                {
                    settings["ConnectionStrings:Accounting"] =
                        connectionString ??
                        "Server=(localdb)\\MSSQLLocalDB;Database=AccountingAI_ApiTests;" +
                        "Integrated Security=true;TrustServerCertificate=true";
                }

                configuration.AddInMemoryCollection(settings);
            });
        }
    }
}
