namespace AccountingAI.Domain.Accounting;

public sealed class LedgerAccount
{
    private LedgerAccount()
    {
    }

    public LedgerAccount(
        Guid id,
        Guid companyId,
        string code,
        string name,
        string? parentCode = null,
        bool allowsPosting = true,
        string? sourceTemplateVersion = null,
        string? taxonomyReference = null,
        int? taxonomyCode = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The account identifier is required.", nameof(id));
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
        ParentCode = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim().ToUpperInvariant();
        AllowsPosting = allowsPosting;
        SourceTemplateVersion = string.IsNullOrWhiteSpace(sourceTemplateVersion)
            ? null
            : sourceTemplateVersion.Trim();
        AssignTaxonomy(taxonomyReference, taxonomyCode);
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? ParentCode { get; private set; }

    public bool AllowsPosting { get; private set; }

    public string? SourceTemplateVersion { get; private set; }

    public string? TaxonomyReference { get; private set; }

    public int? TaxonomyCode { get; private set; }

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public ICollection<JournalEntryLine> EntryLines { get; } = [];

    public void AssignTaxonomy(string? taxonomyReference, int? taxonomyCode)
    {
        if (string.IsNullOrWhiteSpace(taxonomyReference) && taxonomyCode is null)
        {
            TaxonomyReference = null;
            TaxonomyCode = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(taxonomyReference) || taxonomyCode is null or <= 0)
        {
            throw new ArgumentException("Taxonomy reference and a positive code must be provided together.");
        }

        if (!AllowsPosting)
        {
            throw new InvalidOperationException("Only posting accounts can have a taxonomy code.");
        }

        var normalizedReference = taxonomyReference.Trim().ToUpperInvariant();
        if (normalizedReference is not ("S" or "M" or "N" or "O"))
        {
            throw new ArgumentException("Taxonomy reference must be S, M, N or O.", nameof(taxonomyReference));
        }

        if (normalizedReference == "O" && taxonomyCode != 1)
        {
            throw new ArgumentException("Taxonomy reference O must use code 1.", nameof(taxonomyCode));
        }

        TaxonomyReference = normalizedReference;
        TaxonomyCode = taxonomyCode;
    }
}
