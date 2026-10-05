namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>O que aconteceu a um lançamento que já existia.</summary>
public enum JournalEntryChangeAction
{
    /// <summary>O lançamento foi removido: substituído por uma correção com a mesma referência, ou apagado.</summary>
    Removed = 1,

    /// <summary>Passou a conferido.</summary>
    Reviewed = 2,

    /// <summary>Voltou a «por conferir».</summary>
    Reopened = 3,

    /// <summary>Foi anulado.</summary>
    Voided = 4,

    /// <summary>Outra alteração dos dados do lançamento.</summary>
    Changed = 5,
}

/// <summary>
/// Uma alteração a um lançamento já gravado, com o estado em que ele estava
/// <b>antes</b> (<see cref="BeforeJson"/>: cabeçalho e linhas). Identifica o
/// lançamento pela empresa e pela referência, e não pelo identificador, porque
/// corrigir um lançamento remove-o e recria-o com a mesma referência. Só se
/// acrescenta; nunca se altera nem apaga.
/// </summary>
public sealed class JournalEntryChange
{
    private JournalEntryChange()
    {
    }

    public JournalEntryChange(
        Guid id, Guid companyId, string reference, JournalEntryChangeAction action,
        DateTimeOffset changedAtUtc, Guid? changedBy, string beforeJson)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The change identifier is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeJson);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        Id = id;
        CompanyId = companyId;
        Reference = reference.Trim().ToUpperInvariant();
        Action = action;
        ChangedAtUtc = changedAtUtc;
        ChangedBy = changedBy;
        BeforeJson = beforeJson;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public JournalEntryChangeAction Action { get; private set; }

    public DateTimeOffset ChangedAtUtc { get; private set; }

    /// <summary>O utilizador da sessão; nulo num processo sem utilizador (importação em linha de comandos, tarefa agendada).</summary>
    public Guid? ChangedBy { get; private set; }

    public string BeforeJson { get; private set; } = string.Empty;
}
