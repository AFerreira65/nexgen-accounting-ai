using AccountingAI.Application.Reporting;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class AnalysisRunnerTests
{
    [TestMethod]
    public async Task RunsFiltersGroupsAndTotalsForTheMovementsSource()
    {
        var connectionString = TestDatabase.ConnectionString("Runner");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Runner", "gabinete-runner"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Runner, Lda.", "RUNNER-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Runner", "runner@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "COMPRAS", "Diário de Compras"));
            var expense = Guid.NewGuid();
            var supplier = Guid.NewGuid();
            var otherExpense = Guid.NewGuid();
            context.Add(new LedgerAccount(expense, companyId, "6221", "Material de escritório"));
            context.Add(new LedgerAccount(otherExpense, companyId, "6262", "Comunicação"));
            context.Add(new LedgerAccount(supplier, companyId, "2211001", "Fornecedor A"));

            context.Add(Entry(companyId, journalId, "C-1", new DateOnly(2026, 3, 4), "FT 1",
                (expense, 100m, 0m), (supplier, 0m, 100m)));
            context.Add(Entry(companyId, journalId, "C-2", new DateOnly(2026, 3, 20), "FT 2",
                (expense, 250m, 0m), (supplier, 0m, 250m)));
            context.Add(Entry(companyId, journalId, "C-3", new DateOnly(2026, 4, 2), "FT 3",
                (otherExpense, 40m, 0m), (supplier, 0m, 40m)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var runner = new AnalysisRunner(context, new AccountLedgerService(context));

            var definition = new AnalysisDefinition(
                AnalysisSource.Movimentos,
                ["accountCode", "accountName", "entryDate", "debit"],
                [new AnalysisFilter("debit", "gt", "0")],
                ["accountCode"],
                [new AnalysisSort("entryDate", Descending: false)],
                [new AnalysisAggregate("debit", AnalysisAggregateFunction.Sum), new AnalysisAggregate("debit", AnalysisAggregateFunction.Count)],
                IncludeGrandTotal: true);

            var result = await runner
                .RunAsync(userId, companyId, definition, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(result);
            Assert.AreEqual(3, result!.RowCount, "Três linhas de débito (uma por lançamento).");
            Assert.IsFalse(result.Truncated);
            CollectionAssert.AreEqual(new[] { "accountCode", "accountName", "entryDate", "debit" }, result.Columns.Select(c => c.Key).ToArray());

            // Um grupo por conta: 6221 (100+250) e 6262 (40).
            Assert.AreEqual(2, result.Groups.Count);
            var g6221 = result.Groups.Single(g => (string)g.Keys[0].Value! == "6221");
            Assert.AreEqual(350m, g6221.Subtotals["debit:sum"]);
            Assert.AreEqual(2m, g6221.Subtotals["debit:count"]);
            var g6262 = result.Groups.Single(g => (string)g.Keys[0].Value! == "6262");
            Assert.AreEqual(40m, g6262.Subtotals["debit:sum"]);

            Assert.AreEqual(390m, result.GrandTotal["debit:sum"]);
            Assert.AreEqual(3m, result.GrandTotal["debit:count"]);

            // Filtro por período mais estreito: só março.
            var march = await runner
                .RunAsync(userId, companyId, definition, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(2, march!.RowCount);
            Assert.AreEqual(350m, march.GrandTotal["debit:sum"]);

            // Origem SaldosDeConta: agrupar por classe, somar o saldo.
            var balances = new AnalysisDefinition(
                AnalysisSource.SaldosDeConta,
                ["accountCode", "accountName", "class", "balance"],
                [],
                ["class"],
                [],
                [new AnalysisAggregate("balance", AnalysisAggregateFunction.Sum)],
                IncludeGrandTotal: true);

            var byClass = await runner
                .RunAsync(userId, companyId, balances, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(byClass);
            Assert.AreEqual(3, byClass!.RowCount, "Três contas de movimento com saldo.");
            var class6 = byClass.Groups.Single(g => (string)g.Keys[0].Value! == "6");
            Assert.AreEqual(390m, class6.Subtotals["balance:sum"]);
            var class2 = byClass.Groups.Single(g => (string)g.Keys[0].Value! == "2");
            Assert.AreEqual(-390m, class2.Subtotals["balance:sum"]);
            Assert.AreEqual(0m, byClass.GrandTotal["balance:sum"]);

            // Definição inválida → ArgumentException.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => runner.RunAsync(
                userId, companyId, definition with { Columns = ["naoExiste"] }, null, null, CancellationToken.None));

            // Sem acesso → null.
            Assert.IsNull(await runner.RunAsync(Guid.NewGuid(), companyId, definition, null, null, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private static JournalEntry Entry(
        Guid companyId, Guid journalId, string reference, DateOnly date, string document,
        params (Guid AccountId, decimal Debit, decimal Credit)[] lines)
    {
        var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, document, $"Lançamento {reference}");
        foreach (var line in lines)
        {
            entry.AddLine(Guid.NewGuid(), line.AccountId, "Linha", line.Debit, line.Credit);
        }

        entry.Complete(JournalEntryStatus.Reviewed);
        return entry;
    }
}
