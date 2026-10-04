using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class CashFlowAndEquityStatementsTests
{
    [TestMethod]
    public async Task BuildsTheCashFlowAndTheEquityChangesStatements()
    {
        var connectionString = TestDatabase.ConnectionString("CashEquity");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
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

            // Evolução mensal: movimentos do mês sem o apuramento, saldos no fim do mês.
            var evolution = await service.MonthlyEvolutionAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(evolution);
            Assert.AreEqual(12, evolution!.Months.Count);
            Assert.AreEqual(12, evolution.LastMonth);
            var april = evolution.Months[3];
            Assert.AreEqual(100m, april.Income);
            Assert.AreEqual(200m, april.Expenses);
            Assert.AreEqual(-100m, april.Result);
            Assert.AreEqual(1050m - 20m + 123m - 246m, april.Cash);
            Assert.AreEqual(0m, april.Payables, "A fatura do fornecedor foi paga no mesmo mês.");
            Assert.AreEqual(50m, evolution.Months[5].PreviousIncome, "A venda de junho de 2025.");
            var december = evolution.Months[11];
            Assert.AreEqual(0m, december.Income, "O apuramento não é rendimento nem gasto.");
            Assert.AreEqual(0m, december.Expenses);
            Assert.AreEqual(1405m, december.Cash);
            Assert.AreEqual(-110m, evolution.Months.Sum(month => month.Result));
            Assert.IsNull(await service.MonthlyEvolutionAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));

            // Mapa de exploração: a demonstração de resultados mês a mês.
            var map = await service.OperatingMapAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(map);
            Assert.AreEqual(10, map!.LastMonth, "O apuramento de dezembro não conta como movimento de exploração.");
            OperatingMapRow MapRow(string label) => map.Rows.Single(row => row.Label == label);
            Assert.AreEqual(100m, MapRow("Vendas e serviços prestados").Months[3]);
            Assert.AreEqual(-200m, MapRow("Fornecimentos e serviços externos").Months[3]);
            Assert.AreEqual(-10m, MapRow("Juros e gastos similares suportados").Months[6]);
            var mapResult = MapRow("Resultado líquido do período");
            Assert.IsTrue(mapResult.IsTotal);
            Assert.AreEqual(-100m, mapResult.Months[3]);
            Assert.AreEqual(-110m, mapResult.Total);
            Assert.AreEqual(0, map.Excluded.Count);
            Assert.IsFalse(map.HasBudget);
            Assert.IsNull(mapResult.Budget);

            // Com orçamento: cada linha vai para a rubrica das suas contas; a que abrange mais de uma fica de fora.
            for (var month = 1; month <= 12; month++)
            {
                context.Add(BudgetLine.Create(companyId, 2026, month, "7211", null, 50m));
                context.Add(BudgetLine.Create(companyId, 2026, month, "6221", null, month == 4 ? 150m : 0m));
                context.Add(BudgetLine.Create(companyId, 2026, month, "6", null, 1m));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var budgeted = await service.OperatingMapAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(budgeted!.HasBudget);
            OperatingMapRow BudgetedRow(string label) => budgeted.Rows.Single(row => row.Label == label);
            Assert.AreEqual(50m, BudgetedRow("Vendas e serviços prestados").Budget![0]);
            Assert.AreEqual(-150m, BudgetedRow("Fornecimentos e serviços externos").Budget![3], "Os gastos orçamentados são negativos, como os realizados.");
            Assert.AreEqual(0m, BudgetedRow("Juros e gastos similares suportados").Budget!.Sum());
            Assert.AreEqual(600m - 150m, BudgetedRow("Resultado líquido do período").Budget!.Sum());
            Assert.AreEqual(-100m, BudgetedRow("Resultado líquido do período").Budget![3]);
            var outside = budgeted.UnassignedBudget.Single();
            Assert.AreEqual(("6", 12m), (outside.AccountCode, outside.Amount));

            // Rácios sobre o balanço e a demonstração de resultados, com o período homólogo.
            var ratios = await service.RatiosAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(ratios);
            Assert.AreEqual(365, ratios!.Days);
            Assert.AreEqual(0m, ratios.BalanceDifference);
            FinancialRatio Ratio(string label) => ratios.Ratios.Single(ratio => ratio.Label == label);
            Assert.IsNull(Ratio("Liquidez geral").Value, "Sem passivo corrente não há quociente.");
            Assert.AreEqual(1520m, Ratio("Fundo de maneio").Value);
            Assert.AreEqual(1050m, Ratio("Fundo de maneio").Comparative);
            Assert.AreEqual(72.53m, Ratio("Autonomia financeira").Value);
            Assert.AreEqual(100m, Ratio("Autonomia financeira").Comparative);
            Assert.AreEqual(2.64m, Ratio("Solvabilidade").Value);
            Assert.IsNull(Ratio("Solvabilidade").Comparative);
            Assert.AreEqual(-8.33m, Ratio("Rendibilidade do capital próprio").Value);
            Assert.AreEqual(-110m, Ratio("Margem líquida").Value);
            Assert.AreEqual(100m, Ratio("Margem líquida").Comparative);
            Assert.AreEqual(0m, Ratio("Prazo médio de pagamento").Value);
            Assert.IsNull(Ratio("Duração média dos inventários").Value);
            Assert.AreEqual("%", Ratio("Margem EBITDA").Unit);
            Assert.IsNull(await service.RatiosAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.OperatingMapAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));

            // Estrutura do exercício: os gastos por natureza, do maior para o menor, com o peso de cada um.
            var structure = await service.StructureAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(structure);
            CollectionAssert.AreEqual(
                new[] { "Fornecimentos e serviços externos", "Juros e gastos similares suportados" },
                structure!.Expenses.Select(item => item.Label).ToArray());
            Assert.AreEqual(200m, structure.Expenses[0].Amount);
            Assert.AreEqual(95.2m, structure.Expenses[0].Share);
            Assert.AreEqual("Vendas e serviços prestados", structure.Income.Single().Label);
            Assert.AreEqual(100m, structure.Income.Single().Share);
            Assert.AreEqual(0, structure.TopCustomers.Count, "Neste cenário não há lançamentos com terceiro.");
            Assert.IsNull(await service.StructureAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));

            // IES, Anexo A: as rubricas nos campos do impresso, com os gastos positivos.
            var annex = await new IesAnnexAService(context, service).BuildAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(annex);
            Assert.AreEqual("S", annex!.Norm);
            decimal Ies(string code) => annex.Fields.Single(field => field.Code == code).Amount;
            Assert.AreEqual(100m, Ies("A00001"));
            Assert.AreEqual(200m, Ies("A00007"), "Os fornecimentos e serviços externos vão positivos: o impresso subtrai-os.");
            Assert.AreEqual(10m, Ies("A00023"));
            Assert.AreEqual(-100m, Ies("A00018"));
            Assert.AreEqual(-110m, Ies("A00024"));
            Assert.AreEqual(-110m, Ies("A00026"));
            Assert.AreEqual(300m, Ies("A00101"));
            Assert.AreEqual(115m, Ies("A00116"));
            Assert.AreEqual(1405m, Ies("A00124"));
            Assert.AreEqual(1820m, Ies("A00126"));
            Assert.AreEqual(1400m, Ies("A00127"));
            Assert.AreEqual(1430m, Ies("A00136"));
            Assert.AreEqual(-110m, Ies("A00137"));
            Assert.AreEqual(1320m, Ies("A00139"));
            Assert.AreEqual(500m, Ies("A00141"));
            Assert.AreEqual(500m, Ies("A00157"));
            Assert.AreEqual(1820m, Ies("A00158"));
            Assert.IsTrue(annex.Fields.Single(field => field.Code == "A00158").Computed);
            Assert.IsFalse(annex.Fields.Any(field => field.Code == "A00006"), "Os campos sem valor não aparecem.");
            Assert.AreEqual(0, annex.Warnings.Count, string.Join(" | ", annex.Warnings));
            Assert.IsNull(await new IesAnnexAService(context, service).BuildAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));

            Assert.IsNull(await service.CashFlowsAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.EquityChangesAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
