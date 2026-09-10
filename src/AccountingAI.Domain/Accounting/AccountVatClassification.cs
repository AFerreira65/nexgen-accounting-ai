namespace AccountingAI.Domain.Accounting;

/// <summary>
/// Classificação de IVA de uma conta: a taxa, a natureza da operação e a região.
/// É metadados opcional — uma linha por conta — para a desagregação do IVA por
/// taxa/natureza que a declaração periódica exige. Não toca no
/// <see cref="LedgerAccount"/>.
/// </summary>
public sealed class AccountVatClassification
{
    private AccountVatClassification()
    {
    }

    private AccountVatClassification(
        Guid id, Guid companyId, Guid accountId, VatRate rate, VatOperationNature nature, VatRegion region)
    {
        Id = id;
        CompanyId = companyId;
        AccountId = accountId;
        Rate = rate;
        Nature = nature;
        Region = region;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid AccountId { get; private set; }

    public VatRate Rate { get; private set; }

    public VatOperationNature Nature { get; private set; }

    public VatRegion Region { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>`true` quando não há nada classificado (a linha pode ser apagada).</summary>
    public bool IsEmpty => Rate == VatRate.Nenhuma && Nature == VatOperationNature.Nenhuma;

    public static AccountVatClassification Create(
        Guid companyId, Guid accountId, VatRate rate, VatOperationNature nature, VatRegion region)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A conta é obrigatória.", nameof(accountId));
        }

        return new AccountVatClassification(Guid.NewGuid(), companyId, accountId, rate, nature, region);
    }

    public void Update(VatRate rate, VatOperationNature nature, VatRegion region)
    {
        Rate = rate;
        Nature = nature;
        Region = region;
    }
}
