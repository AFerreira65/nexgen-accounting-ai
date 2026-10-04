using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class JournalPostingPartyTests
{
    [TestMethod]
    public async Task PostsRevisesAndReviewsLinesWithPartyVatCodeAndDocumentDate()
    {
        var connectionString = TestDatabase.ConnectionString("LineParty");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Linha", "gabinete-linha"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Linha, Lda.", "LP-PT-0001", TaxRegion.Mainland));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "VENDAS", "Vendas"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "21111", "Cliente A"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "7111", "Vendas"));

            var customer = Party.Create(companyId, "PT", "500584095", "Cliente A, Lda.");
            customer.SetRoles(isCustomer: true, isSupplier: false);
            context.Add(customer);
            var foreign = Party.Create(companyId, "ES", "B12345678", "Cliente B, S.L.");
            context.Add(foreign);
            var inactive = Party.Create(companyId, "PT", "509999999", "Inativo, Lda.");
            inactive.SetActive(false);
            context.Add(inactive);

            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Linha", "linha@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(VatCode.Create(companyId, "V-NOR", "Vendas — taxa normal", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas, VatRegion.Continente));
            var retired = VatCode.Create(companyId, "V-OLD", "Código antigo", VatRate.Normal, 21m, VatOperationNature.TransmissoesInternas, VatRegion.Continente);
            retired.SetActive(false);
            context.Add(retired);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new JournalPostingService(context);
            var date = new DateOnly(2026, 3, 10);

            // Lançar: só a linha de terceiros leva o terceiro.
            var posted = await service.PostAsync(
                companyId, "VENDAS", date, "FT 1/1", "Venda a crédito", reviewed: false,
                [
                    new JournalEntryLineInput("21111", "Cliente", 123m, 0m, " 500 584 095 "),
                    new JournalEntryLineInput("7111", "Venda", 0m, 123m, VatCode: " v-nor "),
                ],
                documentDate: new DateOnly(2026, 3, 8)).ConfigureAwait(false);
            Assert.AreEqual("V-NOR", posted.Lines[1].VatCode);
            Assert.IsNull(posted.Lines[0].VatCode);
            Assert.AreEqual(new DateOnly(2026, 3, 8), posted.DocumentDate);
            Assert.AreEqual("500584095", posted.Lines[0].PartyTaxNumber);
            Assert.AreEqual("PT", posted.Lines[0].PartyCountryCode);
            Assert.AreEqual("Cliente A, Lda.", posted.Lines[0].PartyName);
            Assert.IsNull(posted.Lines[1].PartyTaxNumber);

            context.ChangeTracker.Clear();
            var storedLine = await context.JournalEntryLines.SingleAsync(line => line.PartyId != null).ConfigureAwait(false);
            Assert.AreEqual(customer.Id, storedLine.PartyId);

            // Corrigir: troca para o terceiro estrangeiro (país indicado).
            var revised = await service.ReviseAsync(
                companyId, posted.Reference, "VENDAS", date, "FT 1/1", "Venda a crédito", reviewed: false,
                [
                    new JournalEntryLineInput("21111", "Cliente", 123m, 0m, "b12345678", "es"),
                    new JournalEntryLineInput("7111", "Venda", 0m, 123m),
                ]).ConfigureAwait(false);
            Assert.IsNull(revised.DocumentDate, "Corrigir sem data do documento limpa-a.");
            Assert.AreEqual("ES", revised.Lines[0].PartyCountryCode);
            Assert.AreEqual("Cliente B, S.L.", revised.Lines[0].PartyName);

            // Conferir devolve o terceiro lido da base.
            var reviewed = await service.MarkReviewedAsync(companyId, posted.Reference).ConfigureAwait(false);
            Assert.AreEqual("B12345678", reviewed.Lines[0].PartyTaxNumber);
            Assert.IsNull(reviewed.Lines[1].PartyName);
            Assert.IsNull(reviewed.Lines[1].VatCode, "A correção não indicou código de IVA.");

            // Um código com lançamentos não pode mudar de significado; sem lançamentos pode.
            var withCode = await service.PostAsync(
                companyId, "VENDAS", date, "FT 1/3", "Venda com IVA", reviewed: true,
                [
                    new JournalEntryLineInput("21111", "Cliente", 100m, 0m),
                    new JournalEntryLineInput("7111", "Venda", 0m, 100m, VatCode: "V-NOR"),
                ]).ConfigureAwait(false);
            var reread = await service.ReopenAsync(companyId, withCode.Reference).ConfigureAwait(false);
            Assert.AreEqual("V-NOR", reread.Lines[1].VatCode);
            context.ChangeTracker.Clear();

            var vatCodes = new VatCodeService(context);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => vatCodes.UpdateAsync(
                userId, companyId, "V-NOR",
                new VatCodeInput("Vendas — taxa normal", VatRate.Normal, 22m, VatOperationNature.TransmissoesInternas, VatRegion.Continente),
                CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var renamed = await vatCodes.UpdateAsync(
                userId, companyId, "V-NOR",
                new VatCodeInput("Vendas à taxa normal", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas, VatRegion.Continente),
                CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("Vendas à taxa normal", renamed!.Name);
            var unused = await vatCodes.UpdateAsync(
                userId, companyId, "V-OLD",
                new VatCodeInput("Código antigo", VatRate.Normal, 20m, VatOperationNature.TransmissoesInternas, VatRegion.Continente, IsActive: false),
                CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(20m, unused!.Percentage);
            context.ChangeTracker.Clear();

            // Código de IVA desconhecido ou inativo recusa o lançamento.
            foreach (var code in new[] { "NAO-HA", "V-OLD" })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PostAsync(
                    companyId, "VENDAS", date, "FT 1/4", "Venda", reviewed: false,
                    [
                        new JournalEntryLineInput("21111", "Cliente", 10m, 0m),
                        new JournalEntryLineInput("7111", "Venda", 0m, 10m, VatCode: code),
                    ])).ConfigureAwait(false);
            }

            // Terceiro desconhecido, inativo, e o mesmo NIF noutro país.
            var entriesBefore = await context.JournalEntries.CountAsync().ConfigureAwait(false);
            foreach (var (taxNumber, country) in new[] { ("123456789", "PT"), ("509999999", "PT"), ("500584095", "ES") })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PostAsync(
                    companyId, "VENDAS", date, "FT 1/2", "Venda", reviewed: false,
                    [
                        new JournalEntryLineInput("21111", "Cliente", 10m, 0m, taxNumber, country),
                        new JournalEntryLineInput("7111", "Venda", 0m, 10m),
                    ])).ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            Assert.AreEqual(entriesBefore, await context.JournalEntries.CountAsync().ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
