namespace AccountingAI.Domain.Organizations;

public sealed class UserCompanySettings
{
    private UserCompanySettings()
    {
    }

    public UserCompanySettings(
        Guid companyId,
        Guid userId,
        string culture = "pt-PT",
        string timeZone = "Europe/Lisbon")
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        CompanyId = companyId;
        UserId = userId;
        Update(culture, timeZone, null, InterfaceTheme.System, false, 50, InterfaceTextSize.Normal);
    }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public string Culture { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = string.Empty;

    public int? DefaultFiscalYear { get; private set; }

    public InterfaceTheme Theme { get; private set; }

    public bool CompactMode { get; private set; }

    public int PageSize { get; private set; }

    public InterfaceTextSize TextSize { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public CompanyUser CompanyUser { get; private set; } = null!;

    public void Update(
        string culture,
        string timeZone,
        int? defaultFiscalYear,
        InterfaceTheme theme,
        bool compactMode,
        int pageSize,
        InterfaceTextSize textSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);

        if (defaultFiscalYear is < 2000 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultFiscalYear));
        }

        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        if (pageSize is < 10 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        if (!Enum.IsDefined(textSize))
        {
            throw new ArgumentOutOfRangeException(nameof(textSize));
        }

        Culture = culture.Trim();
        TimeZone = timeZone.Trim();
        DefaultFiscalYear = defaultFiscalYear;
        Theme = theme;
        CompactMode = compactMode;
        PageSize = pageSize;
        TextSize = textSize;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marca <paramref name="fiscalYear"/> como o exercício do utilizador nesta empresa,
    /// sem tocar nas restantes preferências. Usado depois de importar um SAF-T: o
    /// exercício do ficheiro passa a existir para os filtros de período, em vez de cair
    /// no ano corrente por não haver nenhum exercício definido.
    /// </summary>
    public void SetDefaultFiscalYear(int fiscalYear)
    {
        if (fiscalYear is < 2000 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalYear));
        }

        DefaultFiscalYear = fiscalYear;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
