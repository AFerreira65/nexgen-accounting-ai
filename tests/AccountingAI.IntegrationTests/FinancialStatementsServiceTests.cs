using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class FinancialStatementsServiceTests
{
    [TestMethod]
    public void TheCatalogMapsTaxonomiesToStatementLines()
    {
        // Depósitos à ordem: saldo devedor é caixa, credor é financiamento obtido.
        var bank = StatementLineCatalog.Find("S", 2, "base");
        Assert.AreEqual("APC", bank!.Class);
        Assert.AreEqual("Caixa e depósitos bancários", bank.DebitLine);
        Assert.AreEqual("Financiamentos obtidos (corrente)", bank.CreditLine);

        // O mesmo código dá rubricas diferentes consoante o modelo.
        Assert.AreEqual("Imparidade de investimentos não depreciáveis/amortizáveis (perdas/reversões)", StatementLineCatalog.Find("S", 424, "base")!.DebitLine);
        Assert.AreEqual("Outras Imparidades (Perdas/Reversões)", StatementLineCatalog.Find("S", 424, "pe")!.DebitLine);
        Assert.AreEqual("Imparidades (Perdas/Reversões)", StatementLineCatalog.Find("S", 424, "me")!.DebitLine);

        Assert.IsNotNull(StatementLineCatalog.Find("M", 1, "me"));
        Assert.IsNull(StatementLineCatalog.Find("S", 99999, "base"));
        StringAssert.Contains(StatementLineCatalog.Source, "Autoridade Tributária");
    }

    [TestMethod]
    public async Task BuildsTheBalanceSheetAndTheIncomeStatement()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Statements_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Demonstrações", "gabinete-demonstracoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Demonstrações, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Demonstrações", "demonstracoes@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "DIVERSOS", "Diversos"));

            var ids = new Dictionary<string, Guid>();
            void Account(string code, string name, int? taxonomy)
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, taxonomy is null ? null : "S", taxonomy));
            }

            Account("1201", "Banco", 2);
            Account("21111", "Cliente A", 10);
            Account("21112", "Cliente B", 10);
            Account("311", "Compras de mercadorias", 156);
            Account("433", "Equipamento básico", 270);
            Account("4383", "Depreciações acumuladas", 276);
            Account("511", "Capital subscrito", 331);
            Account("56", "Resultados transitados", 338);
            Account("642", "Depreciações do período", 400);
            Account("7211", "Serviços", 507);
            Account("818", "Resultado líquido", 646);
            Account("278", "Conta sem taxonomia", null);

            var number = 0;
            JournalEntry Entry(DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit)[] lines)
            {
                var reference = $"DIVERSOS-{date.Year}-{++number:00000}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                entry.SetSource(source);
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                return entry;
            }

            // 2025: uma venda a pronto de 30.
            context.Add(Entry(new DateOnly(2025, 6, 1), EntrySource.Manual, ("1201", 30m, 0m), ("7211", 0m, 30m)));
            // 2026: abertura, compra de equipamento, venda a crédito, adiantamento de outro cliente,
            // compra de mercadorias (fica na conta 31), conta sem taxonomia, depreciação e apuramento.
            context.Add(Entry(new DateOnly(2026, 1, 1), EntrySource.Opening, ("1201", 1030m, 0m), ("511", 0m, 1000m), ("56", 0m, 30m)));
            context.Add(Entry(new DateOnly(2026, 2, 1), EntrySource.Manual, ("433", 500m, 0m), ("1201", 0m, 500m)));
            context.Add(Entry(new DateOnly(2026, 3, 10), EntrySource.Manual, ("21111", 200m, 0m), ("7211", 0m, 200m)));
            context.Add(Entry(new DateOnly(2026, 4, 2), EntrySource.Manual, ("1201", 40m, 0m), ("21112", 0m, 40m)));
            context.Add(Entry(new DateOnly(2026, 5, 2), EntrySource.Manual, ("311", 60m, 0m), ("1201", 0m, 60m)));
            context.Add(Entry(new DateOnly(2026, 6, 2), EntrySource.Manual, ("278", 5m, 0m), ("1201", 0m, 5m)));
            context.Add(Entry(new DateOnly(2026, 12, 31), EntrySource.Manual, ("642", 100m, 0m), ("4383", 0m, 100m)));
            context.Add(Entry(new DateOnly(2026, 12, 31), EntrySource.Closing, ("7211", 200m, 0m), ("642", 0m, 100m), ("818", 0m, 100m)));
            var voided = Entry(new DateOnly(2026, 7, 1), EntrySource.Manual, ("1201", 999m, 0m), ("7211", 0m, 999m));
            voided.Void();
            context.Add(voided);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new FinancialStatementsService(context);
            var balance = await service.BalanceSheetAsync(userId, companyId, new DateOnly(2026, 12, 31), CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(balance);
            Assert.AreEqual("SNC — modelo geral", balance!.Model);
            Assert.AreEqual(new DateOnly(2025, 12, 31), balance.ComparativeDate);
            static StatementLine Line(IReadOnlyList<StatementLine> lines, string section, string label) =>
                lines.Single(line => line.Section == section && line.Label == label);

            // As depreciações acumuladas abatem ao ativo; o banco fica com 1030 − 500 + 40 − 60 − 5.
            Assert.AreEqual(400m, Line(balance.Lines, "Ativo não corrente", "Ativos fixos tangíveis").Amount);
            Assert.AreEqual(200m, Line(balance.Lines, "Ativo corrente", "Clientes").Amount);
            var cash = Line(balance.Lines, "Ativo corrente", "Caixa e depósitos bancários");
            Assert.AreEqual(505m, cash.Amount);
            Assert.AreEqual(30m, cash.Comparative);
            // O cliente com saldo credor passa para o passivo, em vez de abater aos clientes.
            Assert.AreEqual(40m, Line(balance.Lines, "Passivo corrente", "Adiantamentos de clientes").Amount);
            Assert.AreEqual(1000m, Line(balance.Lines, "Capital próprio", "Capital Subscrito").Amount);
            Assert.AreEqual(30m, Line(balance.Lines, "Capital próprio", "Resultados transitados").Amount);
            // O resultado é o mesmo depois do apuramento; o anulado não conta.
            var result = Line(balance.Lines, "Capital próprio", "Resultado líquido do período");
            Assert.AreEqual(100m, result.Amount);
            Assert.AreEqual(30m, result.Comparative);
            Assert.IsTrue(Line(balance.Lines, "Ativo corrente", "Total do ativo corrente").IsTotal);

            Assert.AreEqual(1105m, balance.TotalAssets);
            Assert.AreEqual(1130m, balance.TotalEquity);
            Assert.AreEqual(40m, balance.TotalLiabilities);
            // As duas contas que não cabem no balanço explicam a diferença.
            Assert.AreEqual(-65m, balance.Difference);
            CollectionAssert.AreEqual(new[] { "278", "311" }, balance.Excluded.Select(item => item.AccountCode).ToArray());
            Assert.AreEqual(65m, balance.Excluded.Sum(item => item.Balance));
            StringAssert.Contains(balance.Excluded[0].Reason, "Sem código de taxonomia");

            var income = await service
                .IncomeStatementAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(income);
            var sales = Line(income!.Lines, string.Empty, "Vendas e serviços prestados");
            Assert.AreEqual(200m, sales.Amount, "O apuramento não entra na demonstração de resultados.");
            Assert.AreEqual(30m, sales.Comparative);
            Assert.AreEqual(-100m, Line(income.Lines, string.Empty, "Gastos / reversões de depreciação e de amortização").Amount);
            Assert.AreEqual(200m, Line(income.Lines, string.Empty, "Resultado antes de depreciações, gastos de financiamento e impostos").Amount);
            Assert.AreEqual(100m, Line(income.Lines, string.Empty, "Resultado antes de impostos").Amount);
            var net = Line(income.Lines, string.Empty, "Resultado líquido do período");
            Assert.AreEqual(100m, net.Amount);
            Assert.AreEqual(30m, net.Comparative);
            Assert.AreEqual(100m, income.NetResult);
            Assert.AreEqual(0, income.Excluded.Count, "As compras são do balanço, não dos resultados.");

            Assert.IsNull(await service.BalanceSheetAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 31), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
