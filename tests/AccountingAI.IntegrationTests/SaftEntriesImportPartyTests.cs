using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class SaftEntriesImportPartyTests
{
    [TestMethod]
    public async Task ImportLinksTheTransactionPartyToItsLines()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_SaftParty_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete SAF-T Terceiro", "gabinete-saft-terceiro"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa SAF-T Terceiro, Lda.", "ST-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador SAF-T Terceiro", "st@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            foreach (var (code, name) in new[] { ("21111", "Cliente A"), ("21112", "Cliente B"), ("22111", "Fornecedor"), ("7111", "Vendas"), ("6221", "Serviços"), ("1201", "Banco") })
            {
                context.Add(new LedgerAccount(Guid.NewGuid(), companyId, code, name));
            }

            var customerA = Party.Create(companyId, "PT", "500584095", "Cliente A, Lda.");
            var customerB = Party.Create(companyId, "PT", "509999999", "Cliente B, Lda.");
            context.AddRange(customerA, customerB);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var date = new DateOnly(2026, 2, 10);
            SaftTransaction Transaction(string id, string? customerId, string? supplierId, params SaftTransactionLine[] lines) =>
                new("VD", "Vendas", id, date, $"Transação {id}", id, lines, customerId, supplierId);

            var reader = new StubReader(new SaftTransactions(
                null, null, null,
                [
                    // O ficheiro liga o cliente C1 à conta 21111: só essa linha leva o terceiro.
                    Transaction("T1", "c1", null,
                        new SaftTransactionLine("21111", "Cliente", 123m, 0m),
                        new SaftTransactionLine("7111", "Venda", 0m, 123m)),
                    // C2 não tem conta no ficheiro: vale a linha da conta 21.
                    Transaction("T2", "C2", null,
                        new SaftTransactionLine("21112", "Cliente", 50m, 0m),
                        new SaftTransactionLine("7111", "Venda", 0m, 50m)),
                    // Fornecedor que ainda não existe na empresa.
                    Transaction("T3", null, "F1",
                        new SaftTransactionLine("6221", "Serviço", 80m, 0m),
                        new SaftTransactionLine("22111", "Fornecedor", 0m, 80m)),
                    // Consumidor final: não é terceiro nem conta como desconhecido.
                    Transaction("T4", "CF", null,
                        new SaftTransactionLine("1201", "Banco", 10m, 0m),
                        new SaftTransactionLine("7111", "Venda", 0m, 10m)),
                    // Sem terceiro.
                    Transaction("T5", null, null,
                        new SaftTransactionLine("1201", "Banco", 5m, 0m),
                        new SaftTransactionLine("7111", "Venda", 0m, 5m)),
                ],
                [
                    new SaftParty(SaftPartyKind.Customer, "C1", "21111", "500584095", "Cliente A, Lda.", "PT"),
                    new SaftParty(SaftPartyKind.Customer, "C2", null, "509 999 999", "Cliente B, Lda.", "Desconhecido"),
                    new SaftParty(SaftPartyKind.Supplier, "F1", "22111", "B12345678", "Proveedor, S.L.", "ES"),
                    new SaftParty(SaftPartyKind.Customer, "CF", null, "999999990", "Consumidor final", "PT"),
                ]));

            var service = new SaftEntriesImportService(reader, context);
            using var content = new MemoryStream();
            var result = await service.ImportAsync(userId, companyId, content, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(result);
            Assert.AreEqual(5, result!.ImportedCount);
            Assert.AreEqual(2, result.LinesWithParty);
            Assert.AreEqual(1, result.TransactionsWithUnknownParty);
            context.ChangeTracker.Clear();

            var lines = await context.JournalEntryLines
                .AsNoTracking()
                .Where(line => line.PartyId != null)
                .Select(line => new { line.JournalEntry.Reference, line.Account.Code, line.PartyId })
                .ToListAsync()
                .ConfigureAwait(false);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(customerA.Id, lines.Single(line => line.Reference == "T1").PartyId);
            Assert.AreEqual("21111", lines.Single(line => line.Reference == "T1").Code);
            Assert.AreEqual(customerB.Id, lines.Single(line => line.Reference == "T2").PartyId);
            Assert.AreEqual("21112", lines.Single(line => line.Reference == "T2").Code);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private sealed class StubReader(SaftTransactions transactions) : ISaftEntriesReader
    {
        public Task<SaftEntriesSummary> ReadSummaryAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaftEntriesSummary(true, transactions.Items.Count, null, null, null, null, null, []));

        public Task<SaftTransactions> ReadTransactionsAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(transactions);
    }
}
