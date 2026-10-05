using NexGenAccountingAI.Application.Banking;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class BankStatementServiceTests
{
    private sealed class FakeReader(BankStatementFile file) : IBankStatementReader
    {
        public Task<BankStatementFile> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(file);
    }

    private static BankStatementRow Row(int line, int day, string description, decimal amount, decimal balance) =>
        new(line, new DateOnly(2026, 3, day), null, description, amount, balance, null, null);

    [TestMethod]
    public async Task ImportsAStatementReconcilesItAndExplainsTheBalance()
    {
        var connectionString = TestDatabase.ConnectionString("Bank");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Bancos", "gabinete-bancos"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Bancos, Lda.", "BK-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Bancos", "bancos@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "BANCOS", "Bancos"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("12011", "Banco A"), ("2111", "Clientes"), ("2211", "Fornecedores"), ("6878", "Serviços bancários"), ("56", "Resultados transitados"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            void Entry(string reference, int month, int day, string account, decimal bankAmount, EntrySource source = EntrySource.Manual)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, month, day), reference, $"Lançamento {reference}");
                entry.SetSource(source);
                entry.AddLine(Guid.NewGuid(), ids["12011"], "Banco", bankAmount > 0 ? bankAmount : 0m, bankAmount < 0 ? -bankAmount : 0m);
                entry.AddLine(Guid.NewGuid(), ids[account], "Contrapartida", bankAmount < 0 ? -bankAmount : 0m, bankAmount > 0 ? bankAmount : 0m);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            JournalEntry Entry2(string reference, int day, string account, decimal bankAmount)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 3, day), reference, $"Lançamento {reference}");
                entry.AddLine(Guid.NewGuid(), ids["12011"], "Banco", bankAmount > 0 ? bankAmount : 0m, bankAmount < 0 ? -bankAmount : 0m);
                entry.AddLine(Guid.NewGuid(), ids[account], "Contrapartida", bankAmount < 0 ? -bankAmount : 0m, bankAmount > 0 ? bankAmount : 0m);
                entry.Complete(JournalEntryStatus.Reviewed);
                return entry;
            }

            Entry("AB-1", 1, 1, "56", 10000m, EntrySource.Opening);
            Entry("B-1", 3, 2, "2111", 1230m);       // recebimento do cliente: no extrato no mesmo dia
            Entry("B-2", 3, 6, "2211", -123.45m);    // pagamento: o banco debitou um dia antes
            Entry("B-3", 3, 20, "2211", -500m);      // cheque emitido, ainda não descontado
            Entry("B-4", 3, 25, "2111", 1230m);      // outro recebimento igual, ainda não no extrato
            Entry("B-5", 3, 3, "6878", -7.50m);      // a comissão, lançada à mão
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var file = new BankStatementFile("CSV", "PT50000201231234567890154", 10000m, 11095.05m, new DateOnly(2026, 3, 5),
            [
                Row(5, 2, "TRF CLIENTE UM", 1230m, 11230m),
                Row(6, 3, "COMISSAO MANUTENCAO", -7.50m, 11222.50m),
                Row(7, 5, "PAG SERV EDP", -123.45m, 11099.05m),
                Row(8, 5, "CAFE", -2m, 11097.05m),
                Row(9, 5, "CAFE", -2m, 11095.05m),
                new BankStatementRow(10, null, null, "Linha partida", 1m, null, null, "Data ilegível ('x')."),
            ], []);
            var service = new BankStatementService(new FakeReader(file), context);

            var preview = await service.ImportAsync(userId, companyId, "12011", Stream.Null, "marco.csv", dryRun: true, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNull(preview!.ImportId);
            Assert.AreEqual(5, preview.Imported, "Os dois cafés iguais são dois movimentos.");
            Assert.AreEqual(1, preview.Invalid);
            Assert.AreEqual(0, await context.BankStatementLines.CountAsync().ConfigureAwait(false));

            var imported = await service.ImportAsync(userId, companyId, "12011", Stream.Null, "marco.csv", dryRun: false, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(5, imported!.Imported);
            var again = await service.ImportAsync(userId, companyId, "12011", Stream.Null, "marco.csv", dryRun: false, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, again!.Imported);
            Assert.AreEqual(5, again.AlreadyExisted, "Reimportar não duplica.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .ImportAsync(userId, companyId, "2111", Stream.Null, "x.csv", true, CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var from = new DateOnly(2026, 3, 1);
            var to = new DateOnly(2026, 3, 31);
            Assert.AreEqual(3, await service.AutoReconcileAsync(userId, companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false),
                "Recebimento, comissão e pagamento conciliam pelo valor e pela data; os cafés não têm lançamento.");
            context.ChangeTracker.Clear();

            var lines = await service.ListLinesAsync(userId, companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(lines);
            Assert.AreEqual("B-1", lines.Single(line => line.Description == "TRF CLIENTE UM").ReconciledEntries.Single().Reference, "O mais próximo na data, não o B-4.");
            Assert.AreEqual("B-2", lines.Single(line => line.Description == "PAG SERV EDP").ReconciledEntries.Single().Reference);
            Assert.IsTrue(lines.Single(line => line.Description == "PAG SERV EDP").ReconciledAutomatically);
            Assert.AreEqual(2, lines.Count(line => !line.Reconciled));

            // O mapa: 11 829,05 contabilístico − 4,00 só no banco − 730,00 só na contabilidade = 11 095,05 no extrato.
            var report = await service.ReconciliationAsync(userId, companyId, "12011", to, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(11829.05m, report!.BookBalance);
            Assert.AreEqual(11095.05m, report.StatementBalance);
            Assert.AreEqual(-4m, report.StatementOnlyTotal);
            CollectionAssert.AreEqual(new[] { "B-3", "B-4" }, report.BookOnly.Select(entry => entry.Reference).ToArray());
            Assert.AreEqual(0m, report.Difference);

            // À mão: desfazer e voltar a conciliar; valor diferente e lançamento já usado são recusados.
            var commission = lines.Single(line => line.Description == "COMISSAO MANUTENCAO");
            Assert.IsTrue(await service.UnreconcileAsync(userId, companyId, commission.Id, CancellationToken.None).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .ReconcileAsync(userId, companyId, commission.Id, "B-3", CancellationToken.None)).ConfigureAwait(false);
            var coffee = lines.First(line => line.Description == "CAFE");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .ReconcileAsync(userId, companyId, coffee.Id, "B-5", CancellationToken.None)).ConfigureAwait(false);
            var manual = await service.ReconcileAsync(userId, companyId, commission.Id, "b-5", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("B-5", manual!.ReconciledEntries.Single().Reference);
            Assert.IsFalse(manual.ReconciledAutomatically);
            context.ChangeTracker.Clear();

            // Anular um lançamento conciliado devolve o movimento a «por conciliar».
            await new JournalPostingService(context).VoidAsync(companyId, "B-2", CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var afterVoid = await service.ListLinesAsync(userId, companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(afterVoid!.Single(line => line.Description == "PAG SERV EDP").Reconciled);

            // Vários para um: os dois cafés (4,00) lançados num só lançamento.
            context.Add(Entry2("B-6", 5, "6878", -4m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var coffees = afterVoid!.Where(line => line.Description == "CAFE").Select(line => line.Id).ToList();
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .ReconcileGroupAsync(userId, companyId, [coffees[0]], ["B-6"], CancellationToken.None)).ConfigureAwait(false);
            var grouped = await service.ReconcileGroupAsync(userId, companyId, coffees, ["b-6"], CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(2, grouped!.Count);
            Assert.IsTrue(grouped.All(line => line.ReconciledLineCount == 2 && line.ReconciledEntries.Single().Reference == "B-6"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .ReconcileGroupAsync(userId, companyId, [coffees[0]], ["B-3"], CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Um para vários: o pagamento à EDP (123,45, o B-2 foi anulado) explicado por dois lançamentos.
            context.Add(Entry2("B-7", 6, "2211", -100m));
            context.Add(Entry2("B-8", 6, "2211", -23.45m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var edp = afterVoid!.Single(line => line.Description == "PAG SERV EDP").Id;
            var split = await service.ReconcileGroupAsync(userId, companyId, [edp], ["B-7", "B-8"], CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEquivalent(new[] { "B-7", "B-8" }, split!.Single().ReconciledEntries.Select(entry => entry.Reference).ToArray());
            context.ChangeTracker.Clear();
            var mapAfter = await service.ReconciliationAsync(userId, companyId, "12011", to, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, mapAfter!.StatementOnly.Count, "Todos os movimentos do extrato estão conciliados.");
            Assert.AreEqual(0m, mapAfter.Difference);

            // Desfazer um movimento do grupo desfaz o grupo inteiro.
            Assert.IsTrue(await service.UnreconcileAsync(userId, companyId, coffees[1], CancellationToken.None).ConfigureAwait(false));
            context.ChangeTracker.Clear();
            var ungrouped = await service.ListLinesAsync(userId, companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(ungrouped!.Where(line => line.Description == "CAFE").All(line => !line.Reconciled));
            Assert.AreEqual(0, await context.BankReconciliations.CountAsync(item => item.EntryReferences.Contains("B-6")).ConfigureAwait(false));

            var accounts = await service.ListAccountsAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            var bank = accounts!.Single();
            Assert.AreEqual("12011", bank.AccountCode);
            Assert.AreEqual(5, bank.LineCount);
            Assert.AreEqual("PT50000201231234567890154", bank.Iban);

            Assert.IsNull(await service.ListAccountsAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ReconciliationAsync(Guid.NewGuid(), companyId, "12011", to, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
