namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// Uma verificação manual do fecho de um mês (F4F-02): o que o contabilista conferiu à mão (os saldos do
/// banco com os extratos, os acréscimos e diferimentos…), com uma nota, quem e quando. As verificações
/// automáticas calculam-se de cada vez e não se guardam.
/// </summary>
public sealed class ClosingCheck
{
    public const int ItemKeyMaxLength = 40;

    public const int PeriodKeyMaxLength = 7;

    public const int NoteMaxLength = 500;

    private ClosingCheck()
    {
    }

    private ClosingCheck(Guid id, Guid companyId, string periodKey, string itemKey)
    {
        Id = id;
        CompanyId = companyId;
        PeriodKey = periodKey;
        ItemKey = itemKey;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>O mês, <c>2026-09</c>.</summary>
    public string PeriodKey { get; private set; } = string.Empty;

    public string ItemKey { get; private set; } = string.Empty;

    public bool Checked { get; private set; }

    public string? Note { get; private set; }

    public Guid? CheckedBy { get; private set; }

    public DateTimeOffset? CheckedAtUtc { get; private set; }

    public static ClosingCheck Create(Guid companyId, string periodKey, string itemKey)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(periodKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        return new ClosingCheck(Guid.NewGuid(), companyId, periodKey.Trim(), itemKey.Trim());
    }

    /// <summary>Marca ou desmarca; marcar regista quem e quando, desmarcar apaga o registo.</summary>
    public void Set(bool isChecked, string? note, Guid userId, DateTimeOffset now)
    {
        Checked = isChecked;
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Note = text is { Length: > NoteMaxLength } ? text[..NoteMaxLength] : text;
        CheckedBy = isChecked ? userId : null;
        CheckedAtUtc = isChecked ? now : null;
    }
}
