namespace AccountingAI.Domain.Accounting;

/// <summary>
/// Um código de IVA da empresa: junta a taxa, a percentagem, a natureza da
/// operação e a região num código curto que se indica na linha do lançamento.
/// É o que permite desagregar o IVA por documento, e não só por conta
/// (<see cref="AccountVatClassification"/>).
/// </summary>
public sealed class VatCode
{
    public const int CodeMaxLength = 12;

    public const int NameMaxLength = 120;

    private VatCode()
    {
    }

    private VatCode(Guid id, Guid companyId, string code)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public VatRate Rate { get; private set; }

    /// <summary>A percentagem de imposto (ex.: 23); zero nas operações isentas.</summary>
    public decimal Percentage { get; private set; }

    public VatOperationNature Nature { get; private set; }

    public VatRegion Region { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static VatCode Create(
        Guid companyId, string code, string name, VatRate rate, decimal percentage, VatOperationNature nature, VatRegion region)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        var vatCode = new VatCode(Guid.NewGuid(), companyId, NormalizeCode(code));
        vatCode.Update(name, rate, percentage, nature, region);
        return vatCode;
    }

    public void Update(string name, VatRate rate, decimal percentage, VatOperationNature nature, VatRegion region)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("A descrição é obrigatória.", nameof(name));
        }

        if (normalizedName.Length > NameMaxLength)
        {
            throw new ArgumentException($"A descrição não pode ter mais de {NameMaxLength} caracteres.", nameof(name));
        }

        if (rate == VatRate.Nenhuma)
        {
            throw new ArgumentException("Indique a taxa (isento, reduzida, intermédia ou normal).", nameof(rate));
        }

        if (nature == VatOperationNature.Nenhuma)
        {
            throw new ArgumentException("Indique a natureza da operação.", nameof(nature));
        }

        percentage = decimal.Round(percentage, 2, MidpointRounding.AwayFromZero);
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentException("A percentagem tem de estar entre 0 e 100.", nameof(percentage));
        }

        if (rate == VatRate.Isento && percentage != 0)
        {
            throw new ArgumentException("Um código isento tem percentagem zero.", nameof(percentage));
        }

        if (rate != VatRate.Isento && percentage == 0)
        {
            throw new ArgumentException("Um código com taxa tem de ter percentagem; use «isento» para zero.", nameof(percentage));
        }

        Name = normalizedName;
        Rate = rate;
        Percentage = percentage;
        Nature = nature;
        Region = region;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public static string NormalizeCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("O código é obrigatório.", nameof(code));
        }

        if (normalized.Length > CodeMaxLength)
        {
            throw new ArgumentException($"O código não pode ter mais de {CodeMaxLength} caracteres.", nameof(code));
        }

        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException("O código só pode ter letras, algarismos e hífenes.", nameof(code));
        }

        return normalized;
    }
}
