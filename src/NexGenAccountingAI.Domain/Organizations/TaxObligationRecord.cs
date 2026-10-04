namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>O estado de uma obrigação num período.</summary>
public enum TaxObligationState
{
    Pendente = 0,
    Entregue = 1,

    /// <summary>Não se aplica neste período (sem operações, sem imposto a pagar), indicado pelo utilizador.</summary>
    Dispensada = 2,
}

/// <summary>
/// O que o gabinete registou sobre uma obrigação de uma empresa num período: o
/// estado, a data de entrega, uma nota (o comprovativo, a referência de
/// pagamento) e, havendo prorrogação, o prazo alterado à mão.
/// </summary>
public sealed class TaxObligationRecord
{
    public const int CodeMaxLength = 20;

    public const int PeriodKeyMaxLength = 10;

    public const int NoteMaxLength = 500;

    private TaxObligationRecord()
    {
    }

    private TaxObligationRecord(Guid id, Guid companyId, string code, string periodKey)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        PeriodKey = periodKey;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    /// <summary><c>2026-03</c>, <c>2026-T1</c> ou <c>2026</c>.</summary>
    public string PeriodKey { get; private set; } = string.Empty;

    public TaxObligationState State { get; private set; }

    public DateOnly? DeliveredOn { get; private set; }

    public string? Note { get; private set; }

    /// <summary>O prazo alterado à mão (prorrogação, dia não útil); `null` usa o legal.</summary>
    public DateOnly? DueDateOverride { get; private set; }

    public uint RowVersion { get; private set; }

    public static TaxObligationRecord Create(Guid companyId, string code, string periodKey)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedPeriod = (periodKey ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCode.Length is 0 or > CodeMaxLength || normalizedPeriod.Length is 0 or > PeriodKeyMaxLength)
        {
            throw new ArgumentException("A obrigação e o período são obrigatórios.");
        }

        return new TaxObligationRecord(Guid.NewGuid(), companyId, normalizedCode, normalizedPeriod);
    }

    public void Update(TaxObligationState state, DateOnly? deliveredOn, string? note, DateOnly? dueDateOverride)
    {
        if (state == TaxObligationState.Entregue && deliveredOn is null)
        {
            throw new ArgumentException("Indique a data de entrega.", nameof(deliveredOn));
        }

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote is { Length: > NoteMaxLength })
        {
            throw new ArgumentException($"A nota não pode ter mais de {NoteMaxLength} caracteres.", nameof(note));
        }

        State = state;
        DeliveredOn = state == TaxObligationState.Entregue ? deliveredOn : null;
        Note = normalizedNote;
        DueDateOverride = dueDateOverride;
    }
}
