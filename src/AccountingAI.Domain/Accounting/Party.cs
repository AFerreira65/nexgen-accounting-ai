namespace AccountingAI.Domain.Accounting;

/// <summary>
/// Um terceiro da empresa — cliente, fornecedor ou ambos — identificado pelo
/// país e pelo número de contribuinte. É a base para as declarações que pedem o
/// NIF da contraparte (recapitulativa, Modelo 30) e para os pendentes por
/// entidade. As subcontas 21/22 a que está ligado são opcionais.
/// </summary>
public sealed class Party
{
    public const string DefaultCountryCode = "PT";

    public const int TaxRegistrationNumberMaxLength = 30;

    public const int NameMaxLength = 200;

    private Party()
    {
    }

    private Party(Guid id, Guid companyId, string countryCode, string taxRegistrationNumber, string name)
    {
        Id = id;
        CompanyId = companyId;
        CountryCode = countryCode;
        TaxRegistrationNumber = taxRegistrationNumber;
        Name = name;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Código ISO 3166-1 alfa-2 do país do contribuinte.</summary>
    public string CountryCode { get; private set; } = DefaultCountryCode;

    public string TaxRegistrationNumber { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsCustomer { get; private set; }

    public bool IsSupplier { get; private set; }

    /// <summary>A subconta de clientes (21) deste terceiro, se houver.</summary>
    public Guid? CustomerAccountId { get; private set; }

    /// <summary>A subconta de fornecedores (22) deste terceiro, se houver.</summary>
    public Guid? SupplierAccountId { get; private set; }

    /// <summary>A morada (rua, número, andar); `null` quando não está registada.</summary>
    public string? AddressDetail { get; private set; }

    public string? City { get; private set; }

    public string? PostalCode { get; private set; }

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Party Create(Guid companyId, string? countryCode, string taxRegistrationNumber, string name)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new Party(
            Guid.NewGuid(),
            companyId,
            NormalizeCountryCode(countryCode),
            NormalizeTaxRegistrationNumber(taxRegistrationNumber),
            NormalizeName(name));
    }

    public void Rename(string name) => Name = NormalizeName(name);

    public void SetRoles(bool isCustomer, bool isSupplier)
    {
        IsCustomer = isCustomer;
        IsSupplier = isSupplier;
    }

    public void LinkAccounts(Guid? customerAccountId, Guid? supplierAccountId)
    {
        CustomerAccountId = customerAccountId;
        SupplierAccountId = supplierAccountId;
    }

    /// <summary>Define a morada de faturação; os campos em branco ficam por registar.</summary>
    public void SetAddress(string? addressDetail, string? city, string? postalCode)
    {
        AddressDetail = Organizations.PostalAddress.Normalize(addressDetail, Organizations.PostalAddress.AddressDetailMaxLength, nameof(addressDetail));
        City = Organizations.PostalAddress.Normalize(city, Organizations.PostalAddress.CityMaxLength, nameof(city));
        PostalCode = Organizations.PostalAddress.Normalize(postalCode, Organizations.PostalAddress.PostalCodeMaxLength, nameof(postalCode));
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public static string NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return DefaultCountryCode;
        }

        var normalized = countryCode.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || !normalized.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("O país é um código de duas letras (ex.: PT, ES).", nameof(countryCode));
        }

        return normalized;
    }

    public static string NormalizeTaxRegistrationNumber(string? taxRegistrationNumber)
    {
        var normalized = new string((taxRegistrationNumber ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray())
            .ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("O número de contribuinte é obrigatório.", nameof(taxRegistrationNumber));
        }

        if (normalized.Length > TaxRegistrationNumberMaxLength)
        {
            throw new ArgumentException(
                $"O número de contribuinte não pode ter mais de {TaxRegistrationNumberMaxLength} caracteres.",
                nameof(taxRegistrationNumber));
        }

        return normalized;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("O nome é obrigatório.", nameof(name));
        }

        if (normalized.Length > NameMaxLength)
        {
            throw new ArgumentException($"O nome não pode ter mais de {NameMaxLength} caracteres.", nameof(name));
        }

        return normalized;
    }
}
