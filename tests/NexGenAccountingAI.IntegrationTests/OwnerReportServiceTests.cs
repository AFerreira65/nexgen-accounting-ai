using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class OwnerReportServiceTests
{
    [TestMethod]
    public async Task WritesTheMonthForTheOwnerWithFactualAlerts()
    {
        var connectionString = TestDatabase.ConnectionString("OwnerReport");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Empresário", "gabinete-empresario"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Empresário, Lda.", "OW-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Empresário", "empresario@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("12011", "Banco"), ("2111", "Clientes"), ("2211", "Fornecedores"), ("6221", "Trabalhos especializados"), ("7211", "Serviços"), ("56", "Resultados transitados") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var customer = Party.Create(companyId, "PT", "500584095", "Cliente Antigo, Lda.");
            context.Add(customer);
            var number = 0;
            void Entry(int year, int month, int day, string debit, string credit, decimal amount, Party? party = null)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, $"G-{++number}", new DateOnly(year, month, day), $"D-{number}", "Lançamento");
                entry.AddLine(Guid.NewGuid(), ids[debit], "Débito", amount, 0m, partyId: debit == "2111" ? party?.Id : null);
                entry.AddLine(Guid.NewGuid(), ids[credit], "Crédito", 0m, amount);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry(2025, 3, 10, "12011", "7211", 1000m);             // março do ano anterior: 1 000 de vendas
            Entry(2025, 11, 20, "2111", "7211", 300m, customer);    // fatura de novembro ainda por receber
            // A abertura de 2026 traz os saldos do banco e dos clientes (sem terceiro, como o apuramento os gera).
            var opening = new JournalEntry(Guid.NewGuid(), companyId, journalId, "AB-2026", new DateOnly(2026, 1, 1), "Abertura", "Abertura");
            opening.SetSource(EntrySource.Opening);
            opening.AddLine(Guid.NewGuid(), ids["12011"], "Banco", 1000m, 0m);
            opening.AddLine(Guid.NewGuid(), ids["2111"], "Clientes", 300m, 0m);
            opening.AddLine(Guid.NewGuid(), ids["56"], "Resultados transitados", 0m, 1300m);
            opening.Complete(JournalEntryStatus.Reviewed);
            context.Add(opening);
            Entry(2026, 1, 15, "12011", "7211", 900m);
            Entry(2026, 1, 20, "6221", "12011", 300m);
            Entry(2026, 3, 10, "12011", "7211", 500m);             // março: metade das vendas
            Entry(2026, 3, 25, "6221", "2211", 700m);              // e um gasto que dá prejuízo
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new OwnerReportService(new FinancialStatementsService(context), new PartyAgingService(context), new BudgetService(context));
            var report = await service.BuildAsync(userId, companyId, 2026, 3, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(report);
            Assert.AreEqual("Março de 2026", report.MonthLabel);
            Assert.AreEqual(500m, report.MonthFigures[0].Value);
            Assert.AreEqual(1000m, report.MonthFigures[0].Comparative);
            Assert.AreEqual(-200m, report.MonthFigures[2].Value);
            Assert.AreEqual(1400m, report.YearFigures[0].Value, "Janeiro e março.");
            Assert.AreEqual(400m, report.YearFigures[2].Value);
            Assert.AreEqual(2100m, report.Position[0].Value, "A tesouraria no fim de março.");
            Assert.AreEqual(300m, report.Position[1].Value);
            StringAssert.Contains(report.Highlights[0], "Em março de 2026, os rendimentos foram de 500,00 €, menos 50 % face ao mesmo mês do ano anterior.");
            Assert.IsTrue(report.Alerts.Any(alert => alert.Text.StartsWith("O mês fechou com prejuízo de 200,00 €", StringComparison.Ordinal)));
            Assert.IsTrue(report.Alerts.Any(alert => alert.Text.Contains("caíram 50 %", StringComparison.Ordinal)));
            Assert.IsTrue(report.Alerts.Any(alert => alert.Text.Contains("300,00 € de clientes por receber há mais de 90 dias", StringComparison.Ordinal)));
            Assert.IsNull(report.Budget, "Sem orçamento, o relatório não o mostra.");

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, 2026, 13, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, 2026, 3, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
