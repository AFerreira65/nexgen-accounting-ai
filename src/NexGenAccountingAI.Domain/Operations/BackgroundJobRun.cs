namespace NexGenAccountingAI.Domain.Operations;

public enum BackgroundJobStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,

    /// <summary>Não havia nada para fazer (por exemplo, um mês sem movimentos).</summary>
    Skipped = 4,
}

/// <summary>
/// Uma unidade de trabalho de uma tarefa automática: a tarefa, a empresa (ou nenhuma,
/// numa tarefa geral) e o período (por exemplo «2026-09»). Há uma só linha por unidade,
/// atualizada a cada tentativa: é ela que impede a mesma tarefa de correr duas vezes
/// para o mesmo período e que guarda o que aconteceu.
/// </summary>
public sealed class BackgroundJobRun
{
    private BackgroundJobRun()
    {
    }

    public BackgroundJobRun(Guid id, string jobKey, Guid companyId, string periodKey, DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The run identifier is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodKey);

        Id = id;
        JobKey = jobKey.Trim();
        CompanyId = companyId;
        PeriodKey = periodKey.Trim();
        Status = BackgroundJobStatus.Running;
        Attempts = 1;
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; private set; }

    public string JobKey { get; private set; } = string.Empty;

    /// <summary><see cref="Guid.Empty"/> numa tarefa que não é de uma empresa.</summary>
    public Guid CompanyId { get; private set; }

    public string PeriodKey { get; private set; } = string.Empty;

    public BackgroundJobStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    public string? Message { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Nova tentativa de uma unidade que falhou ou ficou a meio.</summary>
    public void Restart(DateTimeOffset startedAtUtc)
    {
        Status = BackgroundJobStatus.Running;
        Attempts++;
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = null;
        Message = null;
    }

    public void Finish(BackgroundJobStatus status, DateTimeOffset finishedAtUtc, string? message)
    {
        if (status == BackgroundJobStatus.Running)
        {
            throw new ArgumentException("A run finishes as succeeded, failed or skipped.", nameof(status));
        }

        Status = status;
        FinishedAtUtc = finishedAtUtc;
        var text = message?.Trim();
        Message = string.IsNullOrEmpty(text) ? null : text.Length > 1000 ? text[..1000] : text;
    }
}
