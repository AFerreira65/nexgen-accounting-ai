using NexGenAccountingAI.Application.CrossCheck;
using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class ManagementReportDataServiceTests
{
    [TestMethod]
    public async Task BuildsDossierWithHomologousDeltaRatiosAndTopMovements()
    {
        var connectionString = TestDatabase.ConnectionString("Dossier");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Dossiê", "gabinete-dossie"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Dossiê, Lda.", "DOSSIE-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Dossiê", "dossie@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var sales = Guid.NewGuid();
            var services = Guid.NewGuid();
            var staff = Guid.NewGuid();
            var client = Guid.NewGuid();
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(services, companyId, "6221", "Serviços especializados"));
            context.Add(new LedgerAccount(staff, companyId, "6321", "Remunerações"));
            context.Add(new LedgerAccount(client, companyId, "2111001", "Cliente A"));

            // Ano anterior (2025): rendimentos 1000, FSE 200.
            context.Add(Entry(companyId, journalId, "P-1", new DateOnly(2025, 6, 10), "FT 2025/1",
                (client, 1000m, 0m), (sales, 0m, 1000m)));
            context.Add(Entry(companyId, journalId, "P-2", new DateOnly(2025, 6, 12), "FC 2025/1",
                (services, 200m, 0m), (client, 0m, 200m)));

            // Ano corrente (2026): rendimentos 1500 (um movimento grande), FSE 300, pessoal 400.
            context.Add(Entry(companyId, journalId, "C-1", new DateOnly(2026, 6, 10), "FT 2026/1",
                (client, 1500m, 0m), (sales, 0m, 1500m)));
            context.Add(Entry(companyId, journalId, "C-2", new DateOnly(2026, 6, 12), "FC 2026/1",
                (services, 300m, 0m), (client, 0m, 300m)));
            context.Add(Entry(companyId, journalId, "C-3", new DateOnly(2026, 6, 28), "REC 2026/6",
                (staff, 400m, 0m), (client, 0m, 400m)));

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new ManagementReportDataService(
                context,
                new AccountLedgerService(context),
                new CrossCheckService(context));

            var request = new ManagementReportRequest(
                ManagementReportKind.SinteseMensal, null,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

            var dossier = await service
                .BuildDossierAsync(userId, companyId, request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(dossier);
            Assert.AreEqual("Empresa Dossiê, Lda.", dossier!.CompanyName);
            Assert.IsTrue(dossier.HasComparative, "O exercício de 2025 existe.");
            Assert.AreEqual(new DateOnly(2025, 1, 1), dossier.ComparativeFrom);

            // Resultados: rendimentos 1500 (homólogo 1000), gastos 700 (homólogo 200), resultado 800 (homólogo 800).
            var income = dossier.IncomeSummary.Single(row => row.Label.StartsWith("Rendimentos"));
            Assert.AreEqual(1500m, income.Current);
            Assert.AreEqual(1000m, income.Comparative);
            var expense = dossier.IncomeSummary.Single(row => row.Label.StartsWith("Gastos"));
            Assert.AreEqual(700m, expense.Current);
            var result = dossier.IncomeSummary.Single(row => row.Label.StartsWith("Resultado"));
            Assert.AreEqual(800m, result.Current);
            Assert.AreEqual(800m, result.Comparative);

            // Homólogo por conta: as vendas subiram 500.
            var salesDelta = dossier.TrialBalanceDelta.Single(row => row.AccountCode == "7111");
            Assert.AreEqual(-1500m, salesDelta.Current, "7111 move-se a crédito (débito − crédito).");
            Assert.AreEqual(-500m, salesDelta.Delta);

            // Rácios: margem líquida = 800 / 1500; peso do pessoal = 400 / 1500.
            var margin = dossier.Ratios.Single(ratio => ratio.Key == "margem-liquida");
            Assert.AreEqual(decimal.Round(800m / 1500m, 4), margin.Value);
            var personnel = dossier.Ratios.Single(ratio => ratio.Key == "peso-pessoal");
            Assert.AreEqual(decimal.Round(400m / 1500m, 4), personnel.Value);

            // Maiores movimentos: o de 1500 vem primeiro e cita a conta 7111.
            Assert.AreEqual("C-1", dossier.TopMovements[0].Reference);
            Assert.AreEqual(1500m, dossier.TopMovements[0].Amount);
            CollectionAssert.Contains(dossier.TopMovements[0].AccountCodes.ToArray(), "7111");

            // Há lançamentos de compra mas nenhuma fatura do e-Fatura no período.
            Assert.IsTrue(dossier.CrossCheck.Available);
            Assert.AreEqual(0, dossier.CrossCheck.UnbookedInvoiceCount);
            Assert.IsTrue(dossier.CrossCheck.PeriodBalanced, "Débito = crédito no período.");

            // Sem acesso → null.
            Assert.IsNull(await service
                .BuildDossierAsync(Guid.NewGuid(), companyId, request, CancellationToken.None)
                .ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private static JournalEntry Entry(
        Guid companyId, Guid journalId, string reference, DateOnly date, string document,
        params (Guid AccountId, decimal Debit, decimal Credit)[] lines)
    {
        var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, document, $"Lançamento {reference}");
        foreach (var line in lines)
        {
            entry.AddLine(Guid.NewGuid(), line.AccountId, "Linha", line.Debit, line.Credit);
        }

        entry.Complete(JournalEntryStatus.Reviewed);
        return entry;
    }
}
