using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>
/// O saldo inicial do balancete e da razão começa na abertura do exercício, e o
/// balancete pode sair sem o apuramento de resultados.
/// </summary>
[TestClass]
public sealed class TrialBalanceOpeningTests
{
    [TestMethod]
    public async Task OpeningBalanceStartsAtTheYearOpeningAndClosingCanBeLeftOut()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("TrialOpening"), TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Balancete", "gabinete-balancete"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Balancete, Lda.", "BL-PT-0001", TaxRegion.Mainland));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("111", "Caixa"), ("511", "Capital"), ("561", "Resultados transitados"),
                ("6221", "Trabalhos especializados"), ("7211", "Prestações de serviços"), ("811", "Resultado antes de impostos"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var number = 0;
            void Entry(DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit)[] lines)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, $"T-{++number}", date, $"D-{number}", "Lançamento");
                entry.SetSource(source);
                foreach (var (account, debit, credit) in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[account], "Linha", debit, credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // 2025: capital 1 000, vendas 500, gastos 200; apuramento a 31/12 (resultado 300).
            Entry(new DateOnly(2025, 1, 2), EntrySource.Manual, ("111", 1000m, 0m), ("511", 0m, 1000m));
            Entry(new DateOnly(2025, 6, 1), EntrySource.Manual, ("111", 500m, 0m), ("7211", 0m, 500m));
            Entry(new DateOnly(2025, 7, 1), EntrySource.Manual, ("6221", 200m, 0m), ("111", 0m, 200m));
            Entry(new DateOnly(2025, 12, 31), EntrySource.Closing, ("7211", 500m, 0m), ("6221", 0m, 200m), ("811", 0m, 300m));
            // 2026, ainda sem abertura: uma venda em fevereiro.
            Entry(new DateOnly(2026, 2, 1), EntrySource.Manual, ("111", 50m, 0m), ("7211", 0m, 50m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var ledger = new AccountLedgerService(context);
            var none = CancellationToken.None;

            // Sem abertura lançada em 2026: as contas de balanço trazem o saldo de trás;
            // as de resultados começam a zero (os 500 de vendas de 2025 não entram).
            var march = await ledger.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 1), null, none).ConfigureAwait(false);
            Assert.AreEqual(1350m, march.Rows.Single(row => row.AccountCode == "111").OpeningBalance, "1 000 + 500 − 200 de 2025, mais 50 de fevereiro.");
            Assert.AreEqual(-50m, march.Rows.Single(row => row.AccountCode == "7211").OpeningBalance, "Só a venda de fevereiro de 2026.");
            Assert.IsFalse(march.Rows.Any(row => row.AccountCode == "811"), "O resultado de 2025 não é saldo inicial de 2026 sem abertura.");

            // Com a abertura de 2026 lançada, o saldo inicial parte dela: antes contava a
            // caixa duas vezes (o fim de 2025 mais a abertura que o transporta).
            Entry(new DateOnly(2026, 1, 1), EntrySource.Opening, ("111", 1300m, 0m), ("511", 0m, 1000m), ("561", 0m, 300m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var opened = await ledger.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 1), null, none).ConfigureAwait(false);
            Assert.AreEqual(1350m, opened.Rows.Single(row => row.AccountCode == "111").OpeningBalance);
            Assert.AreEqual(-1000m, opened.Rows.Single(row => row.AccountCode == "511").OpeningBalance);
            Assert.AreEqual(0m, opened.TotalOpeningBalance, "A abertura equilibra.");
            var cash = await ledger.AccountLedgerAsync(companyId, "111", new DateOnly(2026, 3, 1), null, none).ConfigureAwait(false);
            Assert.AreEqual(1350m, cash!.OpeningBalance, "A razão segue a mesma regra.");

            // 2025 inteiro: com o apuramento, as classes 6 e 7 ficam a zero e o resultado está na 811;
            // sem ele, vê-se o gasto e o rendimento, e a 811 não aparece.
            var closed = await ledger.TrialBalanceAsync(companyId, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), none).ConfigureAwait(false);
            Assert.AreEqual(0m, closed.Rows.Single(row => row.AccountCode == "7211").Balance);
            Assert.AreEqual(-300m, closed.Rows.Single(row => row.AccountCode == "811").Balance);
            var beforeClosing = await ledger.TrialBalanceAsync(companyId, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), none, includeClosing: false).ConfigureAwait(false);
            Assert.AreEqual(-500m, beforeClosing.Rows.Single(row => row.AccountCode == "7211").Balance);
            Assert.AreEqual(200m, beforeClosing.Rows.Single(row => row.AccountCode == "6221").Balance);
            Assert.IsFalse(beforeClosing.Rows.Any(row => row.AccountCode == "811"));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
