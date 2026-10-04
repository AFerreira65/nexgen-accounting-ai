using NexGenAccountingAI.Application.Banking;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class BankEntryServiceTests
{
    private sealed class FakeReader(BankStatementFile file) : IBankStatementReader
    {
        public Task<BankStatementFile> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(file);
    }

    [TestMethod]
    public async Task PostsStatementLinesByDescriptionRulesAndReconcilesThem()
    {
        var connectionString = TestDatabase.ConnectionString("BankRules");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Regras", "gabinete-regras"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Regras, Lda.", "BR-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Regras", "regras@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "BANCOS", "Bancos"));
            foreach (var (code, name) in new[] { ("12011", "Banco A"), ("12099", "Outro banco"), ("6878", "Serviços bancários"), ("7911", "Juros obtidos") })
            {
                context.Add(new LedgerAccount(Guid.NewGuid(), companyId, code, name));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var templates = new EntryTemplateService(context);
            await templates.SaveAsync(userId, companyId, "COMISSAO", new EntryTemplateInput(
                "Comissão bancária", "BANCOS", null, "Comissão bancária", true,
                [
                    new EntryTemplateLineInput(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, "6878", null, null),
                    new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, "12099", null, null),
                ]), CancellationToken.None).ConfigureAwait(false);
            await templates.SaveAsync(userId, companyId, "JUROS", new EntryTemplateInput(
                "Juros", "BANCOS", null, null, true,
                [
                    new EntryTemplateLineInput(EntryTemplateLineKind.Base, EntryTemplateSide.Credit, "7911", null, null),
                    new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Debit, "12099", null, null),
                ]), CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            BankStatementRow Row(int line, int day, string description, decimal amount) =>
                new(line, new DateOnly(2026, 3, day), null, description, amount, null, null, null);
            var file = new BankStatementFile("CSV", null, null, null, null,
            [
                Row(1, 3, "Comissão manutenção conta", -7.50m),
                Row(2, 10, "JUROS CREDORES", 1.20m),
                Row(3, 12, "CAFE", -2m),
                Row(4, 15, "ESTORNO COMISSAO", 3m),
            ], []);
            await new BankStatementService(new FakeReader(file), context)
                .ImportAsync(userId, companyId, "12011", Stream.Null, "marco.csv", false, CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new BankEntryService(context, new JournalPostingService(context));
            var rule = await service.SaveRuleAsync(userId, companyId, " comissão ", "comissao", true, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("COMISSAO", rule!.Pattern, "O texto fica em maiúsculas e sem acentos.");
            await service.SaveRuleAsync(userId, companyId, "juros", "JUROS", true, CancellationToken.None).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveRuleAsync(userId, companyId, "ab", "JUROS", true, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveRuleAsync(userId, companyId, "selo", "NAO-HA", true, CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var from = new DateOnly(2026, 3, 1);
            var to = new DateOnly(2026, 3, 31);
            var suggestions = await service.SuggestAsync(userId, companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(suggestions);
            CollectionAssert.AreEqual(new[] { "Comissão manutenção conta", "JUROS CREDORES", "ESTORNO COMISSAO" }, suggestions.Select(item => item.Description).ToArray());

            var commission = suggestions[0];
            Assert.AreEqual("Comissão bancária", commission.EntryDescription);
            Assert.AreEqual(7.50m, commission.Lines.Single(line => line.AccountCode == "6878").Debit);
            Assert.AreEqual(7.50m, commission.Lines.Single(line => line.AccountCode == "12011").Credit, "A contrapartida é a conta do extrato, não a do lançamento-tipo.");
            var refund = suggestions[2];
            Assert.AreEqual(3m, refund.Lines.Single(line => line.AccountCode == "6878").Credit, "Uma entrada inverte o lançamento-tipo escrito para uma saída.");
            Assert.AreEqual(3m, refund.Lines.Single(line => line.AccountCode == "12011").Debit);
            Assert.AreEqual(1.20m, suggestions[1].Lines.Single(line => line.AccountCode == "12011").Debit);

            var lineIds = await context.BankStatementLines.AsNoTracking().OrderBy(line => line.BookingDate).Select(line => line.Id).ToListAsync().ConfigureAwait(false);
            var outcomes = await service.PostAsync(userId, companyId, lineIds, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(outcomes);
            Assert.AreEqual(3, outcomes.Count(item => item.Reference is not null));
            Assert.AreEqual("Nenhuma regra se aplica ao descritivo do movimento.", outcomes.Single(item => item.DocumentId == lineIds[2]).Error);
            context.ChangeTracker.Clear();

            var reference = outcomes.Single(item => item.DocumentId == lineIds[0]).Reference!;
            var posted = await context.JournalEntries.AsNoTracking().SingleAsync(entry => entry.Reference == reference).ConfigureAwait(false);
            Assert.AreEqual(EntrySource.BankStatement, posted.Source);
            Assert.AreEqual(JournalEntryStatus.PendingReview, posted.Status);
            Assert.AreEqual(new DateOnly(2026, 3, 3), posted.EntryDate);
            Assert.AreEqual(reference, (await context.BankStatementLines.AsNoTracking().SingleAsync(line => line.Id == lineIds[0]).ConfigureAwait(false)).ReconciledEntryReference,
                "O movimento fica conciliado com o lançamento criado.");
            Assert.AreEqual(0, (await service.SuggestAsync(userId, companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false))!.Count);

            var again = await service.PostAsync(userId, companyId, [lineIds[0]], CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("O movimento já está conciliado.", again!.Single().Error);

            Assert.AreEqual(2, (await service.ListRulesAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false))!.Count);
            Assert.IsTrue(await service.DeleteRuleAsync(userId, companyId, "Juros", CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.SuggestAsync(Guid.NewGuid(), companyId, "12011", from, to, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
