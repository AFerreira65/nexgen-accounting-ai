using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>O histórico de alterações dos lançamentos (F4-03).</summary>
[TestClass]
public sealed class JournalEntryHistoryTests
{
    private sealed class FixedUser(Guid userId) : ICurrentUserAccessor
    {
        public Guid? UserId { get; set; } = userId;
    }

    [TestMethod]
    public async Task EveryChangeToAnExistingEntryKeepsThePreviousVersion()
    {
        var userId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("EntryHistory"), TestDatabase.Configure)
            .AddInterceptors(new JournalEntryHistoryInterceptor(new FixedUser(userId)))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Histórico", "gabinete-historico"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Histórico, Lda.", "HI-PT-0001", TaxRegion.Mainland));
            context.Add(new UserAccount(userId, "Contabilista Histórico", "historico@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "GERAL", "Geral"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "111", "Caixa"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "6221", "Trabalhos especializados"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "6222", "Publicidade"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var posting = new JournalPostingService(context);
            var history = new JournalEntryHistoryService(context);
            var none = CancellationToken.None;

            var posted = await posting.PostAsync(companyId, "GERAL", new DateOnly(2026, 3, 1), "FT 1", "Serviço", false,
                [new JournalEntryLineInput("6221", "Serviço", 100m, 0m), new JournalEntryLineInput("111", "Caixa", 0m, 100m)], none).ConfigureAwait(false);
            var reference = posted.Reference;
            Assert.HasCount(0, (await history.ListAsync(userId, companyId, reference, none).ConfigureAwait(false))!, "Criar não gera histórico.");

            // Correção: outra conta e outro valor. O histórico guarda como estava.
            await posting.ReviseAsync(companyId, reference, "GERAL", new DateOnly(2026, 3, 2), "FT 1-A", "Publicidade", false,
                [new JournalEntryLineInput("6222", "Anúncio", 120m, 0m), new JournalEntryLineInput("111", "Caixa", 0m, 120m)], none).ConfigureAwait(false);
            await posting.MarkReviewedAsync(companyId, reference, none).ConfigureAwait(false);
            await posting.ReopenAsync(companyId, reference, none).ConfigureAwait(false);
            await posting.VoidAsync(companyId, reference, none).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var rows = (await history.ListAsync(userId, companyId, reference, none).ConfigureAwait(false))!;
            CollectionAssert.AreEqual(
                new[] { "Voided", "Reopened", "Reviewed", "Removed" },
                rows.Select(row => row.Action).ToArray(),
                "Da mais recente para a mais antiga.");
            Assert.IsTrue(rows.All(row => row.ChangedByName == "Contabilista Histórico"));

            var original = rows[^1].Before;
            Assert.AreEqual(new DateOnly(2026, 3, 1), original.EntryDate);
            Assert.AreEqual("FT 1", original.DocumentReference);
            Assert.AreEqual("GERAL", original.JournalCode);
            Assert.AreEqual(100m, original.TotalAmount);
            CollectionAssert.AreEqual(new[] { "6221", "111" }, original.Lines.Select(line => line.AccountCode).ToArray());
            Assert.AreEqual(100m, original.Lines[0].Debit);
            Assert.AreEqual("Trabalhos especializados", original.Lines[0].AccountName);

            var beforeReview = rows[2].Before;
            Assert.AreEqual("PendingReview", beforeReview.Status);
            Assert.AreEqual("FT 1-A", beforeReview.DocumentReference, "Já é a versão corrigida.");
            CollectionAssert.AreEqual(new[] { "6222", "111" }, beforeReview.Lines.Select(line => line.AccountCode).ToArray());
            Assert.AreEqual("Reviewed", rows[1].Before.Status);
            Assert.AreEqual("PendingReview", rows[0].Before.Status);

            Assert.IsNull(await history.ListAsync(Guid.NewGuid(), companyId, reference, none).ConfigureAwait(false), "Sem acesso à empresa.");
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
