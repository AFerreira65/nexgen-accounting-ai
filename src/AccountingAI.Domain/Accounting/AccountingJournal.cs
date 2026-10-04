namespace AccountingAI.Domain.Accounting;

public sealed class AccountingJournal
{
    private AccountingJournal()
    {
    }

    public AccountingJournal(
        Guid id,
        Guid companyId,
        string code,
        string name,
        string? sourceTemplateVersion = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The journal identifier is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        SourceTemplateVersion = string.IsNullOrWhiteSpace(sourceTemplateVersion)
            ? null
            : sourceTemplateVersion.Trim();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? SourceTemplateVersion { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public ICollection<JournalEntry> Entries { get; } = [];

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
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
