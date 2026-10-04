using System.Text;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Billing;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class DocumentArchiveServiceTests
{
    private const string PurchaseQr = "A:503504564*B:500584095*C:PT*D:FT*E:N*F:20260305*G:FT A/12*H:ABCD1234-12*I1:PT*I7:100.00*I8:23.00*N:23.00*O:123.00*Q:abcd*R:1234";
    private const string SaleQr = "A:500584095*B:999999990*C:PT*D:FT*E:N*F:20260310*G:FT V/1*H:WXYZ9876-1*I1:PT*I7:10.00*I8:2.30*N:2.30*O:12.30*Q:wxyz*R:4321";

    [TestMethod]
    public async Task ArchivesDocumentsReadsTheQrCodeAndProtectsReviewedEntries()
    {
        var connectionString = TestDatabase.ConnectionString("Archive");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Arquivo", "gabinete-arquivo"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Arquivo, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Arquivo", "arquivo@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "COMPRAS", "Compras"));
            var expense = Guid.NewGuid();
            var supplier = Guid.NewGuid();
            context.Add(new LedgerAccount(expense, companyId, "6221", "Trabalhos especializados"));
            context.Add(new LedgerAccount(supplier, companyId, "2211", "Fornecedores"));

            JournalEntry Entry(string reference, string document, JournalEntryStatus status)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 3, 6), document, $"Compra {document}");
                entry.AddLine(Guid.NewGuid(), expense, "Serviço", 123m, 0m);
                entry.AddLine(Guid.NewGuid(), supplier, "Fornecedor", 0m, 123m);
                entry.Complete(status);
                return entry;
            }

            context.Add(Entry("C-1", "FT A/12", JournalEntryStatus.Reviewed));
            context.Add(Entry("C-2", "FT A/99", JournalEntryStatus.PendingReview));
            var purchase = EFaturaPurchase.Create(companyId, Guid.NewGuid(), "503504564", "Fornecedor B", "FTA/12", null, new DateOnly(2026, 3, 5), "Fatura", null, null, 100m, 23m, 123m, "x.csv");
            context.Add(purchase);
            var sale = SalesDocument.Create(companyId, Guid.NewGuid(), "FT V/1", null, "FT", new DateOnly(2026, 3, 10), false, "999999990", "Consumidor final", null, 10m, 2.30m, 12.30m, [new SalesDocumentTax(23m, 10m, 2.30m)], "v.xml");
            context.Add(sale);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new DocumentArchiveService(context);
            using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 fatura FT A/12"));
            var uploaded = await service.UploadAsync(userId, companyId, @"C:\Digitalizações\fatura.pdf", pdf, null, PurchaseQr, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(uploaded);
            Assert.AreEqual("fatura.pdf", uploaded.FileName, "Só fica o nome do ficheiro, sem a pasta.");
            Assert.AreEqual("application/pdf", uploaded.ContentType);
            Assert.AreEqual(64, uploaded.Sha256.Length);
            Assert.AreEqual("FT A/12", uploaded.QrDocumentNo);
            Assert.AreEqual(123m, uploaded.QrGrossTotal);
            Assert.AreEqual(new DateOnly(2026, 3, 5), uploaded.QrDocumentDate);
            context.ChangeTracker.Clear();

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .UploadAsync(userId, companyId, "copia.pdf", new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 fatura FT A/12")), null, null, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UploadAsync(userId, companyId, "programa.exe", new MemoryStream([1, 2, 3]), null, null, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UploadAsync(userId, companyId, "outra.pdf", new MemoryStream([1, 2, 3]), null, "não é um QR", CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // O código QR encontra a fatura do e-Fatura (número normalizado), o lançamento e o documento arquivado.
            var lookup = await service.LookupQrAsync(userId, companyId, PurchaseQr, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(lookup!.IsSale);
            Assert.AreEqual(purchase.Id, lookup.EFaturaPurchaseId);
            CollectionAssert.AreEqual(new[] { "C-1" }, lookup.EntryReferences.ToArray());
            CollectionAssert.AreEqual(new[] { uploaded.Id }, lookup.AttachmentIds.ToArray());
            var saleLookup = await service.LookupQrAsync(userId, companyId, SaleQr, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(saleLookup!.IsSale, "O emitente é a própria empresa.");
            Assert.AreEqual(sale.Id, saleLookup.SalesDocumentId);

            // Ligado a um lançamento conferido, o documento não se apaga nem se desliga.
            var linked = await service.UpdateAsync(userId, companyId, uploaded.Id, new DocumentAttachmentUpdate("c-1", "Original digitalizado", null), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("C-1", linked!.EntryReference);
            Assert.IsFalse(linked.CanDelete);
            Assert.AreEqual("FT A/12", linked.QrDocumentNo, "Sem texto de QR, os dados do QR mantêm-se.");
            context.ChangeTracker.Clear();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.DeleteAsync(userId, companyId, uploaded.Id, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .UpdateAsync(userId, companyId, uploaded.Id, new DocumentAttachmentUpdate(null, null, null), CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Num lançamento por conferir, apaga-se.
            var other = await service.UploadAsync(userId, companyId, "recibo.png", new MemoryStream([137, 80, 78, 71]), "C-2", null, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(other!.CanDelete);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UpdateAsync(userId, companyId, other.Id, new DocumentAttachmentUpdate("NAO-HA", null, null), CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            CollectionAssert.AreEqual(new[] { other.Id }, (await service.ListAsync(userId, companyId, "C-2", false, null, CancellationToken.None).ConfigureAwait(false))!.Select(row => row.Id).ToArray());
            Assert.AreEqual(1, (await service.ListAsync(userId, companyId, null, false, "a/12", CancellationToken.None).ConfigureAwait(false))!.Count);
            Assert.IsTrue(await service.DeleteAsync(userId, companyId, other.Id, CancellationToken.None).ConfigureAwait(false));
            Assert.AreEqual(1, await context.DocumentAttachmentContents.CountAsync().ConfigureAwait(false), "O conteúdo vai com o documento.");

            var content = await service.GetContentAsync(userId, companyId, uploaded.Id, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("%PDF-1.4 fatura FT A/12", Encoding.ASCII.GetString(content!.Value.Content));

            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, null, false, null, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.GetContentAsync(Guid.NewGuid(), companyId, uploaded.Id, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
