namespace AccountingAI.Domain.Accounting;

public sealed class AccountingDocumentType
{
    private AccountingDocumentType()
    {
    }

    public AccountingDocumentType(
        Guid id,
        Guid companyId,
        Guid defaultJournalId,
        string code,
        string name,
        string? sourceTemplateVersion = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The document type identifier is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        if (defaultJournalId == Guid.Empty)
        {
            throw new ArgumentException("The default journal identifier is required.", nameof(defaultJournalId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        CompanyId = companyId;
        DefaultJournalId = defaultJournalId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        SourceTemplateVersion = string.IsNullOrWhiteSpace(sourceTemplateVersion)
            ? null
            : sourceTemplateVersion.Trim();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid DefaultJournalId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? SourceTemplateVersion { get; private set; }

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public AccountingJournal DefaultJournal { get; private set; } = null!;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void ChangeDefaultJournal(Guid defaultJournalId)
    {
        if (defaultJournalId == Guid.Empty)
        {
            throw new ArgumentException("The default journal identifier is required.", nameof(defaultJournalId));
        }

        DefaultJournalId = defaultJournalId;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Reactivate()
    {
        IsActive = true;
    }
}
