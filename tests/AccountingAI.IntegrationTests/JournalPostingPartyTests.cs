using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class JournalPostingPartyTests
{
    [TestMethod]
    public async Task PostsRevisesAndReviewsLinesWithParty()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_LineParty_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Linha", "gabinete-linha"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Linha, Lda.", "LP-PT-0001", TaxRegion.Mainland));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "VENDAS", "Vendas"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "21111", "Cliente A"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "7111", "Vendas"));

            var customer = Party.Create(companyId, "PT", "500584095", "Cliente A, Lda.");
            customer.SetRoles(isCustomer: true, isSupplier: false);
            context.Add(customer);
            var foreign = Party.Create(companyId, "ES", "B12345678", "Cliente B, S.L.");
            context.Add(foreign);
            var inactive = Party.Create(companyId, "PT", "509999999", "Inativo, Lda.");
            inactive.SetActive(false);
            context.Add(inactive);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new JournalPostingService(context);
            var date = new DateOnly(2026, 3, 10);

            // Lançar: só a linha de terceiros leva o terceiro.
            var posted = await service.PostAsync(
                companyId, "VENDAS", date, "FT 1/1", "Venda a crédito", reviewed: false,
                [
                    new JournalEntryLineInput("21111", "Cliente", 123m, 0m, " 500 584 095 "),
                    new JournalEntryLineInput("7111", "Venda", 0m, 123m),
                ]).ConfigureAwait(false);
            Assert.AreEqual("500584095", posted.Lines[0].PartyTaxNumber);
            Assert.AreEqual("PT", posted.Lines[0].PartyCountryCode);
            Assert.AreEqual("Cliente A, Lda.", posted.Lines[0].PartyName);
            Assert.IsNull(posted.Lines[1].PartyTaxNumber);

            context.ChangeTracker.Clear();
            var storedLine = await context.JournalEntryLines.SingleAsync(line => line.PartyId != null).ConfigureAwait(false);
            Assert.AreEqual(customer.Id, storedLine.PartyId);

            // Corrigir: troca para o terceiro estrangeiro (país indicado).
            var revised = await service.ReviseAsync(
                companyId, posted.Reference, "VENDAS", date, "FT 1/1", "Venda a crédito", reviewed: false,
                [
                    new JournalEntryLineInput("21111", "Cliente", 123m, 0m, "b12345678", "es"),
                    new JournalEntryLineInput("7111", "Venda", 0m, 123m),
                ]).ConfigureAwait(false);
            Assert.AreEqual("ES", revised.Lines[0].PartyCountryCode);
            Assert.AreEqual("Cliente B, S.L.", revised.Lines[0].PartyName);

            // Conferir devolve o terceiro lido da base.
            var reviewed = await service.MarkReviewedAsync(companyId, posted.Reference).ConfigureAwait(false);
            Assert.AreEqual("B12345678", reviewed.Lines[0].PartyTaxNumber);
            Assert.IsNull(reviewed.Lines[1].PartyName);

            // Terceiro desconhecido, inativo, e o mesmo NIF noutro país.
            var entriesBefore = await context.JournalEntries.CountAsync().ConfigureAwait(false);
            foreach (var (taxNumber, country) in new[] { ("123456789", "PT"), ("509999999", "PT"), ("500584095", "ES") })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PostAsync(
                    companyId, "VENDAS", date, "FT 1/2", "Venda", reviewed: false,
                    [
                        new JournalEntryLineInput("21111", "Cliente", 10m, 0m, taxNumber, country),
                        new JournalEntryLineInput("7111", "Venda", 0m, 10m),
                    ])).ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            Assert.AreEqual(entriesBefore, await context.JournalEntries.CountAsync().ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
