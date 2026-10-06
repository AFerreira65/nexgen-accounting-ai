using System.Text.Json;
using NexGenAccountingAI.Application.Chat;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record ChatMessageView(Guid Id, string Role, string Content, IReadOnlyList<ChatToolUsed> Tools, DateTimeOffset CreatedAtUtc);

public sealed record ChatConversationView(Guid Id, string Title, DateTimeOffset UpdatedAtUtc, int Messages);

/// <param name="Refusal">Porque não se respondeu (IA desligada, sem chave, limite, pergunta inválida ou erro do serviço).</param>
public sealed record ChatAskOutcome(string? Refusal, Guid? ConversationId, ChatMessageView? Answer, bool HitLimit);

/// <summary>
/// A conversa com a IA sobre os dados de uma empresa (F4F-04), com memória. Exige acesso à empresa, o interruptor
/// da IA ligado nessa empresa e uma chave configurada. A IA responde com ferramentas só de leitura
/// (<see cref="CompanyChatToolHost"/>); cada utilizador vê só as suas conversas. As perguntas e as respostas
/// guardam-se como texto, com o que a IA consultou, para a conversa continuar e para se poder conferir.
/// </summary>
public sealed class DataChatService(
    AccountingDbContext context,
    IChatModelClient model,
    DataChatAgent agent,
    ListingService listings,
    VarianceService variance,
    AnomalyService anomalies,
    ClosingChecklistService closing,
    TimeProvider time)
{
    public const int MaxQuestionLength = 2000;

    /// <summary>O máximo de mensagens por conversa, de perguntas por dia e por utilizador e empresa, e quantas voltam ao modelo.</summary>
    public const int MaxMessagesPerConversation = 100, MaxQuestionsPerDay = 100, HistoryMessages = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ChatConversationView>?> ListAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (await CompanyNameAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return await context.AiConversations
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(100)
            .Select(item => new ChatConversationView(item.Id, item.Title, item.UpdatedAtUtc, item.Messages.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatMessageView>?> MessagesAsync(Guid userId, Guid companyId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var owned = await context.AiConversations.AsNoTracking()
            .AnyAsync(item => item.Id == conversationId && item.CompanyId == companyId && item.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (!owned || await CompanyNameAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var rows = await context.AiMessages.AsNoTracking()
            .Where(item => item.ConversationId == conversationId)
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(View).ToList();
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid companyId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await context.AiConversations
            .FirstOrDefaultAsync(item => item.Id == conversationId && item.CompanyId == companyId && item.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return false;
        }

        context.AiConversations.Remove(conversation);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<ChatAskOutcome?> AskAsync(
        Guid userId, Guid companyId, Guid? conversationId, string question, CancellationToken cancellationToken = default)
    {
        var companyName = await CompanyNameAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (companyName is null)
        {
            return null;
        }

        static ChatAskOutcome Refuse(string text) => new(text, null, null, false);
        if (string.IsNullOrWhiteSpace(question))
        {
            return Refuse("Escreva a pergunta.");
        }

        if (question.Length > MaxQuestionLength)
        {
            return Refuse($"A pergunta tem mais de {MaxQuestionLength} caracteres: resuma-a.");
        }

        if (!await context.CompanyAiSettings.AsNoTracking().AnyAsync(item => item.CompanyId == companyId && item.Enabled, cancellationToken).ConfigureAwait(false))
        {
            return Refuse("A IA está desligada para esta empresa. Ligue-a nas definições da empresa para conversar sobre os dados.");
        }

        if (!model.IsConfigured)
        {
            return Refuse("A IA não está configurada neste servidor (falta a chave da Anthropic).");
        }

        var now = time.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var today = await context.AiMessages.AsNoTracking()
            .CountAsync(item => item.Role == AiMessageRole.User && item.CreatedAtUtc >= dayStart &&
                context.AiConversations.Any(conversation => conversation.Id == item.ConversationId && conversation.CompanyId == companyId && conversation.UserId == userId), cancellationToken)
            .ConfigureAwait(false);
        if (today >= MaxQuestionsPerDay)
        {
            return Refuse($"Atingiu o limite de {MaxQuestionsPerDay} perguntas por dia nesta empresa. Volte amanhã.");
        }

        AiConversation? conversation = null;
        var history = new List<ChatTurnMessage>();
        var sequence = 0;
        if (conversationId is { } existing)
        {
            conversation = await context.AiConversations.FirstOrDefaultAsync(
                item => item.Id == existing && item.CompanyId == companyId && item.UserId == userId, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
            {
                return Refuse("A conversa já não existe.");
            }

            var previous = await context.AiMessages.AsNoTracking()
                .Where(item => item.ConversationId == existing)
                .OrderByDescending(item => item.Sequence)
                .Take(MaxMessagesPerConversation + 1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (previous.Count >= MaxMessagesPerConversation)
            {
                return Refuse("A conversa já está muito longa: comece outra.");
            }

            sequence = previous.Count == 0 ? 0 : previous[0].Sequence + 1;
            history = previous.Take(HistoryMessages).Reverse()
                .Select(item => new ChatTurnMessage(item.Role == AiMessageRole.User ? "user" : "assistant", [new ChatBlock.Text(item.Content)]))
                .ToList();
        }

        var host = new CompanyChatToolHost(userId, companyId, DateOnly.FromDateTime(time.GetLocalNow().DateTime), listings, variance, anomalies, closing);
        ChatAnswer answer;
        try
        {
            answer = await agent.AnswerAsync(
                DataChatPrompts.System(companyName, DateOnly.FromDateTime(time.GetLocalNow().DateTime)), history, question.Trim(), host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException || exception.GetType().Namespace?.StartsWith("Anthropic", StringComparison.Ordinal) == true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return Refuse("A IA não respondeu (erro do serviço). Tente de novo dentro de momentos.");
        }

        if (conversation is null)
        {
            conversation = AiConversation.Start(companyId, userId, question, now);
            context.AiConversations.Add(conversation);
        }

        var toolsJson = answer.ToolsUsed.Count == 0 ? null : JsonSerializer.Serialize(answer.ToolsUsed, Json);
        context.AiMessages.Add(new AiMessage(conversation.Id, sequence, AiMessageRole.User, question.Trim(), null, null, 0, 0, now));
        var reply = new AiMessage(conversation.Id, sequence + 1, AiMessageRole.Assistant, answer.Text, toolsJson, answer.Model, answer.PromptTokens, answer.OutputTokens, now);
        context.AiMessages.Add(reply);
        conversation.Touch(now);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ChatAskOutcome(null, conversation.Id, View(reply), answer.HitLimit);
    }

    private static ChatMessageView View(AiMessage message) => new(
        message.Id, message.Role == AiMessageRole.User ? "user" : "assistant", message.Content,
        string.IsNullOrEmpty(message.ToolsJson) ? [] : JsonSerializer.Deserialize<List<ChatToolUsed>>(message.ToolsJson, Json) ?? [],
        message.CreatedAtUtc);

    private async Task<string?> CompanyNameAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId && company.IsActive && company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .Select(company => company.LegalName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
