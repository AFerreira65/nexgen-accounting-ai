using AccountingAI.Application.ManagementReports;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class ManagementReportServiceTests
{
    [TestMethod]
    public async Task GeneratesOnlyWhenEnabledPersistsAndListsTheReport()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_MgmtReport_{Guid.NewGuid():N}";
        var configured = Environment.GetEnvironmentVariable("ACCOUNTINGAI_TEST_SQL_CONNECTION");
        var builder = string.IsNullOrWhiteSpace(configured)
            ? new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            }
            : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = databaseName;

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure"))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete IA", "gabinete-ia"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa IA, Lda.", "IA-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador IA", "ia@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            var sales = Guid.NewGuid();
            var client = Guid.NewGuid();
            context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(client, companyId, "2111001", "Cliente A"));
            context.Add(Entry(companyId, journalId, "V-1", new DateOnly(2026, 6, 10), "FT 1",
                (client, 1000m, 0m), (sales, 0m, 1000m)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var narrator = new StubNarrator();
            var data = new ManagementReportDataService(
                context, new AccountLedgerService(context), new CrossCheckService(context));
            var reports = new ManagementReportService(context, data, narrator);
            var settings = new CompanyAiSettingsService(context, narrator);

            var request = new ManagementReportRequest(
                ManagementReportKind.SinteseMensal, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

            // Interruptor desligado por omissão → indisponível.
            var settingsView = await settings.GetAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(settingsView);
            Assert.IsFalse(settingsView!.Enabled);
            Assert.IsTrue(settingsView.NarratorConfigured);

            var blocked = await reports.GenerateAsync(userId, companyId, request, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(blocked.Disabled);
            StringAssert.Contains(blocked.DisabledReason!, "desligados");

            // Ligar e gerar.
            var saved = await settings.SaveAsync(userId, companyId, enabled: true, autoMonthly: false, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(saved!.Enabled);

            var generated = await reports.GenerateAsync(userId, companyId, request, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(generated.Disabled);
            Assert.IsFalse(generated.NotFound);
            Assert.IsNotNull(generated.View);
            StringAssert.Contains(generated.View!.Result!.Summary, "Empresa IA, Lda.");
            Assert.AreEqual("modelo-de-teste", generated.View.Model);
            Assert.AreEqual(1234, generated.View.PromptTokens);
            Assert.AreEqual(ManagementReportResult.Notice, generated.View.Result!.AiNotice);
            Assert.AreEqual("sintese-mensal", generated.View.Kind);

            Assert.AreEqual(1, await context.ManagementReports.CountAsync().ConfigureAwait(false));

            var history = await reports.ListAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(history);
            Assert.AreEqual(1, history!.Count);
            Assert.IsNotNull(history[0].Result);
            Assert.IsNull(history[0].Dossier);

            var full = await reports.GetAsync(userId, companyId, generated.View.Id, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(full);
            Assert.IsTrue(full!.Dossier.HasValue);
            Assert.IsTrue(full.Dossier!.Value.TryGetProperty("companyName", out _));

            // Pergunta livre.
            var freeform = await reports.GenerateAsync(
                userId, companyId,
                new ManagementReportRequest(null, "Como está a tesouraria?", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("pergunta", freeform.View!.Kind);
            Assert.AreEqual("Como está a tesouraria?", freeform.View.Question);

            // Sem acesso.
            var noAccess = await reports.GenerateAsync(Guid.NewGuid(), companyId, request, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(noAccess.NotFound);
            Assert.IsNull(await settings.GetAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private sealed class StubNarrator : IManagementReportNarrator
    {
        public bool IsConfigured => true;

        public string Model => "modelo-de-teste";

        public Task<ManagementReportNarration> GenerateAsync(
            ManagementReportRequest request, ManagementReportDossier dossier, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManagementReportNarration(
                new ManagementReportResult(
                    $"Resumo de {dossier.CompanyName}.",
                    [
                        new ManagementReportSection(
                            "Resultados", "O resultado do período foi positivo.",
                            [new ManagementReportReference("conta", "7111")]),
                    ],
                    ["Confirmar o corte de proveitos."],
                    [new ManagementReportIndicator("Margem", "12%", null)],
                    ManagementReportResult.Notice),
                "modelo-de-teste",
                1234,
                567));
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
