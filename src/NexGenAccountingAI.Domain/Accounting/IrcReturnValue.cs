namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// Um valor indicado pelo utilizador para o Modelo 22 de um exercício: um
/// campo do impresso (por exemplo <c>724</c> ou <c>359</c>) ou um parâmetro do
/// cálculo (por exemplo a taxa da derrama municipal). Os campos calculados não
/// se guardam.
/// </summary>
public sealed class IrcReturnValue
{
    public const int FieldMaxLength = 30;

    public const int NoteMaxLength = 300;

    private IrcReturnValue()
    {
    }

    private IrcReturnValue(Guid id, Guid companyId, int year, string field)
    {
        Id = id;
        CompanyId = companyId;
        Year = year;
        Field = field;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int Year { get; private set; }

    public string Field { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public string? Note { get; private set; }

    public uint RowVersion { get; private set; }

    public static IrcReturnValue Create(Guid companyId, int year, string field, decimal amount, string? note)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        var normalized = (field ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > FieldMaxLength)
        {
            throw new ArgumentException("O campo é obrigatório.", nameof(field));
        }

        var value = new IrcReturnValue(Guid.NewGuid(), companyId, year, normalized);
        value.Set(amount, note);
        return value;
    }

    public void Set(decimal amount, string? note)
    {
        if (amount < 0m)
        {
            throw new ArgumentException($"O valor do campo {Field} não pode ser negativo.", nameof(amount));
        }

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote is { Length: > NoteMaxLength })
        {
            throw new ArgumentException($"A nota não pode ter mais de {NoteMaxLength} caracteres.", nameof(note));
        }

        Amount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        Note = normalizedNote;
    }
}
