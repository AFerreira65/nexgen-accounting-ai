using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class CashFlowAndEquityStatementsTests
{
    [TestMethod]
    public async Task BuildsTheCashFlowAndTheEquityChangesStatements()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_CashEquity_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Fluxos", "gabinete-fluxos"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Fluxos, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Fluxos", "fluxos@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "DIVERSOS", "Diversos"));

            var ids = new Dictionary<string, Guid>();
            void Account(string code, string name, int taxonomy)
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", taxonomy));
            }

            Account("111", "Caixa", 1);
            Account("1201", "Banco", 2);
            Account("2211", "Fornecedor A", 37);
            Account("2432", "IVA dedutível", 73);
            Account("2433", "IVA liquidado", 72);
            Account("2511", "Empréstimo bancário", 87);
            Account("264", "Resultados atribuídos", 110);
            Account("433", "Equipamento básico", 270);
            Account("511", "Capital subscrito", 331);
            Account("551", "Reservas legais", 336);
            Account("56", "Resultados transitados", 338);
            Account("6221", "Trabalhos especializados", 356);
            Account("6911", "Juros suportados", 500);
            Account("7211", "Serviços", 507);
            Account("818", "Resultado líquido", 646);

            var number = 0;
            void Entry(DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit)[] lines)
            {
                var reference = $"DIVERSOS-{date.Year}-{++number:00000}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                entry.SetSource(source);
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // 2025: realização do capital e uma venda a pronto.
            Entry(new DateOnly(2025, 1, 10), EntrySource.Manual, ("1201", 1000m, 0m), ("511", 0m, 1000m));
            Entry(new DateOnly(2025, 6, 1), EntrySource.Manual, ("1201", 50m, 0m), ("7211", 0m, 50m));
            // 2026: abertura com o resultado de 2025 ainda na 818.
            Entry(new DateOnly(2026, 1, 1), EntrySource.Opening, ("1201", 1050m, 0m), ("511", 0m, 1000m), ("818", 0m, 50m));
            // Aplicação do resultado: 5 para reservas, 25 para transitados, 20 de dividendos — pagos depois.
            Entry(new DateOnly(2026, 3, 1), EntrySource.Manual, ("818", 50m, 0m), ("551", 0m, 5m), ("56", 0m, 25m), ("264", 0m, 20m));
            Entry(new DateOnly(2026, 3, 15), EntrySource.Manual, ("264", 20m, 0m), ("1201", 0m, 20m));
            // Venda a pronto com IVA: o IVA segue a rubrica da venda.
            Entry(new DateOnly(2026, 4, 1), EntrySource.Manual, ("111", 123m, 0m), ("7211", 0m, 100m), ("2433", 0m, 23m));
            // Fatura de fornecedor (sem caixa) e o seu pagamento.
            Entry(new DateOnly(2026, 4, 5), EntrySource.Manual, ("6221", 200m, 0m), ("2432", 46m, 0m), ("2211", 0m, 246m));
            Entry(new DateOnly(2026, 4, 20), EntrySource.Manual, ("2211", 246m, 0m), ("1201", 0m, 246m));
            // Empréstimo, compra de equipamento a pronto com IVA, juros.
            Entry(new DateOnly(2026, 5, 1), EntrySource.Manual, ("1201", 500m, 0m), ("2511", 0m, 500m));
            Entry(new DateOnly(2026, 6, 1), EntrySource.Manual, ("433", 300m, 0m), ("2432", 69m, 0m), ("1201", 0m, 369m));
            Entry(new DateOnly(2026, 7, 1), EntrySource.Manual, ("6911", 10m, 0m), ("1201", 0m, 10m));
            // Depósito do caixa no banco: não é fluxo.
            Entry(new DateOnly(2026, 8, 1), EntrySource.Manual, ("1201", 100m, 0m), ("111", 0m, 100m));
            // Pagamento de IVA ao Estado e aumento de capital.
            Entry(new DateOnly(2026, 9, 1), EntrySource.Manual, ("2433", 23m, 0m), ("1201", 0m, 23m));
            Entry(new DateOnly(2026, 10, 1), EntrySource.Manual, ("1201", 400m, 0m), ("511", 0m, 400m));
            // Apuramento: prejuízo de 110.
            Entry(new DateOnly(2026, 12, 31), EntrySource.Closing, ("7211", 100m, 0m), ("818", 110m, 0m), ("6221", 0m, 200m), ("6911", 0m, 10m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new FinancialStatementsService(context);
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 12, 31);

            var flows = await service.CashFlowsAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(flows);
            CashFlowLine Flow(string label) => flows!.Lines.Single(line => line.Label == label);

            var customers = Flow("Recebimentos de clientes");
            Assert.AreEqual(123m, customers.Amount, "O IVA da venda a pronto segue a venda.");
            Assert.AreEqual(50m, customers.Comparative);
            CollectionAssert.AreEqual(new[] { "2433", "7211" }, customers.Accounts.Select(account => account.AccountCode).ToArray());
            Assert.AreEqual(-246m, Flow("Pagamentos a fornecedores").Amount);
            Assert.AreEqual(-123m, Flow("Caixa gerada pelas operações").Amount);
            Assert.AreEqual(-23m, Flow("Outros recebimentos / pagamentos").Amount);
            Assert.AreEqual(-369m, Flow("Pagamentos — Ativos fixos tangíveis").Amount);
            Assert.AreEqual(500m, Flow("Recebimentos — Financiamentos obtidos").Amount);
            var capital = Flow("Recebimentos — Realizações de capital e de outros instrumentos de capital próprio");
            Assert.AreEqual(400m, capital.Amount);
            Assert.AreEqual(1000m, capital.Comparative);
            Assert.AreEqual(-10m, Flow("Pagamentos — Juros e gastos similares").Amount);
            Assert.AreEqual(-20m, Flow("Pagamentos — Dividendos").Amount);

            Assert.AreEqual(-146m, flows!.Operating);
            Assert.AreEqual(-369m, flows.Investing);
            Assert.AreEqual(870m, flows.Financing);
            Assert.AreEqual(355m, flows.NetChange);
            Assert.AreEqual(1050m, flows.OpeningCash);
            Assert.AreEqual(1405m, flows.ClosingCash);
            Assert.AreEqual(1050m, Flow("Caixa e seus equivalentes no fim do período").Comparative);
            Assert.AreEqual(0m, Flow("Caixa e seus equivalentes no início do período").Comparative);

            var equity = await service.EquityChangesAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(equity);
            CollectionAssert.AreEqual(
                new[] { "Capital Subscrito", "Reservas legais", "Resultados transitados", "Resultado líquido do período" },
                equity!.Columns.ToArray());
            EquityChangesRow Row(string label) => equity.Rows.Single(row => row.Label == label);

            CollectionAssert.AreEqual(new[] { 1000m, 0m, 0m, 50m }, Row("Posição no início do período").Amounts.ToArray());
            // O que foi para reservas e transitados é transferência; só os 20 de dividendos são distribuição.
            CollectionAssert.AreEqual(new[] { 0m, 5m, 25m, -30m }, Row("Aplicação de resultados e transferências entre rubricas").Amounts.ToArray());
            Assert.AreEqual(0m, Row("Aplicação de resultados e transferências entre rubricas").Total);
            CollectionAssert.AreEqual(new[] { 0m, 0m, 0m, -20m }, Row("Distribuições").Amounts.ToArray());
            Assert.AreEqual(-110m, Row("Resultado líquido do período").Total);
            Assert.AreEqual(-110m, Row("Resultado integral").Total);
            Assert.AreEqual(400m, Row("Realizações de capital e de prémios de emissão").Total);
            CollectionAssert.AreEqual(new[] { 1400m, 5m, 25m, -110m }, Row("Posição no fim do período").Amounts.ToArray());
            Assert.AreEqual(1320m, equity.ClosingEquity);

            // A posição final é o capital próprio do balanço, e o balanço fecha.
            var balance = await service.BalanceSheetAsync(userId, companyId, to, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(equity.ClosingEquity, balance!.TotalEquity);
            Assert.AreEqual(0m, balance.Difference);
            Assert.AreEqual(
                flows.ClosingCash,
                balance.Lines.Single(line => line.Label == "Caixa e depósitos bancários").Amount);

            // Um período que começa a meio: o que aconteceu antes é posição inicial.
            var second = await service.EquityChangesAsync(userId, companyId, new DateOnly(2026, 7, 1), to, CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEqual(
                new[] { 1000m, 5m, 25m, 100m - 200m },
                second!.Rows[0].Amounts.ToArray());
            Assert.AreEqual(1320m, second.ClosingEquity);

            Assert.IsNull(await service.CashFlowsAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.EquityChangesAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
