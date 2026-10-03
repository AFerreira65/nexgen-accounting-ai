namespace AccountingAI.Domain.Accounting;

public sealed class JournalEntry
{
    private JournalEntry()
    {
    }

    public JournalEntry(
        Guid id,
        Guid companyId,
        Guid journalId,
        string reference,
        DateOnly entryDate,
        string documentReference,
        string description,
        Guid? documentTypeId = null,
        string? notes = null,
        DateOnly? documentDate = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The journal entry identifier is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        if (journalId == Guid.Empty)
        {
            throw new ArgumentException("The journal identifier is required.", nameof(journalId));
        }

        if (documentTypeId == Guid.Empty)
        {
            throw new ArgumentException("The document type identifier cannot be empty.", nameof(documentTypeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Id = id;
        CompanyId = companyId;
        JournalId = journalId;
        DocumentTypeId = documentTypeId;
        Reference = reference.Trim().ToUpperInvariant();
        EntryDate = entryDate;
        DocumentReference = documentReference.Trim();
        DocumentDate = documentDate;
        Description = description.Trim();
        Notes = NormalizeNotes(notes);
        Status = JournalEntryStatus.PendingReview;
        Source = EntrySource.Manual;
    }

    /// <summary>Máximo de caracteres das observações internas do lançamento.</summary>
    public const int NotesMaxLength = 2000;

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid JournalId { get; private set; }

    public Guid? DocumentTypeId { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public DateOnly EntryDate { get; private set; }

    public string DocumentReference { get; private set; } = string.Empty;

    /// <summary>A data do documento de origem, quando difere da data do lançamento; `null` se não foi indicada.</summary>
    public DateOnly? DocumentDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    /// <summary>Observações internas livres; `null` quando não há nota.</summary>
    public string? Notes { get; private set; }

    public JournalEntryStatus Status { get; private set; }

    /// <summary>De onde veio o lançamento; por omissão <see cref="EntrySource.Manual"/>.</summary>
    public EntrySource Source { get; private set; }

    public decimal TotalAmount { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public AccountingJournal Journal { get; private set; } = null!;

    public AccountingDocumentType? DocumentType { get; private set; }

    public ICollection<JournalEntryLine> Lines { get; } = [];

    public void AddLine(
        Guid lineId,
        Guid accountId,
        string description,
        decimal debit,
        decimal credit,
        Guid? partyId = null)
    {
        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("The line identifier is required.", nameof(lineId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("The account identifier is required.", nameof(accountId));
        }

        if (partyId == Guid.Empty)
        {
            throw new ArgumentException("The party identifier cannot be empty.", nameof(partyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (TotalAmount != 0)
        {
            throw new InvalidOperationException("A completed journal entry cannot receive more lines.");
        }

        debit = decimal.Round(debit, 2, MidpointRounding.AwayFromZero);
        credit = decimal.Round(credit, 2, MidpointRounding.AwayFromZero);
        if (debit < 0 || credit < 0 || (debit == 0) == (credit == 0))
        {
            throw new ArgumentException("A line must contain one positive debit or credit amount.");
        }

        if (Lines.Any(item => item.Id == lineId))
        {
            throw new ArgumentException("The line identifier is already present.", nameof(lineId));
        }

        Lines.Add(new JournalEntryLine(
            lineId,
            CompanyId,
            Id,
            accountId,
            Lines.Count + 1,
            description.Trim(),
            debit,
            credit,
            partyId));
    }

    public void Complete(JournalEntryStatus status)
    {
        if (status is not (JournalEntryStatus.PendingReview or JournalEntryStatus.Reviewed))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (Lines.Count < 2)
        {
            throw new InvalidOperationException("A journal entry requires at least two lines.");
        }

        var totalDebit = Lines.Sum(item => item.Debit);
        var totalCredit = Lines.Sum(item => item.Credit);
        if (totalDebit != totalCredit)
        {
            throw new InvalidOperationException("Journal entry debit and credit totals must be equal.");
        }

        TotalAmount = totalDebit;
        Status = status;
    }

    public void MarkReviewed()
    {
        if (Status != JournalEntryStatus.PendingReview)
        {
            throw new InvalidOperationException("Só um lançamento por conferir pode ser conferido.");
        }

        Status = JournalEntryStatus.Reviewed;
    }

    /// <summary>Marca a origem do lançamento. Só se define uma vez, antes de gravar.</summary>
    public void SetSource(EntrySource source)
    {
        Source = source;
    }

    /// <summary>Devolve um lançamento conferido ao estado "por conferir" para poder ser corrigido.</summary>
    public void Reopen()
    {
        if (Status != JournalEntryStatus.Reviewed)
        {
            throw new InvalidOperationException("Só um lançamento conferido pode ser reaberto.");
        }

        Status = JournalEntryStatus.PendingReview;
    }

    public void Void()
    {
        Status = JournalEntryStatus.Voided;
    }

    /// <summary>Define ou remove (com `null` ou texto vazio) as observações internas.</summary>
    public void SetNotes(string? notes)
    {
        Notes = NormalizeNotes(notes);
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();
        if (trimmed.Length > NotesMaxLength)
        {
            throw new ArgumentException($"As observações não podem exceder {NotesMaxLength} caracteres.", nameof(notes));
        }

        return trimmed;
    }
}
