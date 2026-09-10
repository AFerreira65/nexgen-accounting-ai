using AccountingAI.Application.At;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class CompanyAtCredentialsServiceTests
{
    [TestMethod]
    public async Task StoresThePasswordEncryptedAndNeverReturnsIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_AtCred_{Guid.NewGuid():N}";
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

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete AT", "gabinete-at"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa AT, Lda.", "AT-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador AT", "at@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var protector = new PrefixProtector();
            var service = new CompanyAtCredentialsService(context, protector);

            var before = await service.GetAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(before);
            Assert.IsFalse(before!.Configured);

            var saved = await service
                .SaveAsync(userId, companyId, "500584095/1", "senha-forte", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsTrue(saved!.Configured);
            Assert.AreEqual("500584095/1", saved.SubUser);

            // A senha guardada está cifrada — nunca em texto.
            var stored = await context.CompanyAtCredentials
                .AsNoTracking()
                .SingleAsync(item => item.CompanyId == companyId)
                .ConfigureAwait(false);
            StringAssert.StartsWith(stored.EncryptedPassword, "enc:v1:");
            Assert.AreNotEqual("senha-forte", stored.EncryptedPassword);
            Assert.AreEqual("senha-forte", protector.Unprotect(stored.EncryptedPassword));

            // Um valor já cifrado é guardado tal como está (não se volta a cifrar).
            var reSaved = await service
                .SaveAsync(userId, companyId, "500584095/2", "enc:v1:ja-cifrado", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual("500584095/2", reSaved!.SubUser);
            var reStored = await context.CompanyAtCredentials
                .AsNoTracking().SingleAsync(item => item.CompanyId == companyId).ConfigureAwait(false);
            Assert.AreEqual("enc:v1:ja-cifrado", reStored.EncryptedPassword);

            Assert.IsTrue(await service.DeleteAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false));
            Assert.IsFalse((await service.GetAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false))!.Configured);

            // Sem acesso.
            Assert.IsNull(await service.GetAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.SaveAsync(Guid.NewGuid(), companyId, "x/1", "y", CancellationToken.None).ConfigureAwait(false));
            Assert.IsFalse(await service.DeleteAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private sealed class PrefixProtector : IAtSecretProtector
    {
        private const string Prefix = "enc:v1:";

        public bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => Prefix + plaintext;

        public string Unprotect(string? value) =>
            IsProtected(value) ? value![Prefix.Length..] : value ?? string.Empty;
    }
}
