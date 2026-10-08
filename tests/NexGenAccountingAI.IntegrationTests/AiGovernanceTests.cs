using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Application.Chat;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>Custos, limites e privacidade da IA: franquia mensal, crédito extra, nomes escondidos e prazo das conversas.</summary>
[TestClass]
public sealed class AiGovernanceTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>Guarda tudo o que lhe chega, e responde repetindo o primeiro marcador de terceiro que viu.</summary>
    private sealed class SpyModel : IChatModelClient
    {
        public List<string> Seen { get; } = [];

        public bool IsConfigured => true;

        public string Model => "claude-haiku-4-5-20251001";

        public Task<ChatCompletion> CompleteAsync(
            string system, IReadOnlyList<ChatTurnMessage> messages, IReadOnlyList<ChatToolSpec> tools, CancellationToken cancellationToken = default)
        {
            Seen.Clear();
            Seen.Add(system);
            Seen.AddRange(messages.SelectMany(message => message.Blocks).OfType<ChatBlock.Text>().Select(block => block.Value));
            var asked = string.Concat(messages[^1].Blocks.OfType<ChatBlock.Text>().Select(block => block.Value));
            var marker = System.Text.RegularExpressions.Regex.Match(asked, @"\[TERCEIRO-\d+\]").Value;
            var nif = System.Text.RegularExpressions.Regex.Match(asked, @"\[NIF-\d+\]").Value;
            return Task.FromResult(new ChatCompletion([new ChatBlock.Text($"Sobre {marker} ({nif}): nada a assinalar na [EMPRESA].")], "end_turn", 1000, 200));
        }
    }

    [TestMethod]
    public void ThePricesFollowTheModelAndTheOverridesAndNeverUndercount()
    {
        Assert.AreEqual(1m, AiPricing.PriceOf(AiModels.Haiku).Input);
        Assert.AreEqual(5m, AiPricing.PriceOf(AiModels.Haiku).Output);
        Assert.IsTrue(AiPricing.PriceOf(AiModels.Opus).Output > AiPricing.PriceOf(AiModels.Sonnet).Output);
        Assert.AreEqual(AiPricing.Fallback, AiPricing.PriceOf("um-modelo-que-nao-conheco"), "Um modelo desconhecido paga-se pelo preço mais alto.");
        Assert.AreEqual(0.0015m, AiPricing.CostUsd(AiModels.Haiku, 500, 200), "500 de entrada e 200 de saída no modelo pequeno.");
        Assert.AreEqual(2m, AiPricing.CostUsd("outro", 1_000_000, 0, "{\"outro\":[2,10]}"), "Os preços do produtor substituem os de origem.");
        Assert.AreEqual(AiPricing.CostUsd("outro", 1000, 1000), AiPricing.CostUsd("outro", 1000, 1000, "isto não é json"), "Preços mal escritos ignoram-se.");
        Assert.AreEqual(AiModels.Haiku, AiModels.Default(AiFeature.Chat));
        Assert.AreEqual(AiModels.Sonnet, AiModels.Default(AiFeature.Report));
    }

    [TestMethod]
    public void TheProtectorHidesNamesAndIdentifiersAndRestoresThemInTheAnswer()
    {
        var protector = new AiTextProtector("Empresa Chat, Lda.", ["Fornecedor Misterioso Lda", "Ana Cliente", "Rui"]);
        var text = "A Empresa Chat, Lda. (NIF 500584095) deve ao Fornecedor Misterioso Lda 1 200,50 €; contacto ana@cliente.pt, IBAN PT50 0002 0123 1234 5678 9015 4. Falou com Rui e a ANA CLIENTE. A fatura 1234567890 não é um NIF.";
        var safe = protector.Protect(text);

        foreach (var secret in new[] { "Empresa Chat", "500584095", "Fornecedor Misterioso", "ana@cliente.pt", "PT50", "ANA CLIENTE" })
        {
            Assert.IsFalse(safe.Contains(secret, StringComparison.OrdinalIgnoreCase), $"«{secret}» não podia sair: {safe}");
        }

        StringAssert.Contains(safe, "[EMPRESA]");
        StringAssert.Contains(safe, "[NIF-1]");
        StringAssert.Contains(safe, "[TERCEIRO-1]");
        StringAssert.Contains(safe, "[EMAIL-1]");
        StringAssert.Contains(safe, "[IBAN-1]");
        StringAssert.Contains(safe, "Rui e a [TERCEIRO-", "Um nome com menos de 4 letras não se troca (apanharia palavras comuns).");
        StringAssert.Contains(safe, "1 200,50 €", "Os valores não se tocam.");
        StringAssert.Contains(safe, "1234567890", "Um número de 10 dígitos não é um NIF.");
        Assert.AreEqual(safe, protector.Protect(text), "O mesmo texto dá os mesmos marcadores.");

        // Na resposta, repõem-se (também sem parênteses, como a IA por vezes escreve).
        var answer = "Sobre [TERCEIRO-1] ([NIF-1]) na [EMPRESA]: ver NIF-1 e terceiro-2.";
        var restored = protector.Restore(answer);
        StringAssert.Contains(restored, "Fornecedor Misterioso Lda");
        StringAssert.Contains(restored, "500584095");
        StringAssert.Contains(restored, "Empresa Chat, Lda.");
        Assert.AreEqual(2, restored.Split("500584095").Length - 1, "O NIF repõe-se onde aparece, com ou sem parênteses.");
        Assert.AreEqual("[TERCEIRO-99]", protector.Restore("[TERCEIRO-99]"), "Um marcador desconhecido fica como está.");

        // Dentro de JSON, o que se repõe vai escapado.
        var quoted = new AiTextProtector("Empresa \"Aspas\" Lda");
        var json = quoted.Restore("{\"texto\":\"[EMPRESA] cresceu\"}", jsonEscaped: true);
        Assert.AreEqual("Empresa \"Aspas\" Lda cresceu", System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("texto").GetString());
    }

    [TestMethod]
    public async Task TheBudgetCountsTheAllowanceThenTheExtraCreditAndRefusesWhenItIsSpent()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("AiBudget"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete IA", "gabinete-ia"));
            var company = Guid.NewGuid();
            var other = Guid.NewGuid();
            context.Add(new Company(company, firmId, "Empresa IA, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(other, firmId, "Outra IA, Lda.", "503504564", TaxRegion.Mainland));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var clock = new Clock(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
            var budget = new AiBudgetService(context, clock);
            var none = CancellationToken.None;

            // Sem franquia: só se regista.
            var open = await budget.StatusAsync(company, none).ConfigureAwait(false);
            Assert.IsTrue(open.Unlimited);
            Assert.IsNull(await budget.RefusalAsync(company, none).ConfigureAwait(false));
            await budget.RecordAsync(company, null, AiFeature.Chat, AiModels.Haiku, 100_000, 10_000, none).ConfigureAwait(false);
            Assert.AreEqual(0.15m, (await budget.StatusAsync(company, none).ConfigureAwait(false)).SpentThisMonthUsd);

            // Franquia por empresa; uma por omissão serve as que não têm.
            await budget.SetAllowanceAsync(company, 0.20m, none).ConfigureAwait(false);
            var status = await budget.StatusAsync(company, none).ConfigureAwait(false);
            Assert.AreEqual(0.20m, status.AllowanceUsd);
            Assert.IsFalse(status.AllowanceIsDefault);
            Assert.AreEqual(0.05m, status.AvailableUsd);
            context.Add(new AiGeneralSettings(Guid.NewGuid()));
            await context.SaveChangesAsync().ConfigureAwait(false);
            (await context.AiGeneralSettings.SingleAsync().ConfigureAwait(false)).ConfigureUsage(1m, null, null, null, null);
            await context.SaveChangesAsync().ConfigureAwait(false);
            var viaDefault = await budget.StatusAsync(other, none).ConfigureAwait(false);
            Assert.AreEqual(1m, viaDefault.AllowanceUsd);
            Assert.IsTrue(viaDefault.AllowanceIsDefault);

            // Gasta o que resta: o pedido que passa o saldo regista-se, e depois a IA recusa.
            await budget.RecordAsync(company, null, AiFeature.Report, AiModels.Haiku, 100_000, 10_000, none).ConfigureAwait(false);
            var spent = await budget.StatusAsync(company, none).ConfigureAwait(false);
            Assert.IsTrue(spent.Exhausted);
            Assert.AreEqual(0m, spent.AvailableUsd);
            Assert.AreEqual(AiBudgetService.ExhaustedMessage, await budget.RefusalAsync(company, none).ConfigureAwait(false));
            Assert.IsNull(await budget.RefusalAsync(other, none).ConfigureAwait(false), "Cada empresa tem o seu saldo.");

            // O crédito extra cobre o que falta e só se gasta depois da franquia do mês.
            var topped = await budget.AddCreditAsync(company, 0.50m, "Carregamento de outubro", Guid.NewGuid(), none).ConfigureAwait(false);
            Assert.AreEqual(0.50m, topped.AvailableUsd);
            Assert.IsNull(await budget.RefusalAsync(company, none).ConfigureAwait(false));
            await budget.RecordAsync(company, null, AiFeature.Chat, AiModels.Haiku, 100_000, 10_000, none).ConfigureAwait(false);
            var afterExtra = await budget.StatusAsync(company, none).ConfigureAwait(false);
            Assert.AreEqual(0.35m, afterExtra.ExtraCreditUsd, "Gastou 0,15 US$, todos do crédito extra (a franquia já tinha acabado).");
            Assert.AreEqual(1, await context.AiCreditTopUps.CountAsync().ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => budget.AddCreditAsync(company, 0m, null, null, none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => budget.AddCreditAsync(Guid.NewGuid(), 1m, null, null, none)).ConfigureAwait(false);

            // No mês seguinte a franquia recomeça; o crédito extra que sobrou mantém-se.
            clock.Now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
            var november = await budget.StatusAsync(company, none).ConfigureAwait(false);
            Assert.AreEqual(0m, november.SpentThisMonthUsd);
            Assert.AreEqual(0.35m, november.ExtraCreditUsd);
            Assert.AreEqual(0.55m, november.AvailableUsd);

            var all = await budget.ListAsync(none).ConfigureAwait(false);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual("Gabinete IA", all.First().FirmName);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task TheAssistantSendsNoNamesRepeatsNoneAndKeepsConversationsOnlyAsLongAsTheCompanyWants()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("AiPrivacy"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Privacidade", "gabinete-privacidade"));
            var company = Guid.NewGuid();
            context.Add(new Company(company, firmId, "Empresa Chat, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista", "contabilista@privacidade.invalid"));
            context.Add(new CompanyUser(company, userId, CompanyUserRole.Accountant));
            context.Add(Party.Create(company, "PT", "503504564", "Fornecedor Misterioso Lda"));
            var settings = CompanyAiSettings.Create(company);
            settings.Update(true, false);
            context.Add(settings);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var clock = new Clock(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
            var anomalies = new AnomalyService(context);
            var calendar = new ObligationCalendarService(context);
            var spy = new SpyModel();
            var governor = new AiGovernor(context, new AiBudgetService(context, clock));
            var chat = new DataChatService(
                context, spy, new DataChatAgent(spy), new ListingService(context, new ListingEngine(context), clock), new VarianceService(context), anomalies,
                new ClosingChecklistService(context, anomalies, calendar, new FinancialStatementsService(context), clock), clock, governor);
            var none = CancellationToken.None;

            // Nomes escondidos por omissão: a IA recebe marcadores, o utilizador lê os nomes verdadeiros, e o consumo fica registado.
            var asked = await chat.AskAsync(userId, company, null, "O que há a assinalar sobre o Fornecedor Misterioso Lda, NIF 503504564, na Empresa Chat?", none).ConfigureAwait(false);
            Assert.IsNull(asked!.Refusal);
            foreach (var secret in new[] { "Misterioso", "503504564", "Empresa Chat" })
            {
                Assert.IsFalse(spy.Seen.Any(text => text.Contains(secret, StringComparison.OrdinalIgnoreCase)), $"«{secret}» chegou à IA.");
            }

            StringAssert.Contains(spy.Seen.First(), "Privacidade", "O enunciado explica os marcadores.");
            StringAssert.Contains(asked.Answer!.Content, "Fornecedor Misterioso Lda");
            StringAssert.Contains(asked.Answer.Content, "503504564");
            StringAssert.Contains(asked.Answer.Content, "Empresa Chat, Lda.");
            var usage = await context.AiUsageRecords.AsNoTracking().SingleAsync().ConfigureAwait(false);
            Assert.AreEqual(AiFeature.Chat, usage.Feature);
            Assert.AreEqual(1000, usage.InputTokens);
            Assert.AreEqual(0.002m, usage.CostUsd, "1 000 de entrada e 200 de saída no modelo pequeno.");

            // Com o escondimento desligado, a IA recebe o texto tal como está.
            var row = await context.CompanyAiSettings.SingleAsync().ConfigureAwait(false);
            row.ConfigurePrivacy(false, null);
            await context.SaveChangesAsync().ConfigureAwait(false);
            await chat.AskAsync(userId, company, null, "Fornecedor Misterioso Lda?", none).ConfigureAwait(false);
            Assert.IsTrue(spy.Seen.Any(text => text.Contains("Fornecedor Misterioso", StringComparison.Ordinal)));

            // Prazo 0: a resposta volta ao ecrã mas nada se guarda; com prazo, a conversa guarda-se e apaga-se quando o passa.
            row.ConfigurePrivacy(true, 0);
            await context.SaveChangesAsync().ConfigureAwait(false);
            var conversationsBefore = await context.AiConversations.CountAsync().ConfigureAwait(false);
            var ephemeral = await chat.AskAsync(userId, company, null, "Pergunta que não se guarda", none).ConfigureAwait(false);
            Assert.IsNotNull(ephemeral!.Answer);
            Assert.IsNull(ephemeral.ConversationId);
            Assert.AreEqual(conversationsBefore, await context.AiConversations.CountAsync().ConfigureAwait(false));
            Assert.IsFalse(await context.AiMessages.AnyAsync(message => message.Content.Contains("não se guarda")).ConfigureAwait(false));
            Assert.AreEqual(3, await context.AiUsageRecords.CountAsync().ConfigureAwait(false), "O consumo regista-se mesmo sem guardar a conversa.");

            row.ConfigurePrivacy(true, 7);
            await context.SaveChangesAsync().ConfigureAwait(false);
            var kept = await chat.AskAsync(userId, company, null, "Pergunta que se guarda", none).ConfigureAwait(false);
            Assert.IsNotNull(kept!.ConversationId);
            var retention = new AiRetentionService(context, clock);
            Assert.AreEqual(0, await retention.PurgeAllAsync(none).ConfigureAwait(false), "Ainda dentro do prazo.");
            clock.Now = clock.Now.AddDays(8);
            Assert.IsTrue(await retention.PurgeAllAsync(none).ConfigureAwait(false) >= 1);
            Assert.IsFalse(await context.AiConversations.AnyAsync(conversation => conversation.Id == kept.ConversationId).ConfigureAwait(false));

            // O prazo 0 aplicado de repente apaga as que existem; sem prazo, nada se apaga.
            clock.Now = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
            row.ConfigurePrivacy(true, null);
            await context.SaveChangesAsync().ConfigureAwait(false);
            await chat.AskAsync(userId, company, null, "Outra que se guarda", none).ConfigureAwait(false);
            Assert.AreEqual(0, await retention.PurgeCompanyAsync(company, null, none).ConfigureAwait(false));
            Assert.IsTrue(await retention.PurgeCompanyAsync(company, 0, none).ConfigureAwait(false) >= 1);
            Assert.AreEqual(0, await context.AiConversations.CountAsync().ConfigureAwait(false));

            // Sem saldo, a IA recusa e não chama o modelo.
            context.Add(new CompanyAiBudget(company));
            await context.SaveChangesAsync().ConfigureAwait(false);
            await new AiBudgetService(context, clock).SetAllowanceAsync(company, 0.001m, none).ConfigureAwait(false);
            var calls = spy.Seen.ToList();
            var refused = await chat.AskAsync(userId, company, null, "Mais uma pergunta", none).ConfigureAwait(false);
            Assert.AreEqual(AiBudgetService.ExhaustedMessage, refused!.Refusal);
            CollectionAssert.AreEqual(calls, spy.Seen, "Não houve chamada ao modelo.");
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
