using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class SalesSuggestedEntryServiceTests
{
    private const string Customer = "503504564";

    private sealed class FakeReader(SaftSalesDocumentsResult result) : ISaftSalesDocumentsReader
    {
        public Task<SaftSalesDocumentsResult> ReadAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private static SaftSalesDocument Document(
        string number, int day, string type, string customerTaxId, bool voided, params SaftSalesDocumentTax[] taxes) =>
        new(number, null, type, new DateOnly(2026, 3, day), voided, "C", customerTaxId,
            customerTaxId == Customer ? "Cliente Um, Lda." : "Consumidor final",
            customerTaxId == Customer ? "PT" : "Desconhecido",
            taxes.Sum(tax => tax.TaxBase), taxes.Sum(tax => tax.TaxAmount), taxes.Sum(tax => tax.TaxBase + tax.TaxAmount), taxes);

    [TestMethod]
    public async Task ImportsSalesDocumentsAndSuggestsOneEntryPerDocument()
    {
        var connectionString = TestDatabase.ConnectionString("SalesSuggested");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Vendas", "gabinete-vendas"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Vendas, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Vendas", "vendas@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "VENDAS", "Vendas"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("7111", "Vendas de mercadorias"), ("24331", "IVA liquidado normal"), ("24333", "IVA liquidado reduzida"),
                ("2111", "Clientes c/c"), ("2111001", "Cliente Um"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            context.Add(VatCode.Create(companyId, "V-NOR", "Vendas normal", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas, VatRegion.Continente));
            context.Add(VatCode.Create(companyId, "V-RED", "Vendas reduzida", VatRate.Reduzida, 6m, VatOperationNature.TransmissoesInternas, VatRegion.Continente));
            var customer = Party.Create(companyId, "PT", Customer, "Cliente Um, Lda.");
            customer.SetRoles(isCustomer: true, isSupplier: false);
            customer.LinkAccounts(ids["2111001"], null);
            context.Add(customer);

            // A FT A/3 já foi lançada à mão, pelo número do documento.
            var manual = new JournalEntry(Guid.NewGuid(), companyId, journalId, "M-1", new DateOnly(2026, 3, 20), "FT A/3", "Venda lançada à mão");
            manual.AddLine(Guid.NewGuid(), ids["2111"], "Cliente", 12.30m, 0m);
            manual.AddLine(Guid.NewGuid(), ids["7111"], "Venda", 0m, 10m);
            manual.AddLine(Guid.NewGuid(), ids["24331"], "IVA", 0m, 2.30m);
            manual.Complete(JournalEntryStatus.Reviewed);
            context.Add(manual);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var templates = new EntryTemplateService(context);
            foreach (var (code, vatAccount, vatCode) in new[] { ("VENDA-NOR", "24331", "V-NOR"), ("VENDA-RED", "24333", "V-RED") })
            {
                await templates.SaveAsync(userId, companyId, code, new EntryTemplateInput(
                    code, "VENDAS", null, null, true,
                    [
                        new EntryTemplateLineInput(EntryTemplateLineKind.Base, EntryTemplateSide.Credit, "7111", vatCode, null),
                        new EntryTemplateLineInput(EntryTemplateLineKind.Vat, EntryTemplateSide.Credit, vatAccount, vatCode, "IVA liquidado"),
                        new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Debit, "2111", null, null),
                    ]), CancellationToken.None).ConfigureAwait(false);
                context.ChangeTracker.Clear();
            }

            var file = new SaftSalesDocumentsResult(null,
            [
                Document("FT A/1", 5, "FT", Customer, false, new SaftSalesDocumentTax(23m, 100m, 23m), new SaftSalesDocumentTax(6m, 50m, 3m)),
                Document("NC A/1", 10, "NC", "999999990", false, new SaftSalesDocumentTax(23m, 20m, 4.60m)),
                Document("FT A/2", 12, "FT", Customer, true, new SaftSalesDocumentTax(23m, 5m, 1.15m)),
                Document("FT A/3", 15, "FT", Customer, false, new SaftSalesDocumentTax(23m, 10m, 2.30m)),
                Document("FT A/4", 16, "FT", Customer, false, new SaftSalesDocumentTax(13m, 10m, 1.30m)),
            ], []);
            var service = new SalesSuggestedEntryService(new FakeReader(file), context, new JournalPostingService(context));

            var preview = await service.ImportAsync(userId, companyId, Stream.Null, "vendas.xml", dryRun: true, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(5, preview!.Imported);
            Assert.IsNull(preview.BatchId);
            Assert.AreEqual(0, await context.SalesDocuments.CountAsync().ConfigureAwait(false));

            var imported = await service.ImportAsync(userId, companyId, Stream.Null, "vendas.xml", dryRun: false, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(5, imported!.Imported);
            Assert.AreEqual(1, imported.Voided);
            var again = await service.ImportAsync(userId, companyId, Stream.Null, "vendas.xml", dryRun: false, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, again!.Imported);
            Assert.AreEqual(5, again.AlreadyExisted, "Reimportar não duplica.");
            context.ChangeTracker.Clear();

            var otherCompany = new SalesSuggestedEntryService(
                new FakeReader(file with { CompanyTaxId = "509999999" }), context, new JournalPostingService(context));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => otherCompany
                .ImportAsync(userId, companyId, Stream.Null, "outra.xml", dryRun: true, CancellationToken.None)).ConfigureAwait(false);

            await service.SaveRuleAsync(userId, companyId, 23m, "venda-nor", true, CancellationToken.None).ConfigureAwait(false);
            await service.SaveRuleAsync(userId, companyId, 6m, "VENDA-RED", true, CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var from = new DateOnly(2026, 3, 1);
            var to = new DateOnly(2026, 3, 31);
            var report = await service.ListAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(5, report!.DocumentCount);
            Assert.AreEqual(1, report.VoidedCount);
            Assert.AreEqual(1, report.BookedCount, "A FT A/3 já tem lançamento.");
            CollectionAssert.AreEqual(new[] { "FT A/1", "NC A/1", "FT A/4" }, report.Items.Select(item => item.InvoiceNo).ToArray());
            CollectionAssert.AreEqual(new[] { 13m }, report.PercentagesWithoutRule.ToArray());

            // Duas taxas: duas linhas de rendimento (uma por código de IVA), duas de IVA e um só cliente.
            var mixed = report.Items[0];
            Assert.IsTrue(mixed.Ready);
            Assert.AreEqual("VENDAS", mixed.JournalCode);
            Assert.AreEqual(5, mixed.Lines.Count);
            var customerLine = mixed.Lines.Single(line => line.AccountCode == "2111001");
            Assert.AreEqual(176m, customerLine.Debit);
            Assert.AreEqual(Customer, customerLine.PartyTaxNumber);
            Assert.AreEqual(100m, mixed.Lines.Single(line => line.AccountCode == "7111" && line.VatCode == "V-NOR").Credit);
            Assert.AreEqual(50m, mixed.Lines.Single(line => line.AccountCode == "7111" && line.VatCode == "V-RED").Credit);
            Assert.AreEqual(3m, mixed.Lines.Single(line => line.AccountCode == "24333").Credit);

            var refund = report.Items[1];
            Assert.IsTrue(refund.IsCreditNote);
            Assert.AreEqual(24.60m, refund.Lines.Single(line => line.AccountCode == "2111").Credit, "Consumidor final: conta geral, sem terceiro.");
            Assert.IsNull(refund.Lines.Single(line => line.AccountCode == "2111").PartyTaxNumber);
            Assert.AreEqual(20m, refund.Lines.Single(line => line.AccountCode == "7111").Debit);

            Assert.AreEqual("Sem regra de vendas para a taxa de 13 %.", report.Items[2].Problem);

            var outcomes = await service.FinalizeAsync(userId, companyId, [mixed.DocumentId, refund.DocumentId, report.Items[2].DocumentId], CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(outcomes);
            var reference = outcomes.Single(item => item.DocumentId == mixed.DocumentId).Reference!;
            Assert.IsNotNull(outcomes.Single(item => item.DocumentId == refund.DocumentId).Reference);
            Assert.AreEqual("Sem regra de vendas para a taxa de 13 %.", outcomes.Single(item => item.DocumentId == report.Items[2].DocumentId).Error);
            context.ChangeTracker.Clear();

            var posted = await context.JournalEntries.AsNoTracking().Include(entry => entry.Lines)
                .SingleAsync(entry => entry.CompanyId == companyId && entry.Reference == reference).ConfigureAwait(false);
            Assert.AreEqual(EntrySource.SaftBilling, posted.Source);
            Assert.AreEqual(JournalEntryStatus.PendingReview, posted.Status);
            Assert.AreEqual("FT A/1", posted.DocumentReference);
            Assert.AreEqual(customer.Id, posted.Lines.Single(line => line.AccountId == ids["2111001"]).PartyId);

            var after = await service.ListAsync(userId, companyId, from, to, CancellationToken.None).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "FT A/4" }, after!.Items.Select(item => item.InvoiceNo).ToArray());

            Assert.AreEqual(2, (await service.ListRulesAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false))!.Count);
            Assert.IsTrue(await service.DeleteRuleAsync(userId, companyId, 6m, CancellationToken.None).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveRuleAsync(userId, companyId, 23m, "NAO-HA", true, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ImportAsync(Guid.NewGuid(), companyId, Stream.Null, "x.xml", true, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
