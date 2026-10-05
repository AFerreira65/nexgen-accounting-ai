using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>
/// Os valores que o motor de mapas recebe: a abertura, os meses e o apuramento de cada
/// conta, por exercício, lidos da base de dados.
/// </summary>
[TestClass]
public sealed class MapDataProviderTests
{
    [TestMethod]
    public async Task ReadsOpeningMonthsAndClosingPerAccountAndYear()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("MapData"), TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Mapas", "gabinete-mapas"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Mapas, Lda.", "MP-PT-0001", TaxRegion.Mainland));
            var otherCompanyId = Guid.NewGuid();
            context.Add(new Company(otherCompanyId, firmId, "Outra Empresa, Lda.", "MP-PT-0002", TaxRegion.Mainland));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var otherJournalId = Guid.NewGuid();
            context.Add(new AccountingJournal(otherJournalId, otherCompanyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("111", "Caixa"), ("511", "Capital"), ("561", "Resultados transitados"), ("12", "Depósitos à ordem"),
                ("6221", "Trabalhos especializados"), ("7211", "Prestações de serviços"), ("811", "Resultado antes de impostos"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var otherCash = Guid.NewGuid();
            context.Add(new LedgerAccount(otherCash, otherCompanyId, "111", "Caixa"));
            var otherCapital = Guid.NewGuid();
            context.Add(new LedgerAccount(otherCapital, otherCompanyId, "511", "Capital"));

            var number = 0;
            JournalEntry Entry(DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit)[] lines)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, $"T-{++number}", date, $"D-{number}", "Lançamento");
                entry.SetSource(source);
                foreach (var (account, debit, credit) in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[account], "Linha", debit, credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
                return entry;
            }

            // 2024: capital 1 000, vendas 500, gastos 200; o apuramento veio de um SAF-T (não está marcado).
            Entry(new DateOnly(2024, 1, 2), EntrySource.Manual, ("111", 1000m, 0m), ("511", 0m, 1000m));
            Entry(new DateOnly(2024, 6, 1), EntrySource.SaftImport, ("111", 500m, 0m), ("7211", 0m, 500m));
            Entry(new DateOnly(2024, 7, 1), EntrySource.SaftImport, ("6221", 200m, 0m), ("111", 0m, 200m));
            Entry(new DateOnly(2024, 12, 31), EntrySource.SaftImport, ("7211", 500m, 0m), ("6221", 0m, 200m), ("811", 0m, 300m));
            // 2025: com abertura; uma venda em março e o apuramento da aplicação.
            Entry(new DateOnly(2025, 1, 1), EntrySource.Opening, ("111", 1300m, 0m), ("511", 0m, 1000m), ("561", 0m, 300m));
            Entry(new DateOnly(2025, 3, 10), EntrySource.Manual, ("111", 80m, 0m), ("7211", 0m, 80m));
            Entry(new DateOnly(2025, 12, 31), EntrySource.Closing, ("7211", 80m, 0m), ("811", 0m, 80m));
            // 2026: ainda sem abertura; um depósito em fevereiro e um lançamento anulado.
            Entry(new DateOnly(2026, 2, 5), EntrySource.Manual, ("12", 400m, 0m), ("111", 0m, 400m));
            Entry(new DateOnly(2026, 2, 6), EntrySource.Manual, ("111", 9999m, 0m), ("7211", 0m, 9999m)).Void();

            // Outra empresa: nunca entra.
            var foreign = new JournalEntry(Guid.NewGuid(), otherCompanyId, otherJournalId, "X-1", new DateOnly(2026, 2, 1), "X-1", "Outra");
            foreign.AddLine(Guid.NewGuid(), otherCash, "Linha", 7777m, 0m);
            foreign.AddLine(Guid.NewGuid(), otherCapital, "Linha", 0m, 7777m);
            foreign.Complete(JournalEntryStatus.Reviewed);
            context.Add(foreign);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var accounts = await new MapDataProvider(context).LoadAsync(companyId, [2024, 2025, 2026]).ConfigureAwait(false);
            MapYearAmounts Of(string code, int year) => accounts.Single(account => account.Code == code).Years[year];

            CollectionAssert.AreEqual(
                new[] { "111", "12", "511", "561", "6221", "7211", "811" },
                accounts.Select(account => account.Code).ToArray(),
                "Só as contas com valores, por ordem de código.");

            // 2024: o apuramento importado sai de dezembro e vai para o lugar do apuramento.
            Assert.AreEqual(500m, Of("7211", 2024).Credit[6]);
            Assert.AreEqual(0m, Of("7211", 2024).Debit[12]);
            Assert.AreEqual(500m, Of("7211", 2024).Debit[MapYearAmounts.Closing]);
            Assert.AreEqual(300m, Of("811", 2024).Credit[MapYearAmounts.Closing]);
            Assert.AreEqual(0m, Of("111", 2024).Debit[MapYearAmounts.Opening], "O primeiro exercício não tem nada para trás.");

            // 2025: a abertura está no seu lugar, não em janeiro.
            Assert.AreEqual(1300m, Of("111", 2025).Debit[MapYearAmounts.Opening]);
            Assert.AreEqual(0m, Of("111", 2025).Debit[1]);
            Assert.AreEqual(80m, Of("111", 2025).Debit[3]);
            Assert.AreEqual(80m, Of("7211", 2025).Debit[MapYearAmounts.Closing]);

            // 2026 sem abertura: as contas de balanço trazem o saldo desde a abertura de 2025
            // (sem contar 2024 outra vez); as de resultados começam a zero.
            Assert.AreEqual(1380m, Of("111", 2026).Debit[MapYearAmounts.Opening]);
            Assert.AreEqual(1000m, Of("511", 2026).Credit[MapYearAmounts.Opening]);
            Assert.IsFalse(accounts.Single(account => account.Code == "7211").Years.ContainsKey(2026), "O anulado não conta e os resultados não transitam.");
            Assert.AreEqual(400m, Of("111", 2026).Credit[2]);
            Assert.AreEqual(400m, Of("12", 2026).Debit[2]);

            // De ponta a ponta com o motor: um balancete clássico de fevereiro de 2026.
            var result = MapEngine.Run(
                new MapDefinition([new("L1", "Caixa", Accounts: "11"), new("L2", "Bancos", Accounts: "12")], MapColumnSets.Find(MapColumnSets.Classic)!.Columns),
                new MapRequest(2026, 2, 2),
                accounts);
            CollectionAssert.AreEqual(new decimal?[] { 1380m, 0m, 400m, 980m, 0m }, result.Rows[0].Values.ToArray());
            CollectionAssert.AreEqual(new decimal?[] { 0m, 400m, 0m, 400m, 0m }, result.Rows[1].Values.ToArray());

            Assert.AreEqual(0, (await new MapDataProvider(context).LoadAsync(Guid.NewGuid(), [2026]).ConfigureAwait(false)).Count);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
