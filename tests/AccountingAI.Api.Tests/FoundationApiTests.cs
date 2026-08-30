using System.Net;
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
        StringAssert.Contains(payload, "F1-04B");
        StringAssert.Contains(payload, "ready");
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

        Assert.AreEqual(HttpStatusCode.OK, mainlandResponse.StatusCode);
        StringAssert.Contains(mainlandPayload, "COMP-2026-00842");
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
