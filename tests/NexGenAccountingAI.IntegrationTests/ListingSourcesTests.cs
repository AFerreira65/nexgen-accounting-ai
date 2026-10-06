using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Domain.Banking;
using NexGenAccountingAI.Domain.Billing;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>As origens de dados das listagens (F4D-02): saldos, vendas, compras, ativos, bancos e arquivo.</summary>
[TestClass]
public sealed class ListingSourcesTests
{
    private static readonly DateOnly Today = new(2026, 6, 30);

    [TestMethod]
    public async Task EveryCatalogSourceRunsAndAggregatesOnlyTheCompanyData()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("ListingSources"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Origens", "gabinete-origens"));
            var companyId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Origens, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(otherId, firmId, "Outra Empresa, Lda.", "501964843", TaxRegion.Mainland));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("1201", "Banco"), ("2111", "Clientes"), ("7211", "Serviços"), ("4331", "Equipamento") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", 1));
            }

            // Saldos: duas contas em dois meses.
            void Entry(string reference, DateOnly date, string debitAccount, string creditAccount, decimal amount)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                entry.AddLine(Guid.NewGuid(), ids[debitAccount], "D", amount, 0m);
                entry.AddLine(Guid.NewGuid(), ids[creditAccount], "C", 0m, amount);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry("L-1", new DateOnly(2026, 1, 10), "1201", "7211", 100m);
            Entry("L-2", new DateOnly(2026, 1, 20), "1201", "7211", 50m);
            Entry("L-3", new DateOnly(2026, 2, 5), "1201", "7211", 200m);

            // Vendas: uma válida contabilizada, uma por contabilizar e uma anulada; e uma de outra empresa.
            var batch = Guid.NewGuid();
            SalesDocument Sale(Guid company, string number, string customer, decimal net, decimal tax, bool voided, string? reference)
            {
                var document = SalesDocument.Create(company, batch, number, null, "FT", new DateOnly(2026, 2, 1), voided, "500000001", customer, "PT", net, tax, net + tax, [], "saft.xml");
                if (reference is not null)
                {
                    document.LinkToEntry(reference);
                }

                return document;
            }

            context.AddRange(
                Sale(companyId, "FT 2026/1", "Cliente A", 1000m, 230m, false, "L-1"),
                Sale(companyId, "FT 2026/2", "Cliente A", 500m, 115m, false, null),
                Sale(companyId, "FT 2026/3", "Cliente B", 300m, 69m, true, null),
                Sale(otherId, "FT 2026/1", "Cliente X", 9999m, 0m, false, null));

            // Compras: uma por lançar, uma lançada e uma dispensada.
            EFaturaPurchase Purchase(string number, decimal gross) =>
                EFaturaPurchase.Create(companyId, batch, "500000009", "Fornecedor Z", number, null, new DateOnly(2026, 3, 3), "FT", "Registado", "C03", gross / 1.23m, gross - (gross / 1.23m), gross, "efatura.csv");
            var toBook = Purchase("FT 1", 123m);
            var booked = Purchase("FT 2", 246m);
            booked.LinkToEntry("L-3");
            var dismissed = Purchase("FT 3", 61.5m);
            dismissed.Dismiss("Despesa pessoal");
            context.AddRange(toBook, booked, dismissed);

            // Ativos: um em uso e um abatido.
            var inUse = FixedAsset.Create(companyId, "A001");
            inUse.Update("Computador", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 1), 1000m, 0m, 33.33m, false, null);
            inUse.LinkAccounts(ids["4331"], null, null);
            var disposed = FixedAsset.Create(companyId, "A002");
            disposed.Update("Viatura", new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 1), 20000m, 2000m, 25m, false, new DateOnly(2026, 4, 1));
            context.AddRange(inUse, disposed);

            // Bancos: dois movimentos, um conciliado.
            var import = new BankStatementImport(Guid.NewGuid(), companyId, ids["1201"], "extrato.csv", "csv", null, null, null, null, null, null, 2);
            context.Add(import);
            var reconciled = BankStatementLine.Create(companyId, ids["1201"], import.Id, "k1", new DateOnly(2026, 1, 11), null, "Recebimento", 100m, null, "REF1");
            var open = BankStatementLine.Create(companyId, ids["1201"], import.Id, "k2", new DateOnly(2026, 2, 6), null, "Pagamento", -40m, null, null);
            context.AddRange(reconciled, open);

            // Arquivo: um documento ligado a um lançamento e outro solto.
            var linked = DocumentAttachment.Create(companyId, "fatura.pdf", "ABC", 10, null);
            linked.LinkToEntry("L-1");
            linked.SetNotes("Original em papel");
            context.AddRange(linked, DocumentAttachment.Create(companyId, "solto.pdf", "DEF", 10, null));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var engine = new ListingEngine(context);
            var none = CancellationToken.None;
            Task<ListingResult> Run(ListingDefinition definition, ListingRequest? request = null) =>
                engine.RunAsync(companyId, definition, request ?? new ListingRequest(), Today, none);

            // Saldos por conta e mês: já somados, com o saldo derivado.
            var balances = await Run(new ListingDefinition("saldos", ["conta", "ano", "mes", "debito", "credito", "saldo"])).ConfigureAwait(false);
            Assert.AreEqual(4, balances.TotalRows, "1201 e 7211, em janeiro e em fevereiro.");
            var january = balances.Rows.Single(row => (string?)row.Cells[0] == "1201" && Convert.ToInt32(row.Cells[2], System.Globalization.CultureInfo.InvariantCulture) == 1);
            Assert.AreEqual(150m, january.Cells[3]);
            Assert.AreEqual(150m, january.Cells[5]);
            Assert.AreEqual(350m, balances.Totals["debito"]);
            Assert.AreEqual(0m, balances.Totals["saldo"]);
            var byClass = await Run(new ListingDefinition("saldos", ["conta", "debito"], GroupBy: ["classe", "mes"])).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "1", "7" }, byClass.Groups.Select(group => group.Key).ToArray());
            var monthsOfBank = await Run(new ListingDefinition("saldos", ["conta", "debito"], GroupBy: ["classe", "mes"]), new ListingRequest(GroupPath: ["1"])).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "1", "2" }, monthsOfBank.Groups.Select(group => group.Key).ToArray());
            Assert.AreEqual(200m, monthsOfBank.Groups[1].Sums["debito"]);

            // Vendas: sem a anulada nem a da outra empresa; filtrar pelo estado traz a anulada.
            var sales = await Run(new ListingDefinition("vendas", ["documento", "cliente", "contabilizado", "liquido", "iva", "total"], GroupBy: ["cliente"])).ConfigureAwait(false);
            Assert.AreEqual(2, sales.TotalRows);
            Assert.AreEqual(1500m, sales.Totals["liquido"]);
            Assert.AreEqual(1845m, sales.Totals["total"]);
            Assert.AreEqual("Cliente A", sales.Groups.Single().Key);
            var notBooked = await Run(new ListingDefinition(
                "vendas", ["documento", "lancamento"], new ListingFilterGroup("and", [new ListingCondition("contabilizado", "eq", "Não")]))).ConfigureAwait(false);
            Assert.AreEqual("FT 2026/2", notBooked.Rows.Single().Cells[0]);
            var voided = await Run(new ListingDefinition(
                "vendas", ["documento"], new ListingFilterGroup("and", [new ListingCondition("estado", "eq", "anulado")]))).ConfigureAwait(false);
            Assert.AreEqual("FT 2026/3", voided.Rows.Single().Cells[0]);
            Assert.AreEqual("L-1", (await Run(new ListingDefinition("vendas", ["documento"], Sort: [new ListingSort("documento")])).ConfigureAwait(false)).Rows[0].Link.EntryReference);

            // Compras: a situação diz o que falta fazer.
            var purchases = await Run(new ListingDefinition("compras", ["situacao", "total"], GroupBy: ["situacao"])).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "Dispensada", "Lançada", "Por lançar" }, purchases.Groups.Select(group => group.Key).ToArray());
            Assert.AreEqual(246m, purchases.Groups.Single(group => group.Key == "Lançada").Sums["total"]);
            Assert.AreEqual(430.5m, purchases.Totals["total"]);

            // Ativos: o estado vem do abate; as datas anuláveis filtram-se.
            var assets = await Run(new ListingDefinition("ativos", ["codigo", "estado", "valorAquisicao"], GroupBy: ["estado"])).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "Abatido", "Em uso" }, assets.Groups.Select(group => group.Key).ToArray());
            Assert.AreEqual(21000m, assets.Totals["valorAquisicao"]);
            var disposedBefore = await Run(new ListingDefinition(
                "ativos", ["codigo"], new ListingFilterGroup("and", [new ListingCondition("abate", "between", "2026-01-01", "2026-12-31")]))).ConfigureAwait(false);
            Assert.AreEqual("A002", disposedBefore.Rows.Single().Cells[0]);
            var inUseAccount = await Run(new ListingDefinition("ativos", ["codigo", "conta"], new ListingFilterGroup("and", [new ListingCondition("conta", "eq", "4331")]))).ConfigureAwait(false);
            Assert.AreEqual("A001", inUseAccount.Rows.Single().Cells[0]);

            // Bancos: a conta do banco vem do plano; o valor soma com sinal; filtra-se por conciliado.
            var bank = await Run(new ListingDefinition("bancos", ["data", "descricao", "conta", "conciliado", "valor"])).ConfigureAwait(false);
            Assert.AreEqual(2, bank.TotalRows);
            Assert.AreEqual(60m, bank.Totals["valor"]);
            Assert.AreEqual("1201", bank.Rows[0].Cells[2]);
            var openLines = await Run(new ListingDefinition("bancos", ["descricao", "valor"], new ListingFilterGroup("and", [new ListingCondition("valor", "lt", "0")]))).ConfigureAwait(false);
            Assert.AreEqual(-40m, openLines.Rows.Single().Cells[1]);

            // Arquivo: ligado ou solto.
            var archive = await Run(new ListingDefinition("arquivo", ["ficheiro", "ligado", "lancamento", "notas"], GroupBy: ["ligado"])).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "Não", "Sim" }, archive.Groups.Select(group => group.Key).ToArray());
            var linkedRows = await Run(new ListingDefinition("arquivo", ["ficheiro", "lancamento", "notas"], new ListingFilterGroup("and", [new ListingCondition("ligado", "eq", "Sim")]))).ConfigureAwait(false);
            Assert.AreEqual("fatura.pdf", linkedRows.Rows.Single().Cells[0]);
            Assert.AreEqual("L-1", linkedRows.Rows.Single().Link.EntryReference);

            // Uma data que falta no documento também se filtra (campo com nulos).
            var withoutDate = await Run(new ListingDefinition("arquivo", ["ficheiro"], new ListingFilterGroup("and", [new ListingCondition("dataDocumento", "ge", "2026-01-01")]))).ConfigureAwait(false);
            Assert.AreEqual(0, withoutDate.TotalRows);

            // A outra empresa só vê o seu.
            var other = await engine.RunAsync(otherId, new ListingDefinition("vendas", ["documento", "total"]), new ListingRequest(), Today, none).ConfigureAwait(false);
            Assert.AreEqual(9999m, other.Totals["liquido"]);
            Assert.AreEqual(0, (await engine.RunAsync(otherId, new ListingDefinition("compras", ["documento"]), new ListingRequest(), Today, none).ConfigureAwait(false)).TotalRows);

            // Os modelos guiados: todos válidos e todos correm tal como estão, com os valores por omissão.
            foreach (var template in ListingTemplates.All)
            {
                Assert.AreEqual(0, ListingCatalog.Validate(template.Definition).Count, template.Name);
                // Os parâmetros de texto sem valor por omissão (o terceiro) pedem-se ao utilizador.
                var asked = (template.Definition.Parameters ?? []).Where(parameter => parameter.Type == "text" && string.IsNullOrEmpty(parameter.Default))
                    .ToDictionary(parameter => parameter.Name, _ => "Cliente A");
                var ran = await Run(template.Definition, new ListingRequest(asked)).ConfigureAwait(false);
                Assert.IsNotNull(ran, template.Name);
            }

            var statement = await Run(ListingTemplates.Find("extrato-conta")!.Definition, new ListingRequest(new Dictionary<string, string> { ["conta"] = "1201", ["inicio"] = "2026-02-01", ["fim"] = "2026-02-28" })).ConfigureAwait(false);
            Assert.AreEqual(1, statement.TotalRows, "O extrato do banco em fevereiro tem um movimento.");
            Assert.AreEqual(200m, statement.Totals["debito"]);
            var unbooked = await Run(ListingTemplates.Find("compras-por-lancar")!.Definition).ConfigureAwait(false);
            Assert.AreEqual(1, unbooked.TotalRows);
            var perParty = await Run(ListingTemplates.Find("saldo-por-terceiro")!.Definition).ConfigureAwait(false);
            Assert.AreEqual("groups", perParty.Kind);

            // Tabela cruzada: contas nas linhas e meses nas colunas, a somar o débito; os filtros da definição valem.
            var pivot = await engine.PivotAsync(companyId, new ListingDefinition("saldos", ["conta"]), null, "conta", "mes", "debito", Today, none).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "1201", "7211" }, pivot.Rows.Select(row => row.Label).ToArray());
            CollectionAssert.AreEqual(new[] { "1", "2" }, pivot.Columns.Select(column => column.Label).ToArray());
            CollectionAssert.AreEqual(new[] { 150m, 200m }, pivot.Values[0].ToArray());
            CollectionAssert.AreEqual(new[] { 0m, 0m }, pivot.Values[1].ToArray());
            CollectionAssert.AreEqual(new[] { 350m, 0m }, pivot.RowTotals.ToArray());
            CollectionAssert.AreEqual(new[] { 150m, 200m }, pivot.ColumnTotals.ToArray());
            Assert.AreEqual(350m, pivot.GrandTotal);
            Assert.AreEqual("Débito", pivot.MeasureLabel);
            var net = await engine.PivotAsync(companyId, new ListingDefinition("saldos", ["conta"]), null, "classe", "ano", "saldo", Today, none).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { 350m, -350m }, net.Values.Select(row => row[0]).ToArray(), "A medida derivada (saldo) também se cruza.");
            var counted = await engine.PivotAsync(
                companyId, new ListingDefinition("vendas", ["documento"], new ListingFilterGroup("and", [new ListingCondition("data", "ge", "2026-01-01")])), null, "cliente", "contabilizado", "contagem", Today, none).ConfigureAwait(false);
            Assert.AreEqual(2m, counted.GrandTotal);
            CollectionAssert.AreEqual(new[] { "Não", "Sim" }, counted.Columns.Select(column => column.Label).ToArray());
            var noValue = await engine.PivotAsync(companyId, new ListingDefinition("ativos", ["codigo"]), null, "conta", "estado", "valorAquisicao", Today, none).ConfigureAwait(false);
            Assert.IsTrue(noValue.Rows.Any(row => row.Key is null && row.Label == "(sem valor)"), "O ativo sem conta fica na linha «sem valor».");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => engine.PivotAsync(companyId, new ListingDefinition("saldos", ["conta"]), null, "conta", "conta", "debito", Today, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => engine.PivotAsync(companyId, new ListingDefinition("saldos", ["conta"]), null, "descricao", "mes", "debito", Today, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => engine.PivotAsync(companyId, new ListingDefinition("saldos", ["conta"]), null, "conta", "mes", "total", Today, none)).ConfigureAwait(false);

            // Exportar vale para todas as origens.
            foreach (var source in ListingCatalog.Sources)
            {
                var export = await engine.ExportAsync(companyId, new ListingDefinition(source.Key, source.Fields.Select(field => field.Key).ToList()), null, Today, none).ConfigureAwait(false);
                Assert.AreEqual(source.Fields.Count, export.Columns.Count, source.Label);
            }
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
