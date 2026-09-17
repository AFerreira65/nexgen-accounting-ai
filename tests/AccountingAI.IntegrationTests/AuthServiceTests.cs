using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod]
    public async Task ValidatesCredentialsAndRejectsWrongPasswordsOrUnknownEmails()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Auth_{Guid.NewGuid():N}";
        var configured = Environment.GetEnvironmentVariable("ACCOUNTINGAI_TEST_SQL_CONNECTION");
        var builder = string.IsNullOrWhiteSpace(configured)
            ? new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            }
            : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = databaseName;

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure"))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Auth", "auth@test.invalid"));
            var inactiveId = Guid.NewGuid();
            var inactive = new UserAccount(inactiveId, "Inativo", "inativo@test.invalid");
            inactive.Deactivate();
            context.Add(inactive);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new AuthService(context);

            // Sem senha definida, ninguém entra.
            var beforePassword = await service
                .ValidateCredentialsAsync("auth@test.invalid", "qualquer-coisa", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNull(beforePassword);

            await service.SetPasswordAsync("Auth@Test.Invalid", "S3nha-Forte!", CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var correct = await service
                .ValidateCredentialsAsync("auth@test.invalid", "S3nha-Forte!", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(correct);
            Assert.AreEqual(userId, correct!.Id);
            Assert.AreEqual("Utilizador Auth", correct.DisplayName);

            var wrongPassword = await service
                .ValidateCredentialsAsync("auth@test.invalid", "senha-errada", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNull(wrongPassword);

            var unknownEmail = await service
                .ValidateCredentialsAsync("ninguem@test.invalid", "S3nha-Forte!", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNull(unknownEmail);

            await service.SetPasswordAsync("inativo@test.invalid", "S3nha-Forte!", CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var inactiveLogin = await service
                .ValidateCredentialsAsync("inativo@test.invalid", "S3nha-Forte!", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNull(inactiveLogin);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.SetPasswordAsync("ninguem@test.invalid", "S3nha-Forte!", CancellationToken.None));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
