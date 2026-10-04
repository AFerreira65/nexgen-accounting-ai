namespace AccountingAI.Domain.Accounting;

/// <summary>O estado de um elemento do dossier fiscal num exercício.</summary>
public enum FiscalDossierStatus
{
    /// <summary>Ainda por preparar.</summary>
    Pending = 0,

    /// <summary>Preparado e arquivado.</summary>
    Ready = 1,

    /// <summary>Não se aplica à empresa neste exercício.</summary>
    NotApplicable = 2,
}

/// <summary>
/// O estado que o utilizador deu a um elemento do dossier fiscal (o processo
/// de documentação fiscal do artigo 130.º do CIRC) num exercício, com uma
/// nota — por exemplo, onde o documento está arquivado.
/// </summary>
public sealed class FiscalDossierItem
{
    public const int NoteMaxLength = 500;

    private FiscalDossierItem()
    {
    }

    private FiscalDossierItem(Guid id, Guid companyId, int year, int element)
    {
        Id = id;
        CompanyId = companyId;
        Year = year;
        Element = element;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int Year { get; private set; }

    /// <summary>O número do elemento na lista da portaria (1 a 12).</summary>
    public int Element { get; private set; }

    public FiscalDossierStatus Status { get; private set; }

    public string? Note { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static FiscalDossierItem Create(Guid companyId, int year, int element, FiscalDossierStatus status, string? note)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        if (element < 1)
        {
            throw new ArgumentException("O elemento do dossier não existe.", nameof(element));
        }

        var item = new FiscalDossierItem(Guid.NewGuid(), companyId, year, element);
        item.Set(status, note);
        return item;
    }

    public void Set(FiscalDossierStatus status, string? note)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException("O estado não existe.", nameof(status));
        }

        var normalized = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalized is { Length: > NoteMaxLength })
        {
            throw new ArgumentException($"A nota não pode ter mais de {NoteMaxLength} caracteres.", nameof(note));
        }

        Status = status;
        Note = normalized;
    }
}
