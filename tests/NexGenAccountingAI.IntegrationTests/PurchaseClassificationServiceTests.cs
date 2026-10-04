using NexGenAccountingAI.Application.Classification;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class PurchaseClassificationServiceTests
{
    private const string Supplier = "500584095";
    private const string NewSupplier = "503504564";

    private sealed class FakeClassifier(string? account, string? vat, bool configured = true) : IPurchaseClassifier
    {
        public PurchaseClassificationDossier? LastDossier { get; private set; }

        public bool IsConfigured => configured;

        public Task<PurchaseClassificationAnswer> ClassifyAsync(PurchaseClassificationDossier dossier, CancellationToken cancellationToken = default)
        {
            LastDossier = dossier;
            return Task.FromResult(new PurchaseClassificationAnswer(account, vat, "Ferramentas e utensílios de desgaste rápido.", "modelo-de-teste", 10, 5));
        }
    }

    [TestMethod]
    public async Task ProposesTheClassificationFromTheSupplierHistoryAndAcceptsIt()
    {
        var connectionString = TestDatabase.ConnectionString("Classification");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Classificação", "gabinete-classificacao"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Classificação, Lda.", "CL-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Classificação", "classificacao@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "COMPRAS", "Compras"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("6221", "Trabalhos especializados"), ("6231", "Ferramentas"), ("24323", "IVA dedutível OBS"), ("2211001", "Fornecedor A"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var vatCode = VatCode.Create(companyId, "C-OBS-NOR", "Compras OBS normal", VatRate.Normal, 23m, VatOperationNature.IvaDedutivelOutros, VatRegion.Continente);
            context.Add(vatCode);
            var party = Party.Create(companyId, "PT", Supplier, "Fornecedor A, Lda.");
            context.Add(party);

            var number = 0;
            void Purchase(string expense, decimal amount)
            {
                var reference = $"C-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 2, number), $"FT {number}", "Compra");
                var vat = decimal.Round(amount * 0.23m, 2);
                entry.AddLine(Guid.NewGuid(), ids[expense], "Gasto", amount, 0m, vatCodeId: vatCode.Id);
                entry.AddLine(Guid.NewGuid(), ids["24323"], "IVA", vat, 0m, vatCodeId: vatCode.Id);
                entry.AddLine(Guid.NewGuid(), ids["2211001"], "Fornecedor", 0m, amount + vat, partyId: party.Id);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Purchase("6221", 100m);
            Purchase("6221", 250m);
            Purchase("6231", 40m);
            context.Add(EFaturaPurchase.Create(companyId, Guid.NewGuid(), Supplier, "Fornecedor A, Lda.", "FT 99", null, new DateOnly(2026, 3, 1), "Fatura", null, null, 50m, 11.50m, 61.50m, "x.csv"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new PurchaseClassificationService(context);
            var proposals = await service.ProposeAsync(userId, companyId, [Supplier, NewSupplier], CancellationToken.None).ConfigureAwait(false);
            var proposal = proposals!.Single();
            Assert.AreEqual(Supplier, proposal.SupplierTaxId);
            Assert.AreEqual("Fornecedor A, Lda.", proposal.SupplierName);
            Assert.AreEqual(3, proposal.EntryCount);
            Assert.AreEqual("6221", proposal.Account.Code);
            Assert.AreEqual(2, proposal.Account.Count);
            Assert.AreEqual(0.67m, proposal.Confidence);
            Assert.AreEqual("C-OBS-NOR", proposal.VatCode!.Code);
            Assert.AreEqual("24323", proposal.VatAccount!.Code);
            Assert.AreEqual("2211001", proposal.SupplierAccount!.Code);
            Assert.AreEqual("COMPRAS", proposal.JournalCode);
            StringAssert.Contains(proposal.Explanation, "Em 3 compras lançadas deste fornecedor, a conta 6221");

            // Aceitar cria o lançamento-tipo e a regra; a fatura do e-Fatura passa a ter sugestão pronta.
            var suggestions = new SuggestedEntryService(context, new JournalPostingService(context));
            var rule = await service.AcceptAsync(userId, companyId, Supplier, suggestions, new EntryTemplateService(context), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("H-500584095", rule!.EntryTemplateCode);
            context.ChangeTracker.Clear();
            var report = await suggestions.ListAsync(userId, companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), CancellationToken.None).ConfigureAwait(false);
            var item = report!.Items.Single();
            Assert.IsTrue(item.Ready);
            CollectionAssert.AreEqual(new[] { "6221", "24323", "2211001" }, item.Lines.Select(line => line.AccountCode).ToArray());

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .AcceptAsync(userId, companyId, NewSupplier, suggestions, new EntryTemplateService(context), CancellationToken.None)).ConfigureAwait(false);

            // Sem histórico: a IA escolhe de entre as contas e os códigos do plano; o resto vem do uso da empresa.
            context.Add(EFaturaPurchase.Create(companyId, Guid.NewGuid(), NewSupplier, "Ferragens B, S.A.", "FT 7", null, new DateOnly(2026, 3, 2), "Fatura", null, "Outros", 20m, 4.60m, 24.60m, "x.csv"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var classifier = new FakeClassifier("6231", "c-obs-nor");
            var ai = await service.ProposeWithAiAsync(userId, companyId, NewSupplier, classifier, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(PurchaseClassificationService.AiSource, ai!.Source);
            Assert.AreEqual("6231", ai.Account.Code);
            Assert.AreEqual("C-OBS-NOR", ai.VatCode!.Code);
            Assert.AreEqual("24323", ai.VatAccount!.Code, "A conta de IVA mais usada com esse código.");
            Assert.AreEqual("2211001", ai.SupplierAccount!.Code, "A conta de fornecedores mais usada nas compras.");
            Assert.AreEqual("COMPRAS", ai.JournalCode);
            StringAssert.Contains(ai.Explanation, "confirme antes de aceitar");
            Assert.AreEqual("Ferragens B, S.A.", classifier.LastDossier!.SupplierName);
            Assert.AreEqual("Outros", classifier.LastDossier.Sector);
            Assert.IsTrue(classifier.LastDossier.Accounts.All(account => account.Code[0] is '3' or '4' or '6'), "Só as contas de gasto, inventário e investimento vão no dossiê.");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .ProposeWithAiAsync(userId, companyId, NewSupplier, new FakeClassifier("9999", null), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .ProposeWithAiAsync(userId, companyId, NewSupplier, new FakeClassifier("6231", null, configured: false), CancellationToken.None)).ConfigureAwait(false);

            // Aceitar com as contas corrigidas pelo utilizador.
            var accepted = await service.AcceptChoiceAsync(userId, companyId, NewSupplier,
                new SupplierClassificationChoice("6231", "C-OBS-NOR", "24323", "2211001", "COMPRAS"), suggestions, new EntryTemplateService(context), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("H-503504564", accepted!.EntryTemplateCode);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.AcceptChoiceAsync(userId, companyId, NewSupplier,
                new SupplierClassificationChoice("6231", "C-OBS-NOR", null, "2211001", "COMPRAS"), suggestions, new EntryTemplateService(context), CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.ProposeAsync(Guid.NewGuid(), companyId, [Supplier], CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
