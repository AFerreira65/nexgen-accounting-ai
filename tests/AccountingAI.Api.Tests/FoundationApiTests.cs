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
        StringAssert.Contains(payload, "F1-02C");
        StringAssert.Contains(payload, "ready");
    }

    private sealed class FoundationApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Accounting"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=AccountingAI_ApiTests;" +
                        "Integrated Security=true;TrustServerCertificate=true",
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["Database:SeedDemoData"] = "false"
                });
            });
        }
    }
}
