namespace AccountingAI.Application.Auditing;

/// <summary>
/// Os carimbos de auditoria de um registo — quem o criou e quem o alterou pela
/// última vez, e quando. Qualquer campo pode ser nulo (registo histórico, sem
/// alteração, ou entidade que só guarda a data de criação).
/// </summary>
public sealed record RecordAuditInfo(
    DateTimeOffset? CreatedAtUtc,
    Guid? CreatedBy,
    DateTimeOffset? ModifiedAtUtc,
    Guid? ModifiedBy);
