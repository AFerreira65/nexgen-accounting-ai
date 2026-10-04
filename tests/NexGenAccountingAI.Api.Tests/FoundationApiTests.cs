using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Api.Tests;

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
        StringAssert.Contains(payload, "F2-01");
        StringAssert.Contains(payload, "ready");
    }

    [TestMethod]
    public async Task WorkContextSettingsRejectAnUnknownTextSize()
    {
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
    public async Task MovementsCanBeRequestedAsAServerSidePage()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string movements = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/movements";

        using var firstPage = await client.GetAsync($"{movements}?page=1&pageSize=2").ConfigureAwait(false);
        var firstPayload = await firstPage.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var clampedPage = await client.GetAsync($"{movements}?page=999&pageSize=2").ConfigureAwait(false);
        var clampedPayload = await clampedPage.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var searched = await client
            .GetAsync($"{movements}?page=1&pageSize=25&search=COMP-2026-00842")
            .ConfigureAwait(false);
        var searchedPayload = await searched.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var journalMatch = await client
            .GetAsync($"{movements}?page=1&pageSize=25&journal=COMPRAS")
            .ConfigureAwait(false);
        var journalMatchPayload = await journalMatch.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var journalMiss = await client
            .GetAsync($"{movements}?page=1&pageSize=25&journal=BANCOS")
            .ConfigureAwait(false);
        var journalMissPayload = await journalMiss.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badDate = await client.GetAsync($"{movements}?page=1&from=07-2026").ConfigureAwait(false);

        using var wholeArray = await client.GetAsync(movements).ConfigureAwait(false);
        var wholeArrayPayload = await wholeArray.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, firstPage.StatusCode);
        StringAssert.Contains(firstPayload, "\"page\":1");
        StringAssert.Contains(firstPayload, "\"pageSize\":2");
        StringAssert.Contains(firstPayload, "\"totalItems\":3");
        StringAssert.Contains(firstPayload, "\"totalPages\":2");
        StringAssert.Contains(firstPayload, "\"items\":[");
        // A última página fica travada em 2, não sai fora do intervalo.
        StringAssert.Contains(clampedPayload, "\"page\":2");
        // A pesquisa server-side filtra pela referência do lançamento.
        StringAssert.Contains(searchedPayload, "COMP-2026-00842");
        StringAssert.Contains(searchedPayload, "\"totalItems\":1");
        // O filtro de diário é server-side e distingue os diários entre si.
        StringAssert.Contains(journalMatchPayload, "COMP-2026-00842");
        Assert.IsFalse(journalMissPayload.Contains("COMP-2026-00842", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.BadRequest, badDate.StatusCode);
        // Sem `page`, a resposta continua a ser o array completo (sem envelope).
        Assert.AreEqual(HttpStatusCode.OK, wholeArray.StatusCode);
        StringAssert.StartsWith(wholeArrayPayload.TrimStart(), "[");
        Assert.IsFalse(wholeArrayPayload.Contains("\"totalItems\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task JournalMovementCountsAreScopedToTheCompany()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";

        using var counts = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/accounting/journal-movement-counts")
            .ConfigureAwait(false);
        var countsPayload = await counts.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/accounting/journal-movement-counts")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, counts.StatusCode);
        // COMP-2026-00842 está no diário COMPRAS, por isso a contagem existe.
        StringAssert.Contains(countsPayload, "\"COMPRAS\":");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task ASingleMovementCanBeReadByReference()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string movements = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/" +
            "companies/22222222-2222-2222-2222-222222222222/accounting/movements";

        using var found = await client.GetAsync($"{movements}/COMP-2026-00842").ConfigureAwait(false);
        var foundPayload = await found.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var missing = await client.GetAsync($"{movements}/NAO-EXISTE").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        StringAssert.Contains(foundPayload, "\"id\":\"COMP-2026-00842\"");
        StringAssert.Contains(foundPayload, "\"lines\":[");
        StringAssert.Contains(foundPayload, "\"status\":\"Conferido\"");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [TestMethod]
    public async Task MovementsSummaryAggregatesTheWorkQueueWithoutTheFullList()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";

        using var summary = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/accounting/movements/summary")
            .ConfigureAwait(false);
        var summaryPayload = await summary.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/accounting/movements/summary")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, summary.StatusCode);
        StringAssert.Contains(summaryPayload, "\"total\":3");
        StringAssert.Contains(summaryPayload, "\"pendingReview\":1");
        StringAssert.Contains(summaryPayload, "\"voided\":");
        // Posição de IVA = 2433 liquidado (575) menos 2432 dedutível (276).
        StringAssert.Contains(summaryPayload, "\"vatPosition\":299");
        // A antevisão traz o lançamento por conferir, sem as linhas.
        StringAssert.Contains(summaryPayload, "BANC-2026-00196");
        Assert.IsFalse(summaryPayload.Contains("\"lines\"", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task TaxonomyReviewIsScopedToTheActiveCompanyAndClassifiesAccounts()
    {
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
        // Desde a F2-11A, um `userId` na rota diferente do da sessão autenticada
        // nunca chega ao handler — é sempre 403, exista ou não esse utilizador.
        Assert.AreEqual(HttpStatusCode.Forbidden, unknownUserResponse.StatusCode);
    }

    [TestMethod]
    public async Task SaftSchemaValidationEndpointReportsConformance()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string userPath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/saft/validate";

        using var wellFormed = await client
            .PostAsync(userPath, new StringContent(SaftDocument("500000000", "I"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var wellFormedPayload = await wellFormed.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var malformed = await client
            .PostAsync(userPath, new StringContent("<AuditFile><Header>", Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        using var unknownUser = await client
            .PostAsync(
                "/api/v1/development/users/00000000-0000-0000-0000-000000000000/saft/validate",
                new StringContent(SaftDocument("500000000", "I"), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, wellFormed.StatusCode);
        StringAssert.Contains(wellFormedPayload, "\"version\":\"1.04_01\"");
        // Sem o XSD oficial colocado (drop-in), valida-se só a boa formação.
        StringAssert.Contains(wellFormedPayload, "\"schemaAvailable\":false");
        StringAssert.Contains(wellFormedPayload, "\"schemaValid\":false");
        StringAssert.Contains(wellFormedPayload, "\"severity\":\"Warning\"");
        // XML mal formado é 400; XML válido mas não conforme seria 200.
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, unknownUser.StatusCode);
    }

    [TestMethod]
    public async Task SaftValidationEndpointRunsTheAnalyticalRules()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string userPath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/saft/validate";

        using var response = await client
            .PostAsync(userPath, new StringContent(SaftDocumentWithEntries(), Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // Cabeçalho sem NIF nem base contabilística, contas usadas fora do plano.
        StringAssert.Contains(payload, "\"ruleId\":\"NIF-CHECKDIGIT\"");
        StringAssert.Contains(payload, "\"ruleId\":\"BASIS-KNOWN\"");
        StringAssert.Contains(payload, "\"ruleId\":\"ACCOUNT-REF-RESOLVES\"");
        StringAssert.Contains(payload, "\"severity\":\"Error\"");
    }

    [TestMethod]
    public async Task EFaturaPurchasesCanBePreviewedAndListed()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string csv =
            "Setor;Fornecedor;Tipo;Situação;Número da fatura;Data de emissão;Valor tributável;Valor IVA;Valor total;NIF\n" +
            "Outros;Fornecedor A;FT;Registada;FT A/1;2026-07-01;100,00;23,00;123,00;500000001\n" +
            "Outros;Fornecedor B;FS;Registada;FS B/2;2026-07-02;50,00;11,50;61,50;500000002\n";

        using var preview = await client
            .PostAsync(
                $"{basePath}22222222-2222-2222-2222-222222222222/efatura/purchases-import?dryRun=true&fileName=t.csv",
                new StringContent(csv, Encoding.UTF8, "text/csv"))
            .ConfigureAwait(false);
        var previewPayload = await preview.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badCsv = await client
            .PostAsync(
                $"{basePath}22222222-2222-2222-2222-222222222222/efatura/purchases-import?dryRun=true",
                new StringContent(string.Empty, Encoding.UTF8, "text/csv"))
            .ConfigureAwait(false);

        using var list = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/efatura/purchases")
            .ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/efatura/purchases")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        StringAssert.Contains(previewPayload, "\"imported\":2");
        StringAssert.Contains(previewPayload, "\"invalid\":0");
        Assert.AreEqual(HttpStatusCode.BadRequest, badCsv.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.Contains(listPayload, "\"totalItems\"");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task SaftValidationHistoryIsScopedToTheCompany()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";

        using var reports = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/saft/validation-reports")
            .ConfigureAwait(false);
        var reportsPayload = await reports.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/saft/validation-reports")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, reports.StatusCode);
        StringAssert.StartsWith(reportsPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task CrossCheckReportIsScopedToTheCompanyAndNeedsAClosedPeriod()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string period = "?from=2026-08-01&to=2026-08-31";

        using var report = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/cross-check{period}")
            .ConfigureAwait(false);
        var reportPayload = await report.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var openPeriod = await client
            .GetAsync($"{basePath}22222222-2222-2222-2222-222222222222/cross-check")
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/cross-check{period}")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, report.StatusCode);
        StringAssert.Contains(reportPayload, "\"purchases\"");
        StringAssert.Contains(reportPayload, "\"efaturaWithoutAccounting\"");
        StringAssert.Contains(reportPayload, "\"sales\"");
        StringAssert.Contains(reportPayload, "\"totals\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, openPeriod.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task SaftCompanyCreationRejectsDuplicatesAndUnknownUsers()
    {
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
        Assert.AreEqual(HttpStatusCode.Forbidden, unknownUserResponse.StatusCode);
    }

    [TestMethod]
    public async Task ReportTemplateReturnsADefaultAndAcceptsAnUpdate()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string path = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222/report-templates/balancete";

        using var initial = await client.GetAsync(path).ConfigureAwait(false);
        var initialPayload = await initial.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var saved = await client
            .PutAsync(path, new StringContent(
                "{\"headerLines\":[\"Documento de trabalho\"],\"showFirmName\":true,\"showCompanyIdentity\":true,\"orientation\":\"Landscape\"}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var savedPayload = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknownKey = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222/report-templates/inexistente")
            .ConfigureAwait(false);

        // Repõe o default para não deixar estado no accountingAI.
        using var restore = await client
            .PutAsync(path, new StringContent(
                "{\"headerLines\":[],\"showFirmName\":true,\"showCompanyIdentity\":true,\"orientation\":\"Portrait\"}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode);
        StringAssert.Contains(initialPayload, "\"reportKey\":\"balancete\"");
        StringAssert.Contains(initialPayload, "\"companyName\"");
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        StringAssert.Contains(savedPayload, "Documento de trabalho");
        StringAssert.Contains(savedPayload, "\"orientation\":\"Landscape\"");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownKey.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
    }

    [TestMethod]
    public async Task AnalysisRunReturnsGroupedRowsAndTotals()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string body =
            "{\"definition\":{\"source\":\"movimentos\",\"columns\":[\"accountCode\",\"accountName\",\"debit\",\"credit\"]," +
            "\"filters\":[],\"groupBy\":[\"journalCode\"],\"sort\":[]," +
            "\"aggregates\":[{\"field\":\"debit\",\"function\":\"sum\"},{\"field\":\"credit\",\"function\":\"sum\"}]," +
            "\"includeGrandTotal\":true}}";

        using var run = await client
            .PostAsync(
                $"{basePath}22222222-2222-2222-2222-222222222222/analyses/run?from=2026-01-01&to=2026-12-31",
                new StringContent(body, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var runPayload = await run.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badDefinition = await client
            .PostAsync(
                $"{basePath}22222222-2222-2222-2222-222222222222/analyses/run",
                new StringContent("{\"definition\":{\"source\":\"movimentos\",\"columns\":[\"naoExiste\"],\"filters\":[],\"groupBy\":[],\"sort\":[],\"aggregates\":[],\"includeGrandTotal\":true}}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .PostAsync(
                $"{basePath}99999999-9999-9999-9999-999999999999/analyses/run",
                new StringContent(body, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, run.StatusCode);
        StringAssert.Contains(runPayload, "\"columns\"");
        StringAssert.Contains(runPayload, "\"groups\"");
        StringAssert.Contains(runPayload, "\"grandTotal\"");
        StringAssert.Contains(runPayload, "\"debit:sum\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDefinition.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task AnalysisCatalogAndListAreAvailable()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        using var catalog = await client
            .GetAsync("/api/v1/development/analyses/catalog")
            .ConfigureAwait(false);
        var catalogPayload = await catalog.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var list = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222/analyses")
            .ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/analyses")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, catalog.StatusCode);
        StringAssert.Contains(catalogPayload, "\"key\":\"Movimentos\"");
        StringAssert.Contains(catalogPayload, "\"key\":\"accountCode\"");
        StringAssert.Contains(catalogPayload, "\"operatorsByType\"");
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.StartsWith(listPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task ManagementReportCatalogAndDossierAreAvailable()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string company = "22222222-2222-2222-2222-222222222222";

        using var catalog = await client
            .GetAsync("/api/v1/development/management-reports/catalog")
            .ConfigureAwait(false);
        var catalogPayload = await catalog.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var dossier = await client
            .GetAsync($"{basePath}{company}/management-reports/dossier?kind=sintese-mensal&from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);
        var dossierPayload = await dossier.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var missingDates = await client
            .GetAsync($"{basePath}{company}/management-reports/dossier?kind=sintese-mensal")
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/management-reports/dossier?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, catalog.StatusCode);
        StringAssert.Contains(catalogPayload, "\"key\":\"sintese-mensal\"");
        StringAssert.Contains(catalogPayload, "\"sections\"");
        Assert.AreEqual(HttpStatusCode.OK, dossier.StatusCode);
        StringAssert.Contains(dossierPayload, "\"companyName\"");
        StringAssert.Contains(dossierPayload, "\"incomeSummary\"");
        StringAssert.Contains(dossierPayload, "\"ratios\"");
        StringAssert.Contains(dossierPayload, "\"crossCheck\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, missingDates.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task ManagementReportSettingsGuardGenerationAndHistoryIsReadable()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string company = "22222222-2222-2222-2222-222222222222";
        var settingsPath = $"{basePath}{company}/ai-settings";

        using var settings = await client.GetAsync(settingsPath).ConfigureAwait(false);
        var settingsPayload = await settings.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var disabledGenerate = await client
            .PostAsync(
                $"{basePath}{company}/management-reports/generate?from=2026-01-01&to=2026-12-31",
                new StringContent("{\"kind\":\"sintese-mensal\"}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var enable = await client
            .PutAsync(settingsPath, new StringContent(
                "{\"enabled\":true,\"autoMonthly\":false}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var enablePayload = await enable.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Ligado mas sem chave no servidor de teste → continua 409.
        using var noKeyGenerate = await client
            .PostAsync(
                $"{basePath}{company}/management-reports/generate?from=2026-01-01&to=2026-12-31",
                new StringContent("{\"kind\":\"sintese-mensal\"}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        // Repõe o default para não deixar estado no accountingAI.
        using var restore = await client
            .PutAsync(settingsPath, new StringContent(
                "{\"enabled\":false,\"autoMonthly\":false}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var missingDates = await client
            .PostAsync(
                $"{basePath}{company}/management-reports/generate",
                new StringContent("{\"kind\":\"sintese-mensal\"}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var emptyRequest = await client
            .PostAsync(
                $"{basePath}{company}/management-reports/generate?from=2026-01-01&to=2026-12-31",
                new StringContent("{}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var history = await client.GetAsync($"{basePath}{company}/management-reports").ConfigureAwait(false);
        var historyPayload = await history.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/ai-settings")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, settings.StatusCode);
        StringAssert.Contains(settingsPayload, "\"enabled\":false");
        StringAssert.Contains(settingsPayload, "\"narratorConfigured\":false");
        Assert.AreEqual(HttpStatusCode.Conflict, disabledGenerate.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        StringAssert.Contains(enablePayload, "\"enabled\":true");
        Assert.AreEqual(HttpStatusCode.Conflict, noKeyGenerate.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingDates.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, emptyRequest.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, history.StatusCode);
        StringAssert.StartsWith(historyPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task AtCertificateStatusIsReportedWithoutSecrets()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/development/at/certificate").ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        const string credentialsPath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222/at-credentials";
        using var credentials = await client.GetAsync(credentialsPath).ConfigureAwait(false);
        var credentialsPayload = await credentials.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(payload, "\"environment\":\"Testes\"");
        StringAssert.Contains(payload, "\"configured\":");
        StringAssert.Contains(payload, "\"subject\":");
        // A invariante que interessa: a senha e o material da chave nunca saem.
        Assert.IsFalse(payload.Contains("password", StringComparison.OrdinalIgnoreCase), "A senha nunca é devolvida.");
        Assert.IsFalse(payload.Contains("enc:v1:", StringComparison.Ordinal), "O valor cifrado nunca é devolvido.");

        Assert.AreEqual(HttpStatusCode.OK, credentials.StatusCode);
        StringAssert.Contains(credentialsPayload, "\"configured\":false");
        StringAssert.Contains(credentialsPayload, "\"periodicity\"");
        StringAssert.Contains(credentialsPayload, "\"environment\"");
        Assert.IsFalse(credentialsPayload.Contains("password", StringComparison.OrdinalIgnoreCase), "A senha das credenciais nunca é devolvida.");
    }

    [TestMethod]
    public async Task VatReturnIsComputedForTheClosedPeriod()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string company = "22222222-2222-2222-2222-222222222222";

        using var vat = await client
            .GetAsync($"{basePath}{company}/vat-return?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);
        var vatPayload = await vat.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var missingDates = await client
            .GetAsync($"{basePath}{company}/vat-return")
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/vat-return?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);

        using var saved = await client
            .GetAsync($"{basePath}{company}/vat-return/saved?from=2026-01-01&to=2026-03-31")
            .ConfigureAwait(false);
        var savedPayload = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var history = await client
            .GetAsync($"{basePath}{company}/vat-return/history")
            .ConfigureAwait(false);
        var historyPayload = await history.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, vat.StatusCode);
        StringAssert.Contains(vatPayload, "\"outputVat\"");
        StringAssert.Contains(vatPayload, "\"inputVat\"");
        StringAssert.Contains(vatPayload, "\"netPayable\"");
        StringAssert.Contains(vatPayload, "\"creditToCarryForward\"");
        StringAssert.Contains(vatPayload, "\"lines\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, missingDates.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        StringAssert.Contains(savedPayload, "\"saved\":false");
        StringAssert.Contains(savedPayload, "\"suggestedPriorCredit\"");
        StringAssert.Contains(savedPayload, "\"return\"");
        Assert.AreEqual(HttpStatusCode.OK, history.StatusCode);
        StringAssert.StartsWith(historyPayload.TrimStart(), "[");
    }

    [TestMethod]
    public async Task VatClassificationListsRelevantAccounts()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var list = await client.GetAsync($"{basePath}/vat-classification").ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badRate = await client
            .PutAsync($"{basePath}/vat-classification/7111", new StringContent(
                "{\"rate\":\"NaoExiste\",\"nature\":\"Nenhuma\",\"region\":\"Continente\"}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/vat-classification")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.StartsWith(listPayload.TrimStart(), "[");
        StringAssert.Contains(listPayload, "\"accountCode\"");
        StringAssert.Contains(listPayload, "\"rate\"");
        StringAssert.Contains(listPayload, "\"nature\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task PartiesListIsPagedAndValidatesInput()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var list = await client.GetAsync($"{basePath}/parties?pageSize=5").ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badRole = await client.GetAsync($"{basePath}/parties?role=NaoExiste").ConfigureAwait(false);

        // Um NIF português inválido é recusado antes de qualquer escrita.
        using var badTaxNumber = await client
            .PostAsync($"{basePath}/parties", new StringContent(
                "{\"countryCode\":\"PT\",\"taxRegistrationNumber\":\"123456780\",\"name\":\"Inválido\",\"isCustomer\":true,\"isSupplier\":false}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/parties")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.Contains(listPayload, "\"total\"");
        StringAssert.Contains(listPayload, "\"items\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badTaxNumber.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task Modelo22IsComputedFromTheLedger()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var result = await client.GetAsync($"{basePath}/modelo-22?year=2026").ConfigureAwait(false);
        var payload = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var ies = await client.GetAsync($"{basePath}/ies/annex-a?year=2026").ConfigureAwait(false);
        var iesPayload = await ies.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badIes = await client.GetAsync($"{basePath}/ies/annex-a?year=12").ConfigureAwait(false);
        using var withholdings = await client.GetAsync($"{basePath}/withholdings?year=2026").ConfigureAwait(false);
        var withholdingsPayload = await withholdings.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badWithholdings = await client.GetAsync($"{basePath}/withholdings?year=12").ConfigureAwait(false);
        using var dossier = await client.GetAsync($"{basePath}/fiscal-dossier?year=2026").ConfigureAwait(false);
        var dossierPayload = await dossier.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badDossier = await client.GetAsync($"{basePath}/fiscal-dossier?year=12").ConfigureAwait(false);
        using var aging = await client.GetAsync($"{basePath}/parties/aging?date=2026-12-31&kind=fornecedores").ConfigureAwait(false);
        var agingPayload = await aging.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badAging = await client.GetAsync($"{basePath}/parties/aging?date=2026-12-31&kind=outros").ConfigureAwait(false);
        using var catalog = await client.GetAsync($"{basePath}/modelo-22/fields").ConfigureAwait(false);
        var catalogPayload = await catalog.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badYear = await client.GetAsync($"{basePath}/modelo-22?year=12").ConfigureAwait(false);
        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/modelo-22?year=2026")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        StringAssert.Contains(payload, "\"code\":\"701\"");
        StringAssert.Contains(payload, "\"code\":\"367\"");
        Assert.AreEqual(HttpStatusCode.OK, catalog.StatusCode);
        StringAssert.Contains(catalogPayload, "\"number\":724");
        Assert.AreEqual(HttpStatusCode.OK, ies.StatusCode);
        StringAssert.Contains(iesPayload, "\"code\":\"A00158\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badIes.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, withholdings.StatusCode);
        StringAssert.Contains(withholdingsPayload, "\"totalWithheld\":");
        Assert.AreEqual(HttpStatusCode.BadRequest, badWithholdings.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, dossier.StatusCode);
        StringAssert.Contains(dossierPayload, "\"number\":12");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDossier.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, aging.StatusCode);
        StringAssert.Contains(agingPayload, "\"kind\":\"fornecedores\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badAging.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badYear.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task FixedAssetsAreListedAndScheduled()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var list = await client.GetAsync($"{basePath}/fixed-assets?includeInactive=true").ConfigureAwait(false);
        using var schedule = await client.GetAsync($"{basePath}/fixed-assets/schedule?year=2026").ConfigureAwait(false);
        var payload = await schedule.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badYear = await client.GetAsync($"{basePath}/fixed-assets/schedule?year=12").ConfigureAwait(false);
        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/fixed-assets")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, schedule.StatusCode);
        StringAssert.Contains(payload, "\"ledgerDepreciation\":");
        Assert.AreEqual(HttpStatusCode.BadRequest, badYear.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task VatDeclarationFillsTheOfficialFields()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var declaration = await client.GetAsync($"{basePath}/vat-declaration?from=2026-01-01&to=2026-03-31").ConfigureAwait(false);
        var payload = await declaration.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badPeriod = await client.GetAsync($"{basePath}/vat-declaration?from=2026-03-31&to=2026-01-01").ConfigureAwait(false);
        using var negative = await client.GetAsync($"{basePath}/vat-declaration?from=2026-01-01&to=2026-03-31&priorCredit=-1").ConfigureAwait(false);
        using var recapitulative = await client.GetAsync($"{basePath}/recapitulative-statement?from=2026-01-01&to=2026-03-31").ConfigureAwait(false);
        var recapitulativePayload = await recapitulative.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badRecapitulative = await client.GetAsync($"{basePath}/recapitulative-statement?from=2026-01-01").ConfigureAwait(false);
        using var settlementEntry = await client.GetAsync($"{basePath}/vat-settlement-entry?from=2026-01-01&to=2026-03-31").ConfigureAwait(false);
        var settlementEntryPayload = await settlementEntry.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badSettlementEntry = await client.GetAsync($"{basePath}/vat-settlement-entry?from=2026-03-31&to=2026-01-01").ConfigureAwait(false);
        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/vat-declaration?from=2026-01-01&to=2026-03-31")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, declaration.StatusCode);
        StringAssert.Contains(payload, "\"period\":\"03T\"");
        StringAssert.Contains(payload, "\"number\":93");
        StringAssert.Contains(payload, "Portaria n.º 221/2017");
        Assert.AreEqual(HttpStatusCode.BadRequest, badPeriod.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, recapitulative.StatusCode);
        StringAssert.Contains(recapitulativePayload, "\"field19\":");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRecapitulative.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, settlementEntry.StatusCode);
        StringAssert.Contains(settlementEntryPayload, "\"period\":\"03T\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badSettlementEntry.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task FinancialStatementsAreBuiltFromTheTaxonomies()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var balance = await client.GetAsync($"{basePath}/statements/balance-sheet?date=2026-12-31").ConfigureAwait(false);
        var balancePayload = await balance.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var income = await client.GetAsync($"{basePath}/statements/income-statement?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var incomePayload = await income.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badDate = await client.GetAsync($"{basePath}/statements/balance-sheet?date=31-12-2026").ConfigureAwait(false);
        using var badPeriod = await client.GetAsync($"{basePath}/statements/income-statement?from=2026-12-31&to=2026-01-01").ConfigureAwait(false);
        using var cash = await client.GetAsync($"{basePath}/statements/cash-flows?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var cashPayload = await cash.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var equity = await client.GetAsync($"{basePath}/statements/equity-changes?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var equityPayload = await equity.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var twoYears = await client.GetAsync($"{basePath}/statements/equity-changes?from=2025-01-01&to=2026-12-31").ConfigureAwait(false);
        using var evolution = await client.GetAsync($"{basePath}/management/monthly-evolution?year=2026").ConfigureAwait(false);
        var evolutionPayload = await evolution.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badYear = await client.GetAsync($"{basePath}/management/monthly-evolution?year=12").ConfigureAwait(false);
        using var structure = await client.GetAsync($"{basePath}/management/structure?year=2026").ConfigureAwait(false);
        var structurePayload = await structure.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var budget = await client.GetAsync($"{basePath}/budget?year=2026&month=6").ConfigureAwait(false);
        var budgetPayload = await budget.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badBudget = await client.GetAsync($"{basePath}/budget?year=2026&month=13").ConfigureAwait(false);
        using var badBudgetCopyBody = new StringContent("{\"source\":\"outro\",\"sourceYear\":2025}", System.Text.Encoding.UTF8, "application/json");
        using var badBudgetCopy = await client.PostAsync($"{basePath}/budget/copy?year=2026", badBudgetCopyBody).ConfigureAwait(false);
        using var suggested = await client.GetAsync($"{basePath}/suggested-entries?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var suggestedPayload = await suggested.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badSuggested = await client.GetAsync($"{basePath}/suggested-entries?from=2026-12-31&to=2026-01-01").ConfigureAwait(false);
        using var emptyFinalizeBody = new StringContent("{\"purchaseIds\":[]}", System.Text.Encoding.UTF8, "application/json");
        using var emptyFinalize = await client.PostAsync($"{basePath}/suggested-entries/finalize", emptyFinalizeBody).ConfigureAwait(false);
        using var emptyDismissBody = new StringContent("{\"purchaseIds\":[],\"reason\":\"Duplicado\"}", System.Text.Encoding.UTF8, "application/json");
        using var emptyDismiss = await client.PostAsync($"{basePath}/suggested-entries/dismiss", emptyDismissBody).ConfigureAwait(false);
        using var historyBody = new StringContent("{\"supplierTaxIds\":[\"500584095\"]}", System.Text.Encoding.UTF8, "application/json");
        using var history = await client.PostAsync($"{basePath}/suggested-entries/classification/history", historyBody).ConfigureAwait(false);
        using var aiStatus = await client.GetAsync($"{basePath}/suggested-entries/classification/ai-status").ConfigureAwait(false);
        using var badAcceptBody = new StringContent("{\"supplierTaxId\":\"500584095\",\"accountCode\":\"6221\",\"vatCode\":\"X\",\"supplierAccountCode\":\"2211\",\"journalCode\":\"GERAL\"}", System.Text.Encoding.UTF8, "application/json");
        using var badAccept = await client.PostAsync($"{basePath}/suggested-entries/classification/accept", badAcceptBody).ConfigureAwait(false);
        using var rules = await client.GetAsync($"{basePath}/supplier-entry-rules").ConfigureAwait(false);
        using var badRuleBody = new StringContent("{\"entryTemplateCode\":\"NAO-HA\"}", System.Text.Encoding.UTF8, "application/json");
        using var badRule = await client.PutAsync($"{basePath}/supplier-entry-rules/500584095", badRuleBody).ConfigureAwait(false);
        using var missingRule = await client.DeleteAsync($"{basePath}/supplier-entry-rules/500584095").ConfigureAwait(false);
        using var sales = await client.GetAsync($"{basePath}/sales-suggested-entries?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var salesPayload = await sales.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var salesRules = await client.GetAsync($"{basePath}/sales-entry-rules").ConfigureAwait(false);
        using var badSalesRuleBody = new StringContent("{\"entryTemplateCode\":\"NAO-HA\"}", System.Text.Encoding.UTF8, "application/json");
        using var badSalesRule = await client.PutAsync($"{basePath}/sales-entry-rules/23", badSalesRuleBody).ConfigureAwait(false);
        using var notSaftBody = new StringContent("isto não é XML", System.Text.Encoding.UTF8, "application/xml");
        using var notSaft = await client.PostAsync($"{basePath}/sales-documents/import?dryRun=true", notSaftBody).ConfigureAwait(false);
        using var bankAccounts = await client.GetAsync($"{basePath}/bank-accounts").ConfigureAwait(false);
        using var attachments = await client.GetAsync($"{basePath}/attachments?unlinked=true").ConfigureAwait(false);
        using var exeBody = new ByteArrayContent([1, 2, 3]);
        using var exeUpload = await client.PostAsync($"{basePath}/attachments?fileName=programa.exe", exeBody).ConfigureAwait(false);
        using var badQrBody = new StringContent("{\"qr\":\"https://exemplo.pt\"}", System.Text.Encoding.UTF8, "application/json");
        using var badQr = await client.PostAsync($"{basePath}/invoice-qr/lookup", badQrBody).ConfigureAwait(false);
        using var missingAttachment = await client.GetAsync($"{basePath}/attachments/{Guid.NewGuid()}/content").ConfigureAwait(false);
        using var bankRules = await client.GetAsync($"{basePath}/bank-entry-rules").ConfigureAwait(false);
        using var shortBankRuleBody = new StringContent("{\"pattern\":\"ab\",\"entryTemplateCode\":\"X\"}", System.Text.Encoding.UTF8, "application/json");
        using var shortBankRule = await client.PutAsync($"{basePath}/bank-entry-rules", shortBankRuleBody).ConfigureAwait(false);
        using var emptyBankPostBody = new StringContent("{\"lineIds\":[]}", System.Text.Encoding.UTF8, "application/json");
        using var emptyBankPost = await client.PostAsync($"{basePath}/bank-statements/post", emptyBankPostBody).ConfigureAwait(false);
        using var notBankBody = new StringContent("a;b\n1;2\n", System.Text.Encoding.UTF8, "text/csv");
        using var notBank = await client.PostAsync($"{basePath}/bank-statements/import?accountCode=12&dryRun=true", notBankBody).ConfigureAwait(false);
        using var wrongBankAccount = await client.GetAsync($"{basePath}/bank-statements/lines?accountCode=2111&from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        using var ownerReport = await client.GetAsync($"{basePath}/management/owner-report?year=2026&month=3").ConfigureAwait(false);
        var ownerReportPayload = await ownerReport.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badOwnerReport = await client.GetAsync($"{basePath}/management/owner-report?year=2026&month=0").ConfigureAwait(false);
        using var obligations = await client.GetAsync($"{basePath}/obligations?from=2026-05-01&to=2026-05-31").ConfigureAwait(false);
        var obligationsPayload = await obligations.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badObligationBody = new StringContent("{\"state\":\"Entregue\"}", System.Text.Encoding.UTF8, "application/json");
        using var badObligation = await client.PutAsync($"{basePath}/obligations/IES/2025", badObligationBody).ConfigureAwait(false);
        using var forecast = await client.GetAsync($"{basePath}/management/treasury-forecast?date=2026-03-31").ConfigureAwait(false);
        var forecastPayload = await forecast.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badForecast = await client.GetAsync($"{basePath}/management/treasury-forecast?date=2026-03-31&months=20").ConfigureAwait(false);
        using var map = await client.GetAsync($"{basePath}/management/operating-map?year=2026").ConfigureAwait(false);
        var mapPayload = await map.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var ratios = await client.GetAsync($"{basePath}/management/ratios?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var ratiosPayload = await ratios.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badRatios = await client.GetAsync($"{basePath}/management/ratios?from=2025-01-01&to=2026-12-31").ConfigureAwait(false);
        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/statements/balance-sheet?date=2026-12-31")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, balance.StatusCode);
        StringAssert.Contains(balancePayload, "\"label\":\"Total do ativo\"");
        StringAssert.Contains(balancePayload, "\"comparativeDate\":\"2025-12-31\"");
        Assert.AreEqual(HttpStatusCode.OK, income.StatusCode);
        StringAssert.Contains(incomePayload, "\"label\":\"Resultado antes de impostos\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badPeriod.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, cash.StatusCode);
        StringAssert.Contains(cashPayload, "\"label\":\"Caixa e seus equivalentes no fim do período\"");
        Assert.AreEqual(HttpStatusCode.OK, equity.StatusCode);
        StringAssert.Contains(equityPayload, "\"label\":\"Posição no fim do período\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, twoYears.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, evolution.StatusCode);
        StringAssert.Contains(evolutionPayload, "\"month\":12");
        Assert.AreEqual(HttpStatusCode.BadRequest, badYear.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, budget.StatusCode);
        StringAssert.Contains(budgetPayload, "\"month\":6");
        Assert.AreEqual(HttpStatusCode.BadRequest, badBudget.StatusCode);
        StringAssert.Contains(budgetPayload, "\"lines\":");
        Assert.AreEqual(HttpStatusCode.BadRequest, badBudgetCopy.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, structure.StatusCode);
        StringAssert.Contains(structurePayload, "\"topCustomers\":");
        Assert.AreEqual(HttpStatusCode.OK, suggested.StatusCode);
        StringAssert.Contains(suggestedPayload, "\"items\":");
        StringAssert.Contains(suggestedPayload, "\"readyCount\":");
        Assert.AreEqual(HttpStatusCode.BadRequest, badSuggested.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, emptyFinalize.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, emptyDismiss.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, history.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, aiStatus.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badAccept.StatusCode, "Com código de IVA, falta a conta de IVA.");
        Assert.AreEqual(HttpStatusCode.OK, rules.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badRule.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missingRule.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, sales.StatusCode);
        StringAssert.Contains(salesPayload, "\"percentagesWithoutRule\":");
        Assert.AreEqual(HttpStatusCode.OK, salesRules.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badSalesRule.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, notSaft.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, bankAccounts.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, attachments.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, exeUpload.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badQr.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missingAttachment.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, bankRules.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, shortBankRule.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, emptyBankPost.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, notBank.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongBankAccount.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, ownerReport.StatusCode);
        StringAssert.Contains(ownerReportPayload, "\"monthLabel\":\"Março de 2026\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badOwnerReport.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, obligations.StatusCode);
        StringAssert.Contains(obligationsPayload, "\"code\":\"MOD22\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badObligation.StatusCode, "Entregue sem data.");
        Assert.AreEqual(HttpStatusCode.OK, forecast.StatusCode);
        StringAssert.Contains(forecastPayload, "\"collectionDays\":30");
        Assert.AreEqual(HttpStatusCode.BadRequest, badForecast.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, map.StatusCode);
        StringAssert.Contains(mapPayload, "\"label\":\"Resultado líquido do período\"");
        Assert.AreEqual(HttpStatusCode.OK, ratios.StatusCode);
        StringAssert.Contains(ratiosPayload, "\"label\":\"Autonomia financeira\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRatios.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task SaftExportReportsAndRefusesAnInvalidCompany()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var report = await client.GetAsync($"{basePath}/saft/export-report?year=2026").ConfigureAwait(false);
        var reportPayload = await report.Content.ReadAsStringAsync().ConfigureAwait(false);
        // A empresa de demonstração não tem um NIF português válido: o ficheiro é recusado.
        using var export = await client.GetAsync($"{basePath}/saft/export?year=2026").ConfigureAwait(false);
        using var badYear = await client.GetAsync($"{basePath}/saft/export-report?year=12").ConfigureAwait(false);
        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/saft/export-report?year=2026")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, report.StatusCode);
        StringAssert.Contains(reportPayload, "\"canExport\":false");
        StringAssert.Contains(reportPayload, "\"blocking\":true");
        Assert.AreEqual(HttpStatusCode.Conflict, export.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badYear.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task CostCentersListReportAndValidateInput()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var list = await client.GetAsync($"{basePath}/cost-centers?includeInactive=true").ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var report = await client.GetAsync($"{basePath}/cost-centers/report?from=2026-01-01&to=2026-12-31").ConfigureAwait(false);
        var reportPayload = await report.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badDate = await client.GetAsync($"{basePath}/cost-centers/report?from=01-01-2026").ConfigureAwait(false);
        // Recusado antes de qualquer escrita: código com espaço.
        using var badCode = await client
            .PostAsync($"{basePath}/cost-centers", new StringContent("{\"code\":\"COM ESPACO\",\"name\":\"X\"}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.StartsWith(listPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.OK, report.StatusCode);
        StringAssert.Contains(reportPayload, "\"totalResult\"");
        StringAssert.Contains(reportPayload, "\"centers\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badCode.StatusCode);
    }

    [TestMethod]
    public async Task FiscalTablesReturnTheYearWithItsSources()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        using var current = await client.GetAsync("/api/v1/development/fiscal-tables/2026").ConfigureAwait(false);
        var payload = await current.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var notCovered = await client.GetAsync("/api/v1/development/fiscal-tables/2020").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, current.StatusCode);
        StringAssert.Contains(payload, "\"generalRate\":0.19");
        StringAssert.Contains(payload, "\"smeThreshold\":50000");
        StringAssert.Contains(payload, "\"status\":\"Oficial\"");
        StringAssert.Contains(payload, "\"status\":\"Secundaria\"");
        Assert.AreEqual(HttpStatusCode.NotFound, notCovered.StatusCode);
    }

    [TestMethod]
    public async Task PartyLedgerReturnsBalancesAndRejectsUnknownParties()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var balances = await client.GetAsync($"{basePath}/parties/balances?to=2026-12-31&pageSize=5").ConfigureAwait(false);
        var balancesPayload = await balances.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var badDate = await client.GetAsync($"{basePath}/parties/balances?to=31-12-2026").ConfigureAwait(false);
        using var unknown = await client
            .GetAsync($"{basePath}/parties/99999999-9999-9999-9999-999999999999/statement")
            .ConfigureAwait(false);
        using var byAccount = await client.GetAsync($"{basePath}/parties?accountCode=ZZ-NAO-HA").ConfigureAwait(false);
        var byAccountPayload = await byAccount.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, balances.StatusCode);
        StringAssert.Contains(balancesPayload, "\"totalBalance\"");
        StringAssert.Contains(balancesPayload, "\"items\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, byAccount.StatusCode);
        StringAssert.Contains(byAccountPayload, "\"total\":0");
    }

    [TestMethod]
    public async Task EntryTemplatesListAndValidateInput()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var list = await client.GetAsync($"{basePath}/entry-templates?includeInactive=true").ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Recusados antes de qualquer escrita: tipo de linha inválido e modelo sem contrapartida.
        using var badKind = await client
            .PutAsync($"{basePath}/entry-templates/ZZ-TESTE", new StringContent(
                "{\"name\":\"X\",\"journalCode\":\"COMPRAS\",\"isActive\":true,\"lines\":[{\"kind\":\"NaoExiste\",\"side\":\"Debit\",\"accountCode\":\"6221\"}]}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        using var noBalance = await client
            .PutAsync($"{basePath}/entry-templates/ZZ-TESTE", new StringContent(
                "{\"name\":\"X\",\"journalCode\":\"COMPRAS\",\"isActive\":true,\"lines\":[{\"kind\":\"Base\",\"side\":\"Debit\",\"accountCode\":\"6221\"}]}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        using var unknown = await client
            .PostAsync($"{basePath}/entry-templates/ZZ-NAO-HA/apply", new StringContent("{\"amount\":10}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.StartsWith(listPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, noBalance.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [TestMethod]
    public async Task VatCodesListAndValidateInput()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var list = await client.GetAsync($"{basePath}/vat-codes?includeInactive=true").ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Recusados antes de qualquer escrita: enum inválido e isento com percentagem.
        using var badRate = await client
            .PostAsync($"{basePath}/vat-codes", new StringContent(
                "{\"code\":\"X-1\",\"name\":\"X\",\"rate\":\"NaoExiste\",\"percentage\":23,\"nature\":\"TransmissoesInternas\",\"region\":\"Continente\"}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        using var exemptWithRate = await client
            .PostAsync($"{basePath}/vat-codes", new StringContent(
                "{\"code\":\"X-1\",\"name\":\"X\",\"rate\":\"Isento\",\"percentage\":6,\"nature\":\"OperacoesIsentasSemDeducao\",\"region\":\"Continente\"}",
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/vat-codes")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.StartsWith(listPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, exemptWithRate.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task MovementWithUnknownPartyIsRejectedBeforeWriting()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";
        const string body =
            "{\"journalCode\":\"COMPRAS\",\"entryDate\":\"2026-03-10\",\"documentReference\":\"TESTE\",\"description\":\"Terceiro desconhecido\"," +
            "\"reviewed\":false,\"lines\":[" +
            "{\"accountCode\":\"6221\",\"description\":\"A\",\"debit\":1,\"credit\":0,\"partyTaxNumber\":\"ZZ-NAO-EXISTE\",\"partyCountryCode\":\"ZZ\"}," +
            "{\"accountCode\":\"6221\",\"description\":\"B\",\"debit\":0,\"credit\":1}]}";

        using var response = await client
            .PostAsync($"{basePath}/accounting/movements", new StringContent(body, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknownVatCode = await client
            .PostAsync($"{basePath}/accounting/movements", new StringContent(
                body.Replace("\"partyTaxNumber\":\"ZZ-NAO-EXISTE\",\"partyCountryCode\":\"ZZ\"", "\"vatCode\":\"ZZ-NAO-HA\"", StringComparison.Ordinal),
                Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var vatPayload = await unknownVatCode.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(payload, "O terceiro ZZ ZZ-NAO-EXISTE");
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownVatCode.StatusCode);
        StringAssert.Contains(vatPayload, "ZZ-NAO-HA");
    }

    [TestMethod]
    public async Task PartiesImportPreviewsAnalyseWithoutWriting()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";
        const string saft =
            "<AuditFile><MasterFiles><Customer><CustomerID>C1</CustomerID><CustomerTaxID>999999990</CustomerTaxID>" +
            "<CompanyName>Consumidor final</CompanyName></Customer></MasterFiles></AuditFile>";

        using var saftPreview = await client
            .PostAsync($"{basePath}/saft/parties-preview", new StringContent(saft, Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var saftPayload = await saftPreview.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var malformed = await client
            .PostAsync($"{basePath}/saft/parties-preview", new StringContent("<AuditFile><MasterFiles>", Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        using var efaturaPreview = await client.PostAsync($"{basePath}/efatura/parties-preview", null).ConfigureAwait(false);
        var efaturaPayload = await efaturaPreview.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var linkPreview = await client
            .PostAsync($"{basePath}/saft/parties-link-preview", new StringContent(saft, Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var linkPayload = await linkPreview.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, linkPreview.StatusCode);
        StringAssert.Contains(linkPayload, "\"applied\":false");
        StringAssert.Contains(linkPayload, "\"linesLinked\":0");

        Assert.AreEqual(HttpStatusCode.OK, saftPreview.StatusCode);
        StringAssert.Contains(saftPayload, "\"applied\":false");
        StringAssert.Contains(saftPayload, "\"skippedCount\":1");
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, efaturaPreview.StatusCode);
        StringAssert.Contains(efaturaPayload, "\"applied\":false");
    }

    [TestMethod]
    public async Task VatSummaryReturnsRowsGroupedByNatureRateAndRegion()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var summary = await client
            .GetAsync($"{basePath}/vat-summary?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);
        var summaryPayload = await summary.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var missingRange = await client
            .GetAsync($"{basePath}/vat-summary")
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/vat-summary?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, summary.StatusCode);
        StringAssert.Contains(summaryPayload, "\"rows\"");
        StringAssert.Contains(summaryPayload, "\"unclassified\"");
        StringAssert.Contains(summaryPayload, "\"fieldMappingSource\"");
        StringAssert.Contains(summaryPayload, "\"fieldMappingDisclaimer\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, missingRange.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task IrcEstimateComputesFromNetIncomePlusAdjustments()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var estimate = await client
            .PostAsync($"{basePath}/irc-estimate?from=2026-01-01&to=2026-12-31", new StringContent(
                "{\"taxRate\":0.21,\"adjustments\":[{\"description\":\"Correção\",\"amount\":100}]}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var estimatePayload = await estimate.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var badRate = await client
            .PostAsync($"{basePath}/irc-estimate?from=2026-01-01&to=2026-12-31", new StringContent(
                "{\"taxRate\":0,\"adjustments\":[]}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var missingRange = await client
            .PostAsync($"{basePath}/irc-estimate", new StringContent(
                "{\"taxRate\":0.21,\"adjustments\":[]}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .PostAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/irc-estimate?from=2026-01-01&to=2026-12-31", new StringContent(
                "{\"taxRate\":0.21,\"adjustments\":[]}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, estimate.StatusCode);
        StringAssert.Contains(estimatePayload, "\"netIncome\"");
        StringAssert.Contains(estimatePayload, "\"estimatedTax\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingRange.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task IrcEstimateSavedAndHistoryAreExposedReadOnly()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222";

        using var saved = await client
            .GetAsync($"{basePath}/irc-estimate/saved?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);
        var savedPayload = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var history = await client
            .GetAsync($"{basePath}/irc-estimate/history")
            .ConfigureAwait(false);
        var historyPayload = await history.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Escrita inválida (taxa 0) — rejeitada antes de tocar na base.
        using var badRate = await client
            .PutAsync($"{basePath}/irc-estimate/saved?from=2026-01-01&to=2026-12-31", new StringContent(
                "{\"taxRate\":0,\"adjustments\":[]}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync("/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/99999999-9999-9999-9999-999999999999/irc-estimate/saved?from=2026-01-01&to=2026-12-31")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        StringAssert.Contains(savedPayload, "\"saved\"");
        Assert.AreEqual(HttpStatusCode.OK, history.StatusCode);
        StringAssert.StartsWith(historyPayload.TrimStart(), "[");
        Assert.AreEqual(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task DataDictionaryListsTablesAndDetailsColumns()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        using var list = await client
            .GetAsync("/api/v1/development/data-dictionary")
            .ConfigureAwait(false);
        var listPayload = await list.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var detail = await client
            .GetAsync("/api/v1/development/data-dictionary/accounting/Accounts")
            .ConfigureAwait(false);
        var detailPayload = await detail.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknown = await client
            .GetAsync("/api/v1/development/data-dictionary/accounting/NaoExiste")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.Contains(listPayload, "\"name\":\"Accounts\"");
        StringAssert.Contains(listPayload, "\"schema\":\"organization\"");
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        StringAssert.Contains(detailPayload, "\"columns\"");
        StringAssert.Contains(detailPayload, "\"primaryKey\":true");
        StringAssert.Contains(detailPayload, "organization.Companies");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [TestMethod]
    public async Task RecordInfoReportsAuditStampsAndGuardsTheTable()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string company = "22222222-2222-2222-2222-222222222222";

        using var account = await client
            .GetAsync($"{basePath}{company}/record-info?table=contas&id=6221")
            .ConfigureAwait(false);
        var accountPayload = await account.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var unknownTable = await client
            .GetAsync($"{basePath}{company}/record-info?table=inexistente&id=6221")
            .ConfigureAwait(false);

        using var unknownRecord = await client
            .GetAsync($"{basePath}{company}/record-info?table=contas&id=ZZZZZZ")
            .ConfigureAwait(false);

        using var missingParams = await client
            .GetAsync($"{basePath}{company}/record-info")
            .ConfigureAwait(false);

        using var unauthorized = await client
            .GetAsync($"{basePath}99999999-9999-9999-9999-999999999999/record-info?table=contas&id=6221")
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, account.StatusCode);
        StringAssert.Contains(accountPayload, "\"createdAtUtc\"");
        StringAssert.Contains(accountPayload, "\"table\":\"contas\"");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownTable.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownRecord.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingParams.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task SaftSalesInvoicesSummaryReadsTheFaturacaoBlock()
    {
        await using var factory = new FoundationApiFactory(
            applyMigrations: true,
            seedDemoData: true,
            useConfiguredDatabase: true);
        using var client = factory.CreateClient();

        const string basePath = "/api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/";
        const string xml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<AuditFile xmlns=\"urn:OECD:StandardAuditFile-Tax:PT_1.04_01\"><Header><AuditFileVersion>1.04_01</AuditFileVersion></Header>" +
            "<SourceDocuments><SalesInvoices><NumberOfEntries>2</NumberOfEntries><TotalDebit>0.00</TotalDebit><TotalCredit>300.00</TotalCredit>" +
            "<Invoice><DocumentNumber>FT A/1</DocumentNumber><DocumentStatus><InvoiceStatus>N</InvoiceStatus></DocumentStatus>" +
            "<InvoiceDate>2026-08-04</InvoiceDate><InvoiceType>FT</InvoiceType>" +
            "<DocumentTotals><TaxPayable>23.00</TaxPayable><NetTotal>100.00</NetTotal><GrossTotal>123.00</GrossTotal></DocumentTotals></Invoice>" +
            "<Invoice><DocumentNumber>FT A/2</DocumentNumber><DocumentStatus><InvoiceStatus>N</InvoiceStatus></DocumentStatus>" +
            "<InvoiceDate>2026-09-11</InvoiceDate><InvoiceType>FT</InvoiceType>" +
            "<DocumentTotals><TaxPayable>46.00</TaxPayable><NetTotal>200.00</NetTotal><GrossTotal>246.00</GrossTotal></DocumentTotals></Invoice>" +
            "</SalesInvoices></SourceDocuments></AuditFile>";

        using var summary = await client
            .PostAsync(
                $"{basePath}22222222-2222-2222-2222-222222222222/saft/sales-invoices-summary",
                new StringContent(xml, Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);
        var summaryPayload = await summary.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var malformed = await client
            .PostAsync(
                $"{basePath}22222222-2222-2222-2222-222222222222/saft/sales-invoices-summary",
                new StringContent("<AuditFile><SourceDocuments><SalesInvoices><Invoice></SalesInvoices>", Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        using var unauthorized = await client
            .PostAsync(
                $"{basePath}99999999-9999-9999-9999-999999999999/saft/sales-invoices-summary",
                new StringContent(xml, Encoding.UTF8, "application/xml"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, summary.StatusCode);
        StringAssert.Contains(summaryPayload, "\"hasSalesInvoices\":true");
        StringAssert.Contains(summaryPayload, "\"invoiceCount\":2");
        StringAssert.Contains(summaryPayload, "\"net\":300");
        StringAssert.Contains(summaryPayload, "\"month\":8");
        StringAssert.Contains(summaryPayload, "\"month\":9");
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task SaftAccountsPreviewClassifiesTheFileAccountsAgainstTheCompanyChart()
    {
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
            // UseSetting, não ConfigureAppConfiguration: o Program.cs lê a ligação ao montar os
            // serviços, antes de as fontes acrescentadas pelo WebApplicationFactory entrarem.
            builder.UseSetting("Database:ApplyMigrationsOnStartup", applyMigrations.ToString());
            builder.UseSetting("Database:SeedDemoData", seedDemoData.ToString());
            if (!useConfiguredDatabase)
            {
                builder.UseSetting(
                    "ConnectionStrings:Accounting",
                    connectionString ??
                    "Host=localhost;Port=5432;Database=accounting_test;Username=accounting;Password=accounting");
            }

            // Desde a F2-11A, todo o pedido exige sessão autenticada (ADR-0011).
            // Os testes continuam a chamar `client.GetAsync(...)` diretamente,
            // sem fazer login — substitui-se o esquema de autenticação pelo
            // utilizador de demonstração fixo, o mesmo que todos os testes já
            // usam nas rotas (`33333333-...`).
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }
    }

    /// <summary>
    /// Autentica sempre como o utilizador de demonstração
    /// (<c>33333333-3333-3333-3333-333333333333</c>), o mesmo que todos os
    /// testes já assumem nas rotas — poupa cada teste de fazer login a sério.
    /// </summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "33333333-3333-3333-3333-333333333333"),
                new Claim(ClaimTypes.Name, "António Ferreira (Demonstração)"),
                new Claim(ClaimTypes.Email, "antonio.demo@accountingai.local"),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
