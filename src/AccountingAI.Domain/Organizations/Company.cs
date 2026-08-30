namespace AccountingAI.Domain.Organizations;

public sealed class Company
{
    private Company()
    {
    }

    public Company(
        Guid id,
        Guid accountingFirmId,
        string legalName,
        string taxRegistrationNumber,
        TaxRegion taxRegion,
        string baseCurrency = "EUR")
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(id));
        }

        if (accountingFirmId == Guid.Empty)
        {
            throw new ArgumentException("The accounting firm identifier is required.", nameof(accountingFirmId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxRegistrationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);

        if (!Enum.IsDefined(taxRegion))
        {
            throw new ArgumentOutOfRangeException(nameof(taxRegion));
        }

        var normalizedCurrency = baseCurrency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ArgumentException("The base currency must use a three-letter code.", nameof(baseCurrency));
        }

        Id = id;
        AccountingFirmId = accountingFirmId;
        LegalName = legalName.Trim();
        TaxRegistrationNumber = taxRegistrationNumber.Trim().ToUpperInvariant();
        TaxRegion = taxRegion;
        BaseCurrency = normalizedCurrency;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid AccountingFirmId { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string TaxRegistrationNumber { get; private set; } = string.Empty;

    public TaxRegion TaxRegion { get; private set; }

    public string BaseCurrency { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public AccountingFirm AccountingFirm { get; private set; } = null!;

    public ICollection<CompanyUser> UserMemberships { get; } = [];
}
