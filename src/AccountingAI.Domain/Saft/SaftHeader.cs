namespace AccountingAI.Domain.Saft;

/// <summary>
/// Representação validada do elemento <c>Header</c> de um ficheiro SAF-T (PT).
/// É um valor de leitura: não é persistido e não representa uma empresa.
/// </summary>
public sealed class SaftHeader
{
    public SaftHeader(
        string auditFileVersion,
        string taxRegistrationNumber,
        string taxAccountingBasis,
        string companyName,
        int fiscalYear,
        DateOnly startDate,
        DateOnly endDate,
        string currencyCode,
        string? businessName = null,
        DateOnly? dateCreated = null,
        string? taxEntity = null,
        string? productId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditFileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxRegistrationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxAccountingBasis);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        var normalizedCurrency = currencyCode.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ArgumentException("A moeda tem de usar um código de três letras.", nameof(currencyCode));
        }

        if (fiscalYear is < 2000 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalYear), fiscalYear, "O exercício está fora do intervalo esperado.");
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("A data final do período é anterior à data inicial.", nameof(endDate));
        }

        AuditFileVersion = auditFileVersion.Trim();
        TaxRegistrationNumber = taxRegistrationNumber.Trim().ToUpperInvariant();
        TaxAccountingBasis = taxAccountingBasis.Trim().ToUpperInvariant();
        CompanyName = companyName.Trim();
        FiscalYear = fiscalYear;
        StartDate = startDate;
        EndDate = endDate;
        CurrencyCode = normalizedCurrency;
        BusinessName = string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim();
        DateCreated = dateCreated;
        TaxEntity = string.IsNullOrWhiteSpace(taxEntity) ? null : taxEntity.Trim();
        ProductId = string.IsNullOrWhiteSpace(productId) ? null : productId.Trim();
    }

    public string AuditFileVersion { get; }

    public string TaxRegistrationNumber { get; }

    /// <summary>Valor de <c>TaxAccountingBasis</c>: F, P, C, I, R, S, T ou E.</summary>
    public string TaxAccountingBasis { get; }

    public string CompanyName { get; }

    public int FiscalYear { get; }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    public string CurrencyCode { get; }

    public string? BusinessName { get; }

    public DateOnly? DateCreated { get; }

    public string? TaxEntity { get; }

    public string? ProductId { get; }

    /// <summary>Verdadeiro quando o ficheiro contém contabilidade (<c>C</c> ou <c>I</c>).</summary>
    public bool IncludesAccounting =>
        TaxAccountingBasis is "C" or "I";
}
