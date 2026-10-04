using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatSummaryLineCodeTests
{
    [TestMethod]
    public async Task LineVatCodeOverridesTheAccountClassification()
    {
        var connectionString = TestDatabase.ConnectionString("VatLineCode");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Código Linha", "gabinete-codigo-linha"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Código Linha, Lda.", "CL-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Código Linha", "cl@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var cash = Guid.NewGuid();
            var sales = Guid.NewGuid();
            var otherSales = Guid.NewGuid();
            var outputVat = Guid.NewGuid();
            var purchases = Guid.NewGuid();
            context.Add(new LedgerAccount(cash, companyId, "1201", "Depósito à ordem"));
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(otherSales, companyId, "7112", "Outras vendas"));
            context.Add(new LedgerAccount(outputVat, companyId, "24331", "IVA liquidado"));
            context.Add(new LedgerAccount(purchases, companyId, "311", "Compras de mercadorias"));

            // Só as vendas e o IVA liquidado estão classificados por conta (taxa normal).
            context.Add(AccountVatClassification.Create(
                companyId, sales, VatRate.Normal, VatOperationNature.TransmissoesInternas, VatRegion.Continente));
            context.Add(AccountVatClassification.Create(
                companyId, outputVat, VatRate.Normal, VatOperationNature.TransmissoesInternas, VatRegion.Continente));

            var normal = VatCode.Create(companyId, "V-NOR", "Vendas normal", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas, VatRegion.Continente);
            var reduced = VatCode.Create(companyId, "V-RED", "Vendas reduzida", VatRate.Reduzida, 6m, VatOperationNature.TransmissoesInternas, VatRegion.Continente);
            var stock = VatCode.Create(companyId, "C-EXI-NOR", "Compras existências", VatRate.Normal, 23m, VatOperationNature.IvaDedutivelExistencias, VatRegion.Continente);
            context.AddRange(normal, reduced, stock);

            var date = new DateOnly(2026, 2, 10);
            // 1. Venda sem código: lida pela classificação da conta (normal).
            context.Add(Entry(companyId, journalId, "L-1", date,
                (cash, 123m, 0m, null), (sales, 0m, 100m, null), (outputVat, 0m, 23m, null)));
            // 2. Venda na mesma conta mas com código de taxa reduzida: o código prevalece.
            context.Add(Entry(companyId, journalId, "L-2", date,
                (cash, 53m, 0m, null), (sales, 0m, 50m, reduced.Id), (outputVat, 0m, 3m, reduced.Id)));
            // 3. Compra em classe 3, sem classificação por conta, com código: conta como base.
            context.Add(Entry(companyId, journalId, "L-3", date,
                (purchases, 200m, 0m, stock.Id), (cash, 0m, 200m, null)));
            // 4. Conta 7112 sem classificação: uma linha com código (entra no resumo)…
            context.Add(Entry(companyId, journalId, "L-4", date,
                (cash, 10m, 0m, null), (otherSales, 0m, 10m, normal.Id)));
            // 5. …e outra sem código (fica nas contas por classificar).
            context.Add(Entry(companyId, journalId, "L-5", date,
                (cash, 5m, 0m, null), (otherSales, 0m, 5m, null)));

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var summary = await new VatSummaryService(context)
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(summary);

            VatSummaryRow Row(string kind, string nature, string rate) =>
                summary!.Rows.Single(row => row.Kind == kind && row.Nature == nature && row.Rate == rate);

            var normalBase = Row("base", "TransmissoesInternas", "Normal");
            Assert.AreEqual(110m, normalBase.Credit, "Venda 1 pela conta (100) + venda 4 pelo código (10).");
            Assert.AreEqual(2, normalBase.AccountCount);
            Assert.AreEqual(23m, Row("imposto", "TransmissoesInternas", "Normal").Credit);

            Assert.AreEqual(50m, Row("base", "TransmissoesInternas", "Reduzida").Credit, "O código da linha prevalece sobre a conta.");
            Assert.AreEqual(3m, Row("imposto", "TransmissoesInternas", "Reduzida").Credit);

            var stockBase = Row("base", "IvaDedutivelExistencias", "Normal");
            Assert.AreEqual(200m, stockBase.Debit, "Uma compra em classe 3 com código é base.");

            Assert.AreEqual(5, summary!.Rows.Count);

            // Só a linha sem código da conta por classificar fica de fora.
            var unclassified = summary.Unclassified.Single();
            Assert.AreEqual("7112", unclassified.AccountCode);
            Assert.AreEqual(5m, unclassified.Credit);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private static JournalEntry Entry(
        Guid companyId, Guid journalId, string reference, DateOnly date,
        params (Guid AccountId, decimal Debit, decimal Credit, Guid? VatCodeId)[] lines)
    {
        var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
        foreach (var line in lines)
        {
            entry.AddLine(Guid.NewGuid(), line.AccountId, "Linha", line.Debit, line.Credit, vatCodeId: line.VatCodeId);
        }

        entry.Complete(JournalEntryStatus.Reviewed);
        return entry;
    }
}
