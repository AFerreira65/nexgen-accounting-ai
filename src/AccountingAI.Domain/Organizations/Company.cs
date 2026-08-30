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
        string baseCurrency = "EUR",
        AccountingStandard accountingStandard = AccountingStandard.SncGeneral)
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

        if (!Enum.IsDefined(accountingStandard))
        {
            throw new ArgumentOutOfRangeException(nameof(accountingStandard));
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
        AccountingStandard = accountingStandard;
        BaseCurrency = normalizedCurrency;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid AccountingFirmId { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string TaxRegistrationNumber { get; private set; } = string.Empty;

    public TaxRegion TaxRegion { get; private set; }

    public AccountingStandard AccountingStandard { get; private set; }

    public string? AccountingSetupVersion { get; private set; }

    public DateTimeOffset? AccountingSetupCompletedAtUtc { get; private set; }

    public string BaseCurrency { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public AccountingFirm AccountingFirm { get; private set; } = null!;

    public ICollection<CompanyUser> UserMemberships { get; } = [];

    public void ChangeAccountingStandard(AccountingStandard accountingStandard)
    {
        if (!Enum.IsDefined(accountingStandard))
        {
            throw new ArgumentOutOfRangeException(nameof(accountingStandard));
        }

        if (AccountingSetupVersion is not null && AccountingStandard != accountingStandard)
        {
            throw new InvalidOperationException(
                "The accounting standard cannot be changed after accounting setup.");
        }

        AccountingStandard = accountingStandard;
    }

    public void MarkAccountingSetup(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        AccountingSetupVersion = version.Trim();
        AccountingSetupCompletedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Rename(string legalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        LegalName = legalName.Trim();
    }

    public void ChangeTaxRegion(TaxRegion taxRegion)
    {
        if (!Enum.IsDefined(taxRegion))
        {
            throw new ArgumentOutOfRangeException(nameof(taxRegion));
        }

        TaxRegion = taxRegion;
    }

    public void ChangeBaseCurrency(string baseCurrency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        var normalized = baseCurrency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new ArgumentException("The base currency must use a three-letter code.", nameof(baseCurrency));
        }

        BaseCurrency = normalized;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
