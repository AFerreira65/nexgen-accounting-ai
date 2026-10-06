using System.Text.Json;
using NexGenAccountingAI.Application.Chat;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A conversa com a IA sobre os dados (F4F-04): ferramentas só de leitura, guardas, memória e isolamento.</summary>
[TestClass]
public sealed class DataChatServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>Um modelo que pede uma ferramenta na primeira chamada e responde na segunda, com o resultado à vista.</summary>
    private sealed class ToolThenAnswerModel : IChatModelClient
    {
        private int calls;

        public bool Configured { get; set; } = true;

        public string? LastToolResult { get; private set; }

        public List<string> HistoryTexts { get; } = [];

        public string NextToolInput { get; set; } = "{}";

        public string NextToolName { get; set; } = "run_listing";

        public bool IsConfigured => Configured;

        public string Model => "modelo-de-teste";

        public Task<ChatCompletion> CompleteAsync(
            string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default)
        {
            calls++;
            HistoryTexts.Clear();
            HistoryTexts.AddRange(messages.SelectMany(message => message.Blocks).OfType<ChatBlock.Text>().Select(block => block.Value));
            if (messages[^1].Blocks.OfType<ChatBlock.ToolResult>().FirstOrDefault() is { } result)
            {
                LastToolResult = result.Content;
                return Task.FromResult(new ChatCompletion([new ChatBlock.Text($"Resposta com base em: {(result.IsError ? "erro" : "dados")}.")], "end_turn", 50, 10));
            }

            return Task.FromResult(new ChatCompletion([new ChatBlock.ToolUse($"t{calls}", NextToolName, NextToolInput)], "tool_use", 40, 5));
        }
    }

    [TestMethod]
    public async Task AnswersFromReadOnlyToolsWithMemoryGuardsAndIsolation()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("DataChat"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Chat", "gabinete-chat"));
            var companyId = Guid.NewGuid();
            var otherCompanyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Chat, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(otherCompanyId, firmId, "Outra Empresa, Lda.", "501964843", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            var colleagueId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista Chat", "chat@test.invalid"));
            context.Add(new UserAccount(colleagueId, "Colega Chat", "colega-chat@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(companyId, colleagueId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            var otherJournalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            context.Add(new AccountingJournal(otherJournalId, otherCompanyId, "GERAL", "Diário Geral"));
            var bank = Guid.NewGuid();
            var sales = Guid.NewGuid();
            var otherBank = Guid.NewGuid();
            var otherSales = Guid.NewGuid();
            context.Add(new LedgerAccount(bank, companyId, "1201", "Banco", null, true, null, "S", 1));
            context.Add(new LedgerAccount(sales, companyId, "7211", "Serviços", null, true, null, "S", 1));
            context.Add(new LedgerAccount(otherBank, otherCompanyId, "1201", "Banco", null, true, null, "S", 1));
            context.Add(new LedgerAccount(otherSales, otherCompanyId, "7211", "Serviços", null, true, null, "S", 1));

            void Entry(Guid company, Guid journal, Guid debit, Guid credit, string reference, decimal amount, string description)
            {
                var entry = new JournalEntry(Guid.NewGuid(), company, journal, reference, new DateOnly(2026, 9, 10), reference, description);
                entry.AddLine(Guid.NewGuid(), debit, description, amount, 0m);
                entry.AddLine(Guid.NewGuid(), credit, description, 0m, amount);
                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry(companyId, journalId, bank, sales, "L-1", 1000m, "Venda A");
            Entry(otherCompanyId, otherJournalId, otherBank, otherSales, "L-1", 77777m, "Venda de outra empresa");
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var clock = new FixedClock(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
            var engine = new ListingEngine(context);
            var listings = new ListingService(context, engine, clock);
            var calendar = new ObligationCalendarService(context);
            var anomalies = new AnomalyService(context);
            var closing = new ClosingChecklistService(context, anomalies, calendar, new FinancialStatementsService(context), clock);
            var variance = new VarianceService(context);
            var none = CancellationToken.None;

            // As ferramentas: só leem, só da empresa da sessão, e um pedido mal feito volta como erro.
            var host = new CompanyChatToolHost(userId, companyId, new DateOnly(2026, 10, 7), listings, variance, anomalies, closing);
            CollectionAssert.AreEquivalent(new[] { "run_listing", "pivot", "variance", "anomalies", "closing_status" }, host.Tools.Select(tool => tool.Name).ToArray());
            Assert.IsTrue(host.Tools.All(tool => JsonDocument.Parse(tool.PropertiesJson).RootElement.ValueKind == JsonValueKind.Object), "Os esquemas são JSON.");
            var ran = await host.InvokeAsync("run_listing", """{"definition": {"source": "saldos", "columns": ["conta", "debito", "credito"]}}""", none).ConfigureAwait(false);
            Assert.IsFalse(ran.IsError);
            StringAssert.Contains(ran.Content, "\"totalDeLinhas\":2");
            StringAssert.Contains(ran.Content, "1000");
            Assert.IsFalse(ran.Content.Contains("77777", StringComparison.Ordinal), "A empresa vem da sessão: os dados da outra nunca aparecem.");
            var injected = await host.InvokeAsync("run_listing", """{"companyId": "00000000-0000-0000-0000-000000000000", "definition": {"source": "saldos", "columns": ["conta", "debito"]}}""", none).ConfigureAwait(false);
            Assert.IsFalse(injected.Content.Contains("77777", StringComparison.Ordinal), "Um companyId inventado nos argumentos é ignorado.");
            Assert.IsTrue((await host.InvokeAsync("run_listing", """{"definition": {"source": "saldos", "columns": ["campoInventado"]}}""", none).ConfigureAwait(false)).IsError);
            Assert.IsTrue((await host.InvokeAsync("run_listing", "não é json", none).ConfigureAwait(false)).IsError);
            Assert.IsTrue((await host.InvokeAsync("apagar_tudo", "{}", none).ConfigureAwait(false)).IsError, "Uma ferramenta que não existe é recusada.");
            Assert.IsTrue((await host.InvokeAsync("variance", """{"from": "ontem"}""", none).ConfigureAwait(false)).IsError);
            var varied = await host.InvokeAsync("variance", """{"from": "2026-09-01", "to": "2026-09-30", "compareFrom": "2026-08-01", "compareTo": "2026-08-31"}""", none).ConfigureAwait(false);
            Assert.IsFalse(varied.IsError);
            var closed = await host.InvokeAsync("closing_status", """{"year": 2026, "month": 9}""", none).ConfigureAwait(false);
            StringAssert.Contains(closed.Content, "2026-09");
            var pivoted = await host.InvokeAsync("pivot", """{"definition": {"source": "saldos", "columns": ["conta"]}, "rowField": "conta", "columnField": "mes", "measure": "debito"}""", none).ConfigureAwait(false);
            StringAssert.Contains(pivoted.Content, "totalGeral");
            var strange = await host.InvokeAsync("anomalies", """{"from": "2026-01-01", "to": "2026-12-31"}""", none).ConfigureAwait(false);
            Assert.IsFalse(strange.IsError);
            Assert.IsTrue(strange.Content.Length <= CompanyChatToolHost.MaxResultLength + 200);

            // A conversa: recusas por ordem, depois a resposta, a memória e o isolamento por utilizador.
            var model = new ToolThenAnswerModel();
            var service = new DataChatService(context, model, new DataChatAgent(model), listings, variance, anomalies, closing, clock);
            Assert.IsNull(await service.AskAsync(Guid.NewGuid(), companyId, null, "Olá", none).ConfigureAwait(false), "Sem acesso à empresa.");
            StringAssert.Contains((await service.AskAsync(userId, companyId, null, "  ", none).ConfigureAwait(false))!.Refusal, "Escreva");
            StringAssert.Contains((await service.AskAsync(userId, companyId, null, new string('x', 2001), none).ConfigureAwait(false))!.Refusal, "2000");
            StringAssert.Contains((await service.AskAsync(userId, companyId, null, "Quanto vendi?", none).ConfigureAwait(false))!.Refusal, "desligada");
            var settings = CompanyAiSettings.Create(companyId);
            settings.Update(true, false);
            context.Add(settings);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            model.Configured = false;
            StringAssert.Contains((await service.AskAsync(userId, companyId, null, "Quanto vendi?", none).ConfigureAwait(false))!.Refusal, "chave");
            model.Configured = true;

            model.NextToolInput = """{"definition": {"source": "saldos", "columns": ["conta", "credito"]}}""";
            var first = await service.AskAsync(userId, companyId, null, "Quanto vendi em setembro?", none).ConfigureAwait(false);
            Assert.IsNull(first!.Refusal);
            Assert.IsNotNull(first.ConversationId);
            StringAssert.Contains(first.Answer!.Content, "dados");
            Assert.AreEqual("assistant", first.Answer.Role);
            Assert.AreEqual("run_listing", first.Answer.Tools.Single().Name);
            StringAssert.Contains(model.LastToolResult!, "1000");

            // Segunda pergunta na mesma conversa: o modelo vê a anterior (texto).
            var second = await service.AskAsync(userId, companyId, first.ConversationId, "E em agosto?", none).ConfigureAwait(false);
            Assert.AreEqual(first.ConversationId, second!.ConversationId);
            CollectionAssert.IsSubsetOf(new[] { "Quanto vendi em setembro?", "E em agosto?" }, model.HistoryTexts);
            var conversation = (await service.MessagesAsync(userId, companyId, first.ConversationId!.Value, none).ConfigureAwait(false))!;
            CollectionAssert.AreEqual(new[] { "user", "assistant", "user", "assistant" }, conversation.Select(message => message.Role).ToArray());
            var list = await service.ListAsync(userId, companyId, none).ConfigureAwait(false);
            Assert.AreEqual(1, list!.Count);
            Assert.AreEqual("Quanto vendi em setembro?", list[0].Title);
            Assert.AreEqual(4, list[0].Messages);

            // O colega não vê nem continua nem apaga a conversa do outro.
            Assert.AreEqual(0, (await service.ListAsync(colleagueId, companyId, none).ConfigureAwait(false))!.Count);
            Assert.IsNull(await service.MessagesAsync(colleagueId, companyId, first.ConversationId.Value, none).ConfigureAwait(false));
            StringAssert.Contains((await service.AskAsync(colleagueId, companyId, first.ConversationId, "Posso?", none).ConfigureAwait(false))!.Refusal, "já não existe");
            Assert.IsFalse(await service.DeleteAsync(colleagueId, companyId, first.ConversationId.Value, none).ConfigureAwait(false));

            // O limite diário de perguntas.
            var now = clock.GetUtcNow();
            var many = AiConversation.Start(companyId, userId, "Muitas", now);
            context.AiConversations.Add(many);
            for (var index = 0; index < DataChatService.MaxQuestionsPerDay; index++)
            {
                context.AiMessages.Add(new AiMessage(many.Id, index, AiMessageRole.User, "p", null, null, 0, 0, now));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            StringAssert.Contains((await service.AskAsync(userId, companyId, null, "Mais uma", none).ConfigureAwait(false))!.Refusal, "limite");

            Assert.IsTrue(await service.DeleteAsync(userId, companyId, first.ConversationId.Value, none).ConfigureAwait(false));
            Assert.IsNull(await service.MessagesAsync(userId, companyId, first.ConversationId.Value, none).ConfigureAwait(false));
            Assert.IsFalse(await context.AiMessages.AnyAsync(message => message.ConversationId == first.ConversationId).ConfigureAwait(false), "Apagar a conversa apaga as mensagens.");
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
