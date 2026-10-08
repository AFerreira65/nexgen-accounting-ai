namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>Um comentário numa tarefa do gabinete (F4E-01): quem escreveu, quando e o texto.</summary>
public sealed class FirmTaskComment
{
    public const int TextMaxLength = 2000;

    private FirmTaskComment()
    {
    }

    public FirmTaskComment(Guid id, Guid taskId, Guid companyId, Guid userId, string text)
    {
        if (id == Guid.Empty || taskId == Guid.Empty || companyId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Os identificadores são obrigatórios.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var trimmed = text.Trim();
        if (trimmed.Length > TextMaxLength)
        {
            throw new ArgumentException($"O comentário passa os {TextMaxLength} caracteres.", nameof(text));
        }

        Id = id;
        TaskId = taskId;
        CompanyId = companyId;
        UserId = userId;
        Text = trimmed;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
