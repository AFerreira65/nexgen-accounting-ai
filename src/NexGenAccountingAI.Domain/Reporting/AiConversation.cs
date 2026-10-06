namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>
/// Uma conversa de um utilizador com a IA sobre os dados de uma empresa (F4F-04). Guarda as perguntas e as
/// respostas (só o texto) para a conversa ter memória; cada utilizador vê só as suas.
/// </summary>
public sealed class AiConversation
{
    public const int TitleMaxLength = 120;

    private AiConversation()
    {
    }

    private AiConversation(Guid id, Guid companyId, Guid userId, string title, DateTimeOffset now)
    {
        Id = id;
        CompanyId = companyId;
        UserId = userId;
        Title = title;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ICollection<AiMessage> Messages { get; } = [];

    public static AiConversation Start(Guid companyId, Guid userId, string firstQuestion, DateTimeOffset now)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("A empresa e o utilizador são obrigatórios.");
        }

        var text = (firstQuestion ?? string.Empty).Trim().ReplaceLineEndings(" ");
        var title = text.Length == 0 ? "Conversa" : text.Length <= TitleMaxLength ? text : text[..(TitleMaxLength - 1)] + "…";
        return new AiConversation(Guid.NewGuid(), companyId, userId, title, now);
    }

    public void Touch(DateTimeOffset now) => UpdatedAtUtc = now;
}

public enum AiMessageRole
{
    User = 0,
    Assistant = 1,
}

/// <summary>Uma mensagem de uma conversa: a pergunta ou a resposta, e, na resposta, o que a IA consultou.</summary>
public sealed class AiMessage
{
    public const int ContentMaxLength = 8000;

    private AiMessage()
    {
    }

    public AiMessage(
        Guid conversationId, int sequence, AiMessageRole role, string content, string? toolsJson, string? model, int promptTokens, int outputTokens, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ConversationId = conversationId;
        Sequence = sequence;
        Role = role;
        Content = content.Length <= ContentMaxLength ? content : content[..ContentMaxLength];
        ToolsJson = toolsJson;
        Model = model;
        PromptTokens = promptTokens;
        OutputTokens = outputTokens;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public int Sequence { get; private set; }

    public AiMessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    /// <summary>O que a IA consultou para responder (nome e resumo de cada ferramenta), em JSON.</summary>
    public string? ToolsJson { get; private set; }

    public string? Model { get; private set; }

    public int PromptTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
