namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// O que o utilizador indica, por exercício e por conta, para o Modelo 30 (mapa
/// de provisões, perdas por imparidade em créditos e ajustamentos em
/// inventários, Portaria n.º 92-A/2011): o grupo do mapa — I (imparidades em
/// créditos e ajustamentos em inventários), II (provisões ou imparidades
/// fiscalmente não dedutíveis), III (provisões fiscalmente dedutíveis) — e as
/// partes já tributadas que a contabilidade não separa: do saldo inicial
/// (coluna 3), da utilização (coluna 5) e da reversão (coluna 7).
/// </summary>
public sealed class ProvisionMapInput
{
    public const int AccountCodeMaxLength = 30;

    private ProvisionMapInput()
    {
    }

    private ProvisionMapInput(Guid id, Guid companyId, int year, string accountCode)
    {
        Id = id;
        CompanyId = companyId;
        Year = year;
        AccountCode = accountCode;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int Year { get; private set; }

    public string AccountCode { get; private set; } = string.Empty;

    /// <summary>1, 2 ou 3 — os grupos I, II e III do mapa.</summary>
    public int Group { get; private set; }

    public decimal TaxedOpening { get; private set; }

    public decimal TaxedUsed { get; private set; }

    public decimal TaxedReversed { get; private set; }

    public uint RowVersion { get; private set; }

    public static ProvisionMapInput Create(Guid companyId, int year, string accountCode)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        var code = (accountCode ?? string.Empty).Trim();
        if (code.Length is 0 or > AccountCodeMaxLength)
        {
            throw new ArgumentException("A conta é obrigatória.", nameof(accountCode));
        }

        return new ProvisionMapInput(Guid.NewGuid(), companyId, year, code);
    }

    public void Update(int group, decimal taxedOpening, decimal taxedUsed, decimal taxedReversed)
    {
        if (group is < 1 or > 3)
        {
            throw new ArgumentException("O grupo tem de ser I, II ou III.", nameof(group));
        }

        if (taxedOpening < 0m || taxedUsed < 0m || taxedReversed < 0m)
        {
            throw new ArgumentException("As partes tributadas não podem ser negativas.");
        }

        Group = group;
        TaxedOpening = decimal.Round(taxedOpening, 2, MidpointRounding.AwayFromZero);
        TaxedUsed = decimal.Round(taxedUsed, 2, MidpointRounding.AwayFromZero);
        TaxedReversed = decimal.Round(taxedReversed, 2, MidpointRounding.AwayFromZero);
    }
}
