using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>O relato composto: o relatório para a banca a partir dos números da empresa, e a redação com IA.</summary>
[TestClass]
public sealed class ReportPackServiceTests
{
    [TestMethod]
    public async Task BuildsTheBankReportAndNarratesWithAi()
    {
        var currentUser = new CurrentUserAccessor { UserId = Guid.NewGuid() };
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .AddInterceptors(new AuditStampingInterceptor(currentUser))
            .UseNpgsql(TestDatabase.ConnectionString("ReportPack"), TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete do Relato", "gabinete-relato"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa do Relato, Lda.", "RELATO-PT-0001", TaxRegion.Mainland));
            var userId = currentUser.UserId!.Value;
            context.Add(new UserAccount(userId, "Utilizador do Relato", "relato@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("111", "Caixa"), ("2111", "Clientes"), ("511", "Capital"), ("6221", "Trabalhos especializados"), ("7211", "Serviços") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, "R-1", new DateOnly(2026, 2, 5), "D-1", "Lançamento");
            entry.AddLine(Guid.NewGuid(), ids["111"], "Linha", 1000m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["511"], "Linha", 0m, 1000m);
            entry.AddLine(Guid.NewGuid(), ids["2111"], "Linha", 500m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["7211"], "Linha", 0m, 500m);
            entry.AddLine(Guid.NewGuid(), ids["6221"], "Linha", 200m, 0m);
            entry.AddLine(Guid.NewGuid(), ids["111"], "Linha", 0m, 200m);
            entry.Complete(JournalEntryStatus.Reviewed);
            context.Add(entry);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var narrator = new StubNarrator();
            var data = new MapDataProvider(context);
            var reports = new ManagementReportService(context, new ManagementReportDataService(context, new AccountLedgerService(context), new CrossCheckService(context)), narrator);
            var service = new ReportPackService(
                context, new FinancialMapService(context, data), new TrialBalanceTreeService(context, data),
                new FinancialStatementsService(context), reports, TimeProvider.System);

            var request = ReportPackPresets.Build(ReportPackPresets.Bank, 2026, 1, 12)! with { Preparer = "Gabinete do Relato" };
            var pack = await service.BuildAsync(userId, companyId, request).ConfigureAwait(false);

            Assert.IsNotNull(pack);
            Assert.AreEqual("Empresa do Relato, Lda.", pack.CompanyName);
            Assert.AreEqual("Exercício de 2026", pack.Period);
            CollectionAssert.AreEqual(
                new[] { "Apresentação", "Balanço", "Demonstração de resultados", "Exploração mensal", "Rácios económico-financeiros", "Balancete sintético" },
                pack.Sections.Select(section => section.Title).ToArray());
            var balance = pack.Sections[1].Table!;
            CollectionAssert.AreEqual(new[] { "Rubrica", "Exercício", "Exercício anterior" }, balance.Header.ToArray());
            var totalAssets = balance.Rows.Single(row => row.Cells[0] == "Total do ativo");
            Assert.AreEqual("1 300,00", totalAssets.Cells[1].Replace(' ', ' '), "Caixa 800 + clientes 500.");
            Assert.IsTrue(totalAssets.Bold);
            Assert.AreEqual(totalAssets.Cells[1], balance.Rows.Single(row => row.Cells[0] == "Total do capital próprio e do passivo").Cells[1], "O balanço fecha.");
            Assert.AreEqual("–", balance.Rows.Single(row => row.Cells[0] == "Inventários").Cells[1], "O zero é um traço.");
            var income = pack.Sections[2].Table!;
            Assert.AreEqual("300,00", income.Rows.Single(row => row.Cells[0] == "Resultado líquido do período").Cells[1].Replace(' ', ' '));
            Assert.AreEqual(14, pack.Sections[3].Table!.Header.Count, "Rubrica, doze meses e o total.");
            Assert.IsTrue(pack.Sections[4].Table!.Rows.Any(row => row.IsTitle && row.Cells[0] == "Liquidez"));
            var balancete = pack.Sections[5].Table!;
            Assert.IsTrue(balancete.Rows.Any(row => row.Cells[0] == "1" && row.Bold));
            Assert.IsTrue(balancete.Rows.Any(row => row.Cells[0] == "111" && row.Level == 1));
            Assert.AreEqual("Totais", balancete.Rows[^1].Cells[1]);
            Assert.IsNull(pack.Notice, "Sem texto de IA não há aviso.");

            // Secções desligadas ficam de fora; sem nenhuma, recusa.
            var only = await service.BuildAsync(userId, companyId, request with { Sections = request.Sections.Select((section, index) => section with { Enabled = index == 1 }).ToList() }).ConfigureAwait(false);
            Assert.AreEqual("Balanço", only!.Sections.Single().Title);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, request with { Sections = [] })).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, request with { Sections = [new("desconhecido")] })).ConfigureAwait(false);
            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, request).ConfigureAwait(false), "Sem acesso não há relato.");

            // A redação com IA: desligada para a empresa até se ligar; ligada, devolve parágrafos e o modelo.
            var off = await service.NarrateAsync(userId, companyId, "Apresentação", null, 2026, 1, 12).ConfigureAwait(false);
            Assert.IsNotNull(off);
            Assert.AreEqual(0, off.Paragraphs.Count);
            Assert.IsTrue(off.Unavailable!.Contains("desligados", StringComparison.Ordinal));
            await new CompanyAiSettingsService(context, narrator).SaveAsync(userId, companyId, enabled: true, autoMonthly: false).ConfigureAwait(false);
            var on = await service.NarrateAsync(userId, companyId, "Apresentação", "Realçar a liquidez", 2026, 1, 12).ConfigureAwait(false);
            Assert.IsNull(on!.Unavailable);
            Assert.AreEqual("modelo-de-teste", on.Model);
            CollectionAssert.AreEqual(new[] { "Resumo de Empresa do Relato, Lda..", "A atividade cresceu." }, on.Paragraphs.ToArray());
            Assert.IsTrue(narrator.LastQuestion!.Contains("Realçar a liquidez", StringComparison.Ordinal));
            Assert.IsTrue(narrator.LastQuestion.Contains("«Apresentação»", StringComparison.Ordinal));

            // Com texto marcado como da IA, o relato leva o aviso.
            var withAi = await service.BuildAsync(userId, companyId, request with { Sections = [new("texto", "Apresentação", "Texto da IA.\n\n[IA] Texto redigido por modelo-de-teste.")] }).ConfigureAwait(false);
            Assert.IsNotNull(withAi!.Notice);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class StubNarrator : IManagementReportNarrator
    {
        public string? LastQuestion { get; private set; }

        public bool IsConfigured => true;

        public string Model => "modelo-de-teste";

        public Task<ManagementReportNarration> GenerateAsync(ManagementReportRequest request, ManagementReportDossier dossier, CancellationToken cancellationToken = default)
        {
            LastQuestion = request.Question;
            return Task.FromResult(new ManagementReportNarration(
                new ManagementReportResult($"Resumo de {dossier.CompanyName}.", [new ManagementReportSection("Atividade", "A atividade cresceu.", [])], [], [], ManagementReportResult.Notice),
                "modelo-de-teste", 10, 20));
        }
    }
}
