namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>O estado de uma tarefa do gabinete.</summary>
public enum FirmTaskStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3,
}

public enum FirmTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// Uma tarefa de uma empresa da carteira do gabinete (F4E-01): um título, um prazo, uma prioridade e
/// quem a trata. Pode nascer à mão ou de uma obrigação por entregar; neste caso guarda a origem
/// (<c>obrigacao:IVA:2026-09</c>) para não se criar duas vezes a mesma tarefa.
/// </summary>
public sealed class FirmTask
{
    public const int TitleMaxLength = 200;

    public const int DescriptionMaxLength = 2000;

    public const int OriginMaxLength = 60;

    private FirmTask()
    {
    }

    private FirmTask(Guid id, Guid companyId, Guid createdByUserId)
    {
        Id = id;
        CompanyId = companyId;
        CreatedByUserId = createdByUserId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public FirmTaskPriority Priority { get; private set; } = FirmTaskPriority.Normal;

    public FirmTaskStatus Status { get; private set; }

    public Guid? AssigneeUserId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    /// <summary>De onde nasceu, quando não foi escrita à mão (por exemplo <c>obrigacao:IVA:2026-09</c>); único por empresa.</summary>
    public string? Origin { get; private set; }

    public uint RowVersion { get; private set; }

    public static FirmTask Create(
        Guid companyId, Guid createdByUserId, string title, string? description, DateOnly? dueDate, FirmTaskPriority priority, Guid? assigneeUserId, string? origin = null)
    {
        if (companyId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("A empresa e o autor são obrigatórios.");
        }

        var task = new FirmTask(Guid.NewGuid(), companyId, createdByUserId);
        task.Edit(title, description, dueDate, priority, assigneeUserId);
        task.Origin = string.IsNullOrWhiteSpace(origin) ? null : Trim(origin, OriginMaxLength);
        return task;
    }

    public void Edit(string title, string? description, DateOnly? dueDate, FirmTaskPriority priority, Guid? assigneeUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        Title = Trim(title, TitleMaxLength);
        Description = string.IsNullOrWhiteSpace(description) ? null : Trim(description, DescriptionMaxLength);
        DueDate = dueDate;
        Priority = priority;
        AssigneeUserId = assigneeUserId == Guid.Empty ? null : assigneeUserId;
    }

    /// <summary>Muda o estado; concluir regista quem e quando, e reabrir apaga o registo.</summary>
    public void SetStatus(FirmTaskStatus status, Guid userId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        if (status is FirmTaskStatus.Done or FirmTaskStatus.Cancelled)
        {
            CompletedAtUtc = now;
            CompletedByUserId = userId;
        }
        else
        {
            CompletedAtUtc = null;
            CompletedByUserId = null;
        }
    }

    public bool IsClosed => Status is FirmTaskStatus.Done or FirmTaskStatus.Cancelled;

    private static string Trim(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
