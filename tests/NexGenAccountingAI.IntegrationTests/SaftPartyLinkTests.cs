using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class SaftPartyLinkTests
{
    [TestMethod]
    public async Task LinksPartiesToEntriesImportedBeforeThePartiesExisted()
    {
        var connectionString = TestDatabase.ConnectionString("PartyLink");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Ligação", "gabinete-ligacao"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Ligação, Lda.", "LG-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Ligação", "ligacao@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            foreach (var (code, name) in new[] { ("21111", "Cliente A"), ("22111", "Fornecedor"), ("7111", "Vendas"), ("6221", "Serviços") })
            {
                context.Add(new LedgerAccount(Guid.NewGuid(), companyId, code, name));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var date = new DateOnly(2026, 2, 10);
            static SaftTransaction Transaction(string id, DateOnly date, string? customerId, string? supplierId, params SaftTransactionLine[] lines) =>
                new("VD", "Vendas", id, date, $"Transação {id}", id, lines, customerId, supplierId);

            SaftParty[] fileParties =
            [
                new(SaftPartyKind.Customer, "C1", "21111", "500584095", "Cliente A, Lda.", "PT"),
                new(SaftPartyKind.Supplier, "F1", "22111", "B12345678", "Proveedor, S.L.", "ES"),
            ];
            var sale = Transaction("T1", date, "C1", null, new SaftTransactionLine("21111", "Cliente", 123m, 0m), new SaftTransactionLine("7111", "Venda", 0m, 123m));
            var secondSale = Transaction("T2", date, "C1", null, new SaftTransactionLine("21111", "Cliente", 50m, 0m), new SaftTransactionLine("7111", "Venda", 0m, 50m));
            var purchase = Transaction("T3", date, null, "F1", new SaftTransactionLine("6221", "Serviço", 80m, 0m), new SaftTransactionLine("22111", "Fornecedor", 0m, 80m));

            // 1. Importa-se sem terceiros na empresa: nenhuma linha fica ligada.
            using var content = new MemoryStream();
            var imported = await new SaftEntriesImportService(new StubReader(new SaftTransactions(null, null, null, [sale, secondSale, purchase], fileParties)), context)
                .ImportAsync(userId, companyId, content, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(3, imported!.ImportedCount);
            Assert.AreEqual(0, imported.LinesWithParty);
            Assert.AreEqual(3, imported.TransactionsWithUnknownParty);
            context.ChangeTracker.Clear();

            // 2. Cria-se o cliente (o fornecedor continua por criar).
            var customer = Party.Create(companyId, "PT", "500584095", "Cliente A, Lda.");
            context.Add(customer);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // O ficheiro relido: T2 com outro valor (o lançamento "foi corrigido") e T9 nunca importada.
            var changedSale = secondSale with { Lines = [new SaftTransactionLine("21111", "Cliente", 55m, 0m), new SaftTransactionLine("7111", "Venda", 0m, 55m)] };
            var neverImported = Transaction("T9", date, "C1", null, new SaftTransactionLine("21111", "Cliente", 1m, 0m), new SaftTransactionLine("7111", "Venda", 0m, 1m));
            var noParty = Transaction("T8", date, null, null, new SaftTransactionLine("21111", "Cliente", 1m, 0m), new SaftTransactionLine("7111", "Venda", 0m, 1m));
            var service = new SaftEntriesImportService(
                new StubReader(new SaftTransactions(null, null, null, [sale, changedSale, purchase, neverImported, noParty], fileParties)), context);

            // 3. A análise não escreve.
            var preview = await service.LinkPartiesAsync(userId, companyId, content, apply: false, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(preview);
            Assert.IsFalse(preview!.Applied);
            Assert.AreEqual(4, preview.TransactionCount, "Só contam as transações com cliente ou fornecedor.");
            Assert.AreEqual(2, preview.EntriesMatched);
            Assert.AreEqual(1, preview.EntriesChanged);
            Assert.AreEqual(1, preview.EntriesNotFound);
            Assert.AreEqual(1, preview.LinesLinked);
            Assert.AreEqual(1, preview.TransactionsWithUnknownParty);
            Assert.AreEqual(0, await context.JournalEntryLines.CountAsync(line => line.PartyId != null).ConfigureAwait(false));

            // 4. A ligação preenche só a linha da conta do cliente em T1.
            var applied = await service.LinkPartiesAsync(userId, companyId, content, apply: true, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(applied!.Applied);
            Assert.AreEqual(1, applied.LinesLinked);
            context.ChangeTracker.Clear();
            var linked = await context.JournalEntryLines
                .AsNoTracking()
                .Where(line => line.PartyId != null)
                .Select(line => new { line.JournalEntry.Reference, line.Account.Code, line.PartyId, line.Debit })
                .SingleAsync()
                .ConfigureAwait(false);
            Assert.AreEqual("T1", linked.Reference);
            Assert.AreEqual("21111", linked.Code);
            Assert.AreEqual(customer.Id, linked.PartyId);
            Assert.AreEqual(123m, linked.Debit, "Os valores não mudam.");

            // 5. Repetir não liga nada de novo.
            var again = await service.LinkPartiesAsync(userId, companyId, content, apply: true, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, again!.LinesLinked);
            Assert.AreEqual(1, again.LinesAlreadyLinked);

            Assert.IsNull(await service.LinkPartiesAsync(Guid.NewGuid(), companyId, content, false, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
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
