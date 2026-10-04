using System.Net;
using System.Net.Http.Json;
using System.Text;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexGenAccountingAI.Tests.Shared;

namespace NexGenAccountingAI.Api.Tests;

/// <summary>
/// Exercita o fluxo real de sessão (login por cookie, <c>/auth/me</c>,
/// logout) numa base PostgreSQL descartável — ao contrário de
/// <see cref="FoundationApiTests"/>, esta fábrica NÃO substitui a
/// autenticação por um esquema de teste, porque é precisamente isso que se
/// quer testar aqui (ver ADR-0011).
/// </summary>
[TestClass]
public sealed class AuthApiTests
{
    [TestMethod]
    public async Task MeIsUnauthorizedWithoutASession()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task LoginWithAWrongPasswordIsRejectedAndDoesNotAuthenticate()
    {
        await using var factory = new AuthApiFactory();
        var userId = await factory.SeedUserAsync("carla@test.invalid", "Carla Autenticação", "S3nha-Forte!").ConfigureAwait(false);
        using var client = factory.CreateClient();

        using var loginResponse = await client
            .PostAsJsonAsync("/api/v1/auth/login", new { email = "carla@test.invalid", password = "senha-errada" })
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        using var meResponse = await client.GetAsync("/api/v1/auth/me").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meResponse.StatusCode);
        _ = userId;
    }

    [TestMethod]
    public async Task LoginThenMeThenLogoutRoundTripsAndRouteUserIdIsEnforced()
    {
        await using var factory = new AuthApiFactory();
        var userId = await factory.SeedUserAsync("duarte@test.invalid", "Duarte Autenticação", "S3nha-Forte!").ConfigureAwait(false);
        using var client = factory.CreateClient();

        using var loginResponse = await client
            .PostAsJsonAsync("/api/v1/auth/login", new { email = "duarte@test.invalid", password = "S3nha-Forte!" })
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);

        using var meResponse = await client.GetAsync("/api/v1/auth/me").ConfigureAwait(false);
        var mePayload = await meResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
        StringAssert.Contains(mePayload, userId.ToString());
        StringAssert.Contains(mePayload, "duarte@test.invalid");

        // Autenticado como `userId`, uma rota com o `userId` de outra pessoa é 403.
        using var otherUserResponse = await client
            .GetAsync($"/api/v1/development/work-context/{Guid.NewGuid()}")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Forbidden, otherUserResponse.StatusCode);

        // A própria rota, com o `userId` certo, funciona.
        using var ownWorkContextResponse = await client
            .GetAsync($"/api/v1/development/work-context/{userId}")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, ownWorkContextResponse.StatusCode);

        using var logoutResponse = await client.PostAsync("/api/v1/auth/logout", new StringContent("", Encoding.UTF8)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, logoutResponse.StatusCode);

        using var afterLogoutResponse = await client.GetAsync("/api/v1/auth/me").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, afterLogoutResponse.StatusCode);
    }

    private sealed class AuthApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString =
            "Host=localhost;Port=5432;Username=accounting;Password=accounting;Database=acc_authapi_" + Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // UseSetting, não ConfigureAppConfiguration: o Program.cs lê a ligação ao montar os
            // serviços, antes de as fontes acrescentadas pelo WebApplicationFactory entrarem — com
            // ConfigureAppConfiguration o teste escrevia (e apagava) a base de desenvolvimento.
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("Database:SeedDemoData", "false");
            builder.UseSetting("ConnectionStrings:Accounting", _connectionString);
        }

        public async Task<Guid> SeedUserAsync(string email, string displayName, string password)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, displayName, email));
            await context.SaveChangesAsync().ConfigureAwait(false);
            await auth.SetPasswordAsync(email, password).ConfigureAwait(false);
            return userId;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            await PostgresTestDatabaseDropper.DropAsync(_connectionString).ConfigureAwait(false);
        }
    }
}
