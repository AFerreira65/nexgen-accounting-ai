using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>O rascunho de listagens com IA (F4D-05): o acesso, o interruptor da empresa e a chave.</summary>
[TestClass]
public sealed class ListingDraftServiceTests
{
    [TestMethod]
    public async Task DraftsOnlyForMembersWithTheCompanySwitchOnAndAKey()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("ListingDraft"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete IA", "gabinete-ia"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa IA, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista IA", "ia@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var drafter = new FakeDrafter();
            var service = new ListingDraftService(context, drafter, TimeProvider.System);
            var none = CancellationToken.None;

            Assert.IsNull(await service.DraftAsync(Guid.NewGuid(), companyId, "vendas", none).ConfigureAwait(false), "Sem acesso à empresa.");
            StringAssert.Contains((await service.DraftAsync(userId, companyId, "   ", none).ConfigureAwait(false))!.Refusal, "Escreva");
            StringAssert.Contains((await service.DraftAsync(userId, companyId, new string('x', 1001), none).ConfigureAwait(false))!.Refusal, "1000");

            // O interruptor da empresa está desligado por omissão: nada chega ao modelo.
            StringAssert.Contains((await service.DraftAsync(userId, companyId, "vendas por cliente", none).ConfigureAwait(false))!.Refusal, "desligada");
            Assert.AreEqual(0, drafter.Calls);

            var settings = CompanyAiSettings.Create(companyId);
            settings.Update(true, false);
            context.Add(settings);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            drafter.Configured = false;
            StringAssert.Contains((await service.DraftAsync(userId, companyId, "vendas por cliente", none).ConfigureAwait(false))!.Refusal, "chave");
            Assert.AreEqual(0, drafter.Calls);

            drafter.Configured = true;
            var outcome = await service.DraftAsync(userId, companyId, "  vendas por cliente em 2026  ", none).ConfigureAwait(false);
            Assert.IsNull(outcome!.Refusal);
            Assert.AreEqual("Vendas por cliente", outcome.Draft!.Name);
            Assert.AreEqual(0, ListingCatalog.Validate(outcome.Draft.Definition).Count);
            Assert.AreEqual("vendas por cliente em 2026", drafter.LastRequest, "O pedido vai aparado e é tudo o que vai.");
            Assert.AreEqual(1, drafter.Calls);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class FakeDrafter : IListingDrafter
    {
        public bool Configured { get; set; } = true;

        public int Calls { get; private set; }

        public string? LastRequest { get; private set; }

        public bool IsConfigured => Configured;

        public string Model => "modelo-de-teste";

        public Task<ListingDraft> DraftAsync(string request, DateOnly today, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            var template = ListingTemplates.Find("vendas-por-cliente")!;
            return Task.FromResult(new ListingDraft("Vendas por cliente", "Agrupa as vendas por cliente.", template.Definition, [], Model, 100, 50));
        }
    }
}
