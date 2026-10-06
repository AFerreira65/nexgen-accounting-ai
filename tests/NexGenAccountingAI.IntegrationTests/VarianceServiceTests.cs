using NexGenAccountingAI.Application.Analysis;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>As variações entre dois períodos (F4F-03): os números e a explicação com IA.</summary>
[TestClass]
public sealed class VarianceServiceTests
{
    [TestMethod]
    public async Task ComparesPeriodsByAccountAndOnlyAsksTheAiWhenAllowed()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("Variance"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Variações", "gabinete-variacoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Variações, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista Variações", "variacoes@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("1201", "Banco"), ("6221", "Trabalhos especializados"), ("6262", "Comunicação"), ("7211", "Serviços") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", 1));
            }

            var agency = Party.Create(companyId, "PT", "500000001", "Agência X");
            var lawyer = Party.Create(companyId, "PT", "500000002", "Advogado Y");
            context.AddRange(agency, lawyer);

            var number = 0;
            void Entry(DateOnly date, string expenseOrIncome, decimal amount, Guid? party, bool income = false, EntrySource? source = null, bool voided = false)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, reference, $"Lançamento {reference}");
                if (income)
                {
                    entry.AddLine(Guid.NewGuid(), ids["1201"], "D", amount, 0m);
                    entry.AddLine(Guid.NewGuid(), ids[expenseOrIncome], "C", 0m, amount, party);
                }
                else
                {
                    entry.AddLine(Guid.NewGuid(), ids[expenseOrIncome], "D", amount, 0m, party);
                    entry.AddLine(Guid.NewGuid(), ids["1201"], "C", 0m, amount);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                if (source is { } value)
                {
                    entry.SetSource(value);
                }

                if (voided)
                {
                    entry.Void();
                }

                context.Add(entry);
            }

            // Agosto (anterior): serviços 1 000, trabalhos 200 (advogado), comunicação 50.
            Entry(new DateOnly(2026, 8, 10), "7211", 1000m, null, income: true);
            Entry(new DateOnly(2026, 8, 12), "6221", 200m, lawyer.Id);
            Entry(new DateOnly(2026, 8, 15), "6262", 50m, null);
            // Setembro (atual): serviços 1 400, trabalhos 900 (agência 600 + advogado 300), comunicação 50 (igual: não aparece).
            Entry(new DateOnly(2026, 9, 10), "7211", 1400m, null, income: true);
            Entry(new DateOnly(2026, 9, 11), "6221", 600m, agency.Id);
            Entry(new DateOnly(2026, 9, 12), "6221", 300m, lawyer.Id);
            Entry(new DateOnly(2026, 9, 15), "6262", 50m, null);
            // O que não conta: anulado e abertura em setembro.
            Entry(new DateOnly(2026, 9, 20), "6221", 5000m, null, voided: true);
            Entry(new DateOnly(2026, 9, 1), "6221", 7777m, null, source: EntrySource.Opening);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VarianceService(context);
            var none = CancellationToken.None;
            var report = await service.BuildAsync(userId, companyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), none).ConfigureAwait(false);
            Assert.IsNotNull(report);
            Assert.AreEqual(1400m, report!.Totals.Income);
            Assert.AreEqual(1000m, report.Totals.PreviousIncome);
            Assert.AreEqual(950m, report.Totals.Expenses);
            Assert.AreEqual(250m, report.Totals.PreviousExpenses);
            Assert.AreEqual(450m, report.Totals.Result);
            Assert.AreEqual(750m, report.Totals.PreviousResult);

            // Ordenadas pela maior variação; a conta sem variação (6262) não aparece.
            CollectionAssert.AreEqual(new[] { "6221", "7211" }, report.Rows.Select(row => row.AccountCode).ToArray());
            var work = report.Rows[0];
            Assert.AreEqual("expense", work.Kind);
            Assert.AreEqual(900m, work.Current);
            Assert.AreEqual(200m, work.Previous);
            Assert.AreEqual(700m, work.Delta);
            Assert.AreEqual(350.0m, work.Percent);
            Assert.IsFalse(work.Favorable, "Mais gastos não é favorável.");
            Assert.AreEqual("Trabalhos especializados", work.AccountName);
            // Quem mais pesou: a agência (nova, +600) e depois o advogado (+100).
            CollectionAssert.AreEqual(new[] { "Agência X", "Advogado Y" }, work.TopParties.Select(party => party.Name).ToArray());
            Assert.AreEqual(600m, work.TopParties[0].Delta);
            Assert.AreEqual(100m, work.TopParties[1].Delta);
            var income = report.Rows[1];
            Assert.AreEqual("income", income.Kind);
            Assert.AreEqual(400m, income.Delta);
            Assert.AreEqual(40.0m, income.Percent);
            Assert.IsTrue(income.Favorable);

            // Uma conta que só existe no período atual não tem percentagem.
            var onlyNow = await service.BuildAsync(userId, companyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), none).ConfigureAwait(false);
            Assert.IsTrue(onlyNow!.Rows.All(row => row.Percent is null && row.Previous == 0m));

            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), none).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.BuildAsync(userId, companyId, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), none)).ConfigureAwait(false);

            // A explicação com IA: interruptor da empresa, chave, e só os números da comparação seguem.
            var explainer = new FakeExplainer();
            var explanations = new VarianceExplanationService(context, service, explainer);
            Task<VarianceExplanationOutcome?> Ask() => explanations.ExplainAsync(userId, companyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), none);
            StringAssert.Contains((await Ask().ConfigureAwait(false))!.Refusal, "desligada");
            Assert.AreEqual(0, explainer.Calls);
            var settings = CompanyAiSettings.Create(companyId);
            settings.Update(true, false);
            context.Add(settings);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            explainer.Configured = false;
            StringAssert.Contains((await Ask().ConfigureAwait(false))!.Refusal, "chave");
            explainer.Configured = true;
            var outcome = await Ask().ConfigureAwait(false);
            Assert.IsNull(outcome!.Refusal);
            Assert.AreEqual("Os trabalhos especializados subiram.", outcome.Explanation!.Text);
            StringAssert.Contains(explainer.LastJson!, "\"6221\"");
            StringAssert.Contains(explainer.LastJson!, "Agência X");
            Assert.IsFalse(explainer.LastJson!.Contains("L-", StringComparison.Ordinal), "Nenhum lançamento segue para a IA.");
            Assert.IsFalse(explainer.LastJson!.Contains("500000001", StringComparison.Ordinal), "Nem NIF.");
            Assert.IsNull(await explanations.ExplainAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), none).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class FakeExplainer : IVarianceExplainer
    {
        public bool Configured { get; set; } = true;

        public int Calls { get; private set; }

        public string? LastJson { get; private set; }

        public bool IsConfigured => Configured;

        public string Model => "modelo-de-teste";

        public Task<VarianceExplanation> ExplainAsync(string reportJson, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastJson = reportJson;
            return Task.FromResult(new VarianceExplanation("Os trabalhos especializados subiram.", Model, 100, 20));
        }
    }
}
