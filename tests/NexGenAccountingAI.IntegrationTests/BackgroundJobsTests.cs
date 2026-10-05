using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>As tarefas automáticas (F4-05): a reserva de cada unidade de trabalho e a síntese mensal com IA.</summary>
[TestClass]
public sealed class BackgroundJobsTests
{
    [TestMethod]
    public async Task AUnitOfWorkRunsOnceAndFailedOnesAreRetriedAFewTimes()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("JobLog"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var log = new BackgroundJobLog(context);
            var start = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
            var company = Guid.NewGuid();

            var run = await log.TryBeginAsync("tarefa", company, "2026-09", start).ConfigureAwait(false);
            Assert.IsNotNull(run);
            Assert.IsNull(await log.TryBeginAsync("tarefa", company, "2026-09", start.AddMinutes(5)).ConfigureAwait(false), "Está a correr.");
            Assert.IsNotNull(await log.TryBeginAsync("tarefa", company, "2026-10", start).ConfigureAwait(false), "Outro período é outra unidade.");
            Assert.IsNotNull(await log.TryBeginAsync("tarefa", Guid.NewGuid(), "2026-09", start).ConfigureAwait(false), "Outra empresa também.");

            // Falhou: só volta a tentar passada uma hora, e no máximo três vezes.
            await log.FinishAsync(run, BackgroundJobStatus.Failed, start.AddMinutes(1), "Erro de rede").ConfigureAwait(false);
            Assert.IsNull(await log.TryBeginAsync("tarefa", company, "2026-09", start.AddMinutes(30)).ConfigureAwait(false));
            var second = await log.TryBeginAsync("tarefa", company, "2026-09", start.AddHours(2)).ConfigureAwait(false);
            Assert.AreEqual(2, second!.Attempts);
            await log.FinishAsync(second, BackgroundJobStatus.Failed, start.AddHours(2), "Erro de rede").ConfigureAwait(false);
            var third = await log.TryBeginAsync("tarefa", company, "2026-09", start.AddHours(4)).ConfigureAwait(false);
            Assert.AreEqual(3, third!.Attempts);
            await log.FinishAsync(third, BackgroundJobStatus.Failed, start.AddHours(4), "Erro de rede").ConfigureAwait(false);
            Assert.IsNull(await log.TryBeginAsync("tarefa", company, "2026-09", start.AddDays(2)).ConfigureAwait(false), "Esgotou as tentativas.");

            // Concluída: não se repete. A meio há muito tempo (o processo parou): retoma-se.
            var done = await log.TryBeginAsync("tarefa", company, "2026-11", start).ConfigureAwait(false);
            await log.FinishAsync(done!, BackgroundJobStatus.Succeeded, start, null).ConfigureAwait(false);
            Assert.IsNull(await log.TryBeginAsync("tarefa", company, "2026-11", start.AddDays(1)).ConfigureAwait(false));
            Assert.IsNotNull(await log.TryBeginAsync("tarefa", company, "2026-10", start.AddHours(3)).ConfigureAwait(false), "Ficou a meio há mais de duas horas.");
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task TheMonthlyAiSummaryIsGeneratedOnceForCompaniesThatAskedForIt()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("MonthlyAi"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Tarefas", "gabinete-tarefas"));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Dona", "dona@tarefas.invalid"));

            Guid Company(string name, string taxId, bool automatic, bool withMovement, bool withOwner = true)
            {
                var companyId = Guid.NewGuid();
                context.Add(new Company(companyId, firmId, name, taxId, TaxRegion.Mainland));
                if (withOwner)
                {
                    context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Owner));
                }

                var settings = CompanyAiSettings.Create(companyId);
                settings.Update(enabled: true, autoMonthly: automatic);
                context.Add(settings);
                if (withMovement)
                {
                    var journalId = Guid.NewGuid();
                    context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
                    var sales = Guid.NewGuid();
                    var cash = Guid.NewGuid();
                    context.Add(new LedgerAccount(sales, companyId, "7111", "Vendas"));
                    context.Add(new LedgerAccount(cash, companyId, "111", "Caixa"));
                    var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, "V-1", new DateOnly(2026, 9, 15), "FT 1", "Venda");
                    entry.AddLine(Guid.NewGuid(), cash, "Caixa", 500m, 0m);
                    entry.AddLine(Guid.NewGuid(), sales, "Venda", 0m, 500m);
                    entry.Complete(JournalEntryStatus.Reviewed);
                    context.Add(entry);
                }

                return companyId;
            }

            var active = Company("Empresa Automática, Lda.", "TA-PT-0001", automatic: true, withMovement: true);
            var manual = Company("Empresa Manual, Lda.", "TA-PT-0002", automatic: false, withMovement: true);
            var empty = Company("Empresa Parada, Lda.", "TA-PT-0003", automatic: true, withMovement: false);
            var orphan = Company("Empresa Sem Dono, Lda.", "TA-PT-0004", automatic: true, withMovement: true, withOwner: false);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var narrator = new CountingNarrator();
            var reports = new ManagementReportService(
                context, new ManagementReportDataService(context, new AccountLedgerService(context), new CrossCheckService(context)), narrator);
            var log = new BackgroundJobLog(context);
            var job = new MonthlyAiReportJob(context, reports, narrator, log);

            // Antes do dia 10 não faz nada: o mês anterior pode ainda não estar lançado.
            await job.RunAsync(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, narrator.Calls);

            var onTheTenth = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            await job.RunAsync(onTheTenth, CancellationToken.None).ConfigureAwait(false);
            await job.RunAsync(onTheTenth.AddHours(1), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(1, narrator.Calls, "Uma vez, e só na empresa com a geração automática ligada e com movimentos.");
            Assert.AreEqual((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), narrator.LastPeriod);

            context.ChangeTracker.Clear();
            var runs = await context.BackgroundJobRuns.AsNoTracking().ToDictionaryAsync(run => run.CompanyId).ConfigureAwait(false);
            Assert.AreEqual(BackgroundJobStatus.Succeeded, runs[active].Status);
            Assert.AreEqual("2026-09", runs[active].PeriodKey);
            Assert.IsFalse(runs.ContainsKey(manual), "Sem a opção ligada, a empresa nem entra.");
            Assert.AreEqual(BackgroundJobStatus.Skipped, runs[empty].Status);
            Assert.AreEqual(BackgroundJobStatus.Failed, runs[orphan].Status);
            StringAssert.Contains(runs[orphan].Message, "dono");
            Assert.AreEqual(1, await context.ManagementReports.CountAsync(report => report.CompanyId == active).ConfigureAwait(false));

            // O utilizador vê as execuções das suas empresas (a sem dono não é dele).
            var visible = await log.RecentAsync(userId, 50).ConfigureAwait(false);
            CollectionAssert.AreEquivalent(
                new[] { "Empresa Automática, Lda.", "Empresa Parada, Lda." },
                visible.Select(run => run.CompanyName).ToArray());
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class CountingNarrator : IManagementReportNarrator
    {
        public int Calls { get; private set; }

        public (DateOnly From, DateOnly To) LastPeriod { get; private set; }

        public bool IsConfigured => true;

        public string Model => "modelo-de-teste";

        public Task<ManagementReportNarration> GenerateAsync(
            ManagementReportRequest request, ManagementReportDossier dossier, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPeriod = (request.From, request.To);
            return Task.FromResult(new ManagementReportNarration(
                new ManagementReportResult("Resumo.", [], [], [], ManagementReportResult.Notice), "modelo-de-teste", 10, 5));
        }
    }
}
