using NexGenAccountingAI.Application.Fiscal;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class ObligationCalendarServiceTests
{
    [TestMethod]
    public async Task ListsTheObligationsThatApplyAndRecordsDeliveries()
    {
        var connectionString = TestDatabase.ConnectionString("Obligations");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Obrigações", "gabinete-obrigacoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Obrigações, Lda.", "OB-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Obrigações", "obrigacoes@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("12011", "Banco"), ("2421", "Retenções"), ("6311", "Remunerações"), ("7111", "Vendas") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var intraCommunity = VatCode.Create(companyId, "V-ICB", "Transmissão intracomunitária", VatRate.Isento, 0m, VatOperationNature.TransmissoesIntracomunitariasBens, VatRegion.Continente);
            context.Add(intraCommunity);

            // Março: salários com retenção e uma venda intracomunitária.
            var payroll = new JournalEntry(Guid.NewGuid(), companyId, journalId, "S-1", new DateOnly(2026, 3, 31), "Salários março", "Salários");
            payroll.AddLine(Guid.NewGuid(), ids["6311"], "Remunerações", 1000m, 0m);
            payroll.AddLine(Guid.NewGuid(), ids["2421"], "Retenção de IRS", 0m, 100m);
            payroll.AddLine(Guid.NewGuid(), ids["12011"], "Pagamento", 0m, 900m);
            payroll.Complete(JournalEntryStatus.Reviewed);
            var sale = new JournalEntry(Guid.NewGuid(), companyId, journalId, "V-1", new DateOnly(2026, 3, 15), "FT 1", "Venda para Espanha");
            sale.AddLine(Guid.NewGuid(), ids["12011"], "Recebimento", 500m, 0m);
            sale.AddLine(Guid.NewGuid(), ids["7111"], "Venda", 0m, 500m, vatCodeId: intraCommunity.Id);
            sale.Complete(JournalEntryStatus.Reviewed);
            context.AddRange(payroll, sale);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new ObligationCalendarService(context);
            var calendar = await service.CalendarAsync(userId, companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31), CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(calendar);
            Assert.AreEqual("Mensal", calendar.VatPeriodicity, "Sem credenciais da AT, assume-se o IVA mensal.");
            Assert.IsFalse(calendar.VatPeriodicityConfigured);
            string[] Codes(string periodKey) => calendar.Rows.Where(row => row.PeriodKey == periodKey).Select(row => row.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(
                new[] { ObligationCatalog.InvoiceCommunication, ObligationCatalog.MonthlyPayroll, ObligationCatalog.VatReturnMonthly, ObligationCatalog.VatPaymentMonthly, ObligationCatalog.RecapitulativeMonthly, ObligationCatalog.Withholdings },
                Codes("2026-03"),
                "Março: DMR, retenções e recapitulativa (há operações) e o IVA de março, que vence em maio.");
            CollectionAssert.AreEqual(new[] { ObligationCatalog.VatReturnMonthly, ObligationCatalog.VatPaymentMonthly }, Codes("2026-02"), "Fevereiro: só o IVA, sem salários nem vendas intracomunitárias.");
            Assert.AreEqual(new DateOnly(2026, 5, 31), calendar.Rows.Single(row => row.Code == ObligationCatalog.Modelo22).DueDate);
            Assert.IsFalse(calendar.Rows.Any(row => row.Code is ObligationCatalog.VatReturnQuarterly or ObligationCatalog.RecapitulativeQuarterly or ObligationCatalog.StampDuty));

            // Com «todas», aparecem também as que os dados não indicam — o Imposto do Selo regista-se à mão.
            var all = await service.CalendarAsync(userId, companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31), CancellationToken.None, includeAll: true).ConfigureAwait(false);
            var stamp = all!.Rows.First(row => row.Code == ObligationCatalog.StampDuty);
            Assert.AreEqual("Os lançamentos não a indicam", stamp.Reason);
            Assert.IsTrue(calendar.Rows.All(row => row.State == "Pendente"));
            Assert.IsTrue(calendar.Rows.Single(row => row.Code == ObligationCatalog.MonthlyPayroll).Overdue(new DateOnly(2026, 4, 11)));

            // Entregar e prorrogar.
            Assert.IsTrue(await service.UpdateAsync(userId, companyId, "iva-dp-m", "2026-03", new ObligationUpdate("entregue", new DateOnly(2026, 5, 18), "Comprovativo 123", null), CancellationToken.None).ConfigureAwait(false));
            Assert.IsTrue(await service.UpdateAsync(userId, companyId, ObligationCatalog.Modelo22, "2025", new ObligationUpdate("Pendente", null, "Prorrogado por despacho", new DateOnly(2026, 6, 30)), CancellationToken.None).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UpdateAsync(userId, companyId, ObligationCatalog.Ies, "2025", new ObligationUpdate("Entregue", null, null, null), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UpdateAsync(userId, companyId, ObligationCatalog.Ies, "2025", new ObligationUpdate("Feita", null, null, null), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .UpdateAsync(userId, companyId, "NAO-HA", "2025", new ObligationUpdate("Pendente", null, null, null), CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var after = await service.CalendarAsync(userId, companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), CancellationToken.None).ConfigureAwait(false);
            var vat = after!.Rows.Single(row => row.Code == ObligationCatalog.VatReturnMonthly && row.PeriodKey == "2026-03");
            Assert.AreEqual("Entregue", vat.State);
            Assert.AreEqual(new DateOnly(2026, 5, 18), vat.DeliveredOn);
            Assert.AreEqual("Comprovativo 123", vat.Note);
            var modelo22 = after.Rows.Single(row => row.Code == ObligationCatalog.Modelo22);
            Assert.AreEqual(new DateOnly(2026, 6, 30), modelo22.DueDate);
            Assert.AreEqual(new DateOnly(2026, 5, 31), modelo22.LegalDueDate);
            Assert.IsTrue(modelo22.DueDateChanged);

            var may = await service.CalendarAsync(userId, companyId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(may!.Rows.Any(row => row.Code == ObligationCatalog.Modelo22), "Prorrogado para junho, sai de maio.");

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.CalendarAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 1), CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.CalendarAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
