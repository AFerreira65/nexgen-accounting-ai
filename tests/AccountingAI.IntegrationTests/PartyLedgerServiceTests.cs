using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class PartyLedgerServiceTests
{
    [TestMethod]
    public async Task BalancesAndStatementFollowThePartyOnTheLines()
    {
        var connectionString = TestDatabase.ConnectionString("PartyLedger");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Extrato", "gabinete-extrato"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Extrato, Lda.", "EX-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Extrato", "extrato@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var receivable = Guid.NewGuid();
            var payable = Guid.NewGuid();
            var sales = Guid.NewGuid();
            var bank = Guid.NewGuid();
            context.Add(new LedgerAccount(receivable, companyId, "21111", "Clientes c/c"));
            context.Add(new LedgerAccount(payable, companyId, "22111", "Fornecedores c/c"));
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(bank, companyId, "1201", "Banco"));

            var customer = Party.Create(companyId, "PT", "500584095", "Cliente A, Lda.");
            customer.LinkAccounts(receivable, null);
            var supplier = Party.Create(companyId, "ES", "B12345678", "Proveedor B, S.L.");
            var idle = Party.Create(companyId, "PT", "509999999", "Sem movimento, Lda.");
            context.AddRange(customer, supplier, idle);

            // Cliente: fatura em janeiro (100), fatura em março (50), recebimento em março (120), e um anulado.
            context.Add(Entry(companyId, journalId, "V-1", new DateOnly(2026, 1, 10), false, (receivable, 100m, 0m, customer.Id), (sales, 0m, 100m, null)));
            context.Add(Entry(companyId, journalId, "V-2", new DateOnly(2026, 3, 5), false, (receivable, 50m, 0m, customer.Id), (sales, 0m, 50m, null)));
            context.Add(Entry(companyId, journalId, "R-1", new DateOnly(2026, 3, 20), false, (bank, 120m, 0m, null), (receivable, 0m, 120m, customer.Id)));
            context.Add(Entry(companyId, journalId, "V-X", new DateOnly(2026, 3, 25), true, (receivable, 999m, 0m, customer.Id), (sales, 0m, 999m, null)));
            // Fornecedor: uma fatura a pagar.
            context.Add(Entry(companyId, journalId, "C-1", new DateOnly(2026, 2, 1), false, (sales, 80m, 0m, null), (payable, 0m, 80m, supplier.Id)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new PartyLedgerService(context);

            // Saldos: só os terceiros com movimento, sem o lançamento anulado.
            var balances = await service.BalancesAsync(userId, companyId, null, null, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(balances);
            Assert.AreEqual(2, balances!.Total);
            var customerRow = balances.Items.Single(row => row.PartyId == customer.Id);
            Assert.AreEqual(150m, customerRow.Debit);
            Assert.AreEqual(120m, customerRow.Credit);
            Assert.AreEqual(30m, customerRow.Balance);
            Assert.AreEqual(-80m, balances.Items.Single(row => row.PartyId == supplier.Id).Balance);
            Assert.AreEqual(-50m, balances.TotalBalance);

            // Até uma data, com pesquisa e paginação.
            var atFebruary = await service.BalancesAsync(userId, companyId, new DateOnly(2026, 2, 28), "5005", 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(100m, atFebruary!.Items.Single().Balance);
            var secondPage = await service.BalancesAsync(userId, companyId, null, null, 2, 1, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(2, secondPage!.Total);
            Assert.AreEqual(supplier.Id, secondPage.Items.Single().PartyId);

            // Extrato de março: saldo anterior 100, duas linhas, saldo final 30.
            var march = await service
                .StatementAsync(userId, companyId, customer.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 1, 50, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(march);
            Assert.AreEqual(100m, march!.OpeningBalance);
            Assert.AreEqual(50m, march.PeriodDebit);
            Assert.AreEqual(120m, march.PeriodCredit);
            Assert.AreEqual(30m, march.ClosingBalance);
            Assert.AreEqual(2, march.Total);
            CollectionAssert.AreEqual(new[] { "V-2", "R-1" }, march.Lines.Select(line => line.Reference).ToArray());
            CollectionAssert.AreEqual(new[] { 150m, 30m }, march.Lines.Select(line => line.Balance).ToArray());

            // A segunda página continua o saldo acumulado da primeira.
            var full = await service.StatementAsync(userId, companyId, customer.Id, null, null, 2, 2, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(3, full!.Total);
            Assert.AreEqual(0m, full.OpeningBalance);
            Assert.AreEqual("R-1", full.Lines.Single().Reference);
            Assert.AreEqual(30m, full.Lines.Single().Balance);

            // Terceiro sem movimento, desconhecido e sem acesso.
            var empty = await service.StatementAsync(userId, companyId, idle.Id, null, null, 1, 50, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, empty!.Total);
            Assert.AreEqual(0m, empty.ClosingBalance);
            Assert.IsNull(await service.StatementAsync(userId, companyId, Guid.NewGuid(), null, null, 1, 50, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.BalancesAsync(Guid.NewGuid(), companyId, null, null, 1, 50, CancellationToken.None).ConfigureAwait(false));

            // Terceiros por subconta, para o editor propor o terceiro.
            var byAccount = await new PartyService(context)
                .ListAsync(userId, companyId, null, PartyRoleFilter.Todos, false, 1, 50, CancellationToken.None, "21111")
                .ConfigureAwait(false);
            Assert.AreEqual(customer.Id, byAccount!.Items.Single().Id);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private static JournalEntry Entry(
        Guid companyId, Guid journalId, string reference, DateOnly date, bool voided,
        params (Guid AccountId, decimal Debit, decimal Credit, Guid? PartyId)[] lines)
    {
        var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
        foreach (var line in lines)
        {
            entry.AddLine(Guid.NewGuid(), line.AccountId, "Linha", line.Debit, line.Credit, line.PartyId);
        }

        entry.Complete(JournalEntryStatus.PendingReview);
        if (voided)
        {
            entry.Void();
        }

        return entry;
    }
}
