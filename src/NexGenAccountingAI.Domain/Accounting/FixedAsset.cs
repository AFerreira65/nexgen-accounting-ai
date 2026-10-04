namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// Um ativo fixo (tangível ou intangível) da empresa, depreciado por quotas
/// constantes: a taxa anual aplica-se ao valor de aquisição menos o valor
/// residual, a partir do ano de entrada em funcionamento. Com
/// <see cref="ByMonths"/> a quota do primeiro e do último ano é por
/// duodécimos; sem ele, o ano de início leva a quota anual inteira e o ano da
/// alienação não leva nenhuma. A taxa é indicada pelo utilizador (as tabelas
/// do Decreto Regulamentar n.º 25/2009 não estão na aplicação).
/// </summary>
public sealed class FixedAsset
{
    public const int CodeMaxLength = 20;

    public const int DescriptionMaxLength = 200;

    private FixedAsset()
    {
    }

    private FixedAsset(Guid id, Guid companyId, string code)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public DateOnly AcquisitionDate { get; private set; }

    /// <summary>A data de entrada em funcionamento ou utilização: é dela que conta a depreciação.</summary>
    public DateOnly StartOfUseDate { get; private set; }

    public decimal AcquisitionValue { get; private set; }

    public decimal ResidualValue { get; private set; }

    /// <summary>A taxa anual de depreciação, em percentagem (ex.: 25 para quatro anos).</summary>
    public decimal AnnualRate { get; private set; }

    /// <summary>Depreciação por duodécimos no ano de início e no ano da alienação.</summary>
    public bool ByMonths { get; private set; }

    /// <summary>A conta do ativo (43 ou 44).</summary>
    public Guid? AssetAccountId { get; private set; }

    /// <summary>A conta das depreciações acumuladas (438 ou 448).</summary>
    public Guid? AccumulatedAccountId { get; private set; }

    /// <summary>A conta do gasto de depreciação (642 ou 643).</summary>
    public Guid? ExpenseAccountId { get; private set; }

    /// <summary>A data da alienação ou do abate, quando já saiu da empresa.</summary>
    public DateOnly? DisposalDate { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public LedgerAccount? AssetAccount { get; private set; }

    public LedgerAccount? AccumulatedAccount { get; private set; }

    public LedgerAccount? ExpenseAccount { get; private set; }

    /// <summary>O valor sobre o qual incide a taxa.</summary>
    public decimal DepreciableValue => AcquisitionValue - ResidualValue;

    public static FixedAsset Create(Guid companyId, string code)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new FixedAsset(Guid.NewGuid(), companyId, NormalizeCode(code));
    }

    public void Update(
        string description,
        DateOnly acquisitionDate,
        DateOnly startOfUseDate,
        decimal acquisitionValue,
        decimal residualValue,
        decimal annualRate,
        bool byMonths,
        DateOnly? disposalDate)
    {
        var normalized = (description ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A descrição é obrigatória.", nameof(description));
        }

        if (normalized.Length > DescriptionMaxLength)
        {
            throw new ArgumentException($"A descrição não pode ter mais de {DescriptionMaxLength} caracteres.", nameof(description));
        }

        if (startOfUseDate < acquisitionDate)
        {
            throw new ArgumentException("A entrada em funcionamento não pode ser anterior à aquisição.", nameof(startOfUseDate));
        }

        acquisitionValue = decimal.Round(acquisitionValue, 2, MidpointRounding.AwayFromZero);
        residualValue = decimal.Round(residualValue, 2, MidpointRounding.AwayFromZero);
        if (acquisitionValue <= 0m)
        {
            throw new ArgumentException("O valor de aquisição tem de ser positivo.", nameof(acquisitionValue));
        }

        if (residualValue < 0m || residualValue >= acquisitionValue)
        {
            throw new ArgumentException("O valor residual tem de ser inferior ao valor de aquisição e não pode ser negativo.", nameof(residualValue));
        }

        annualRate = decimal.Round(annualRate, 4, MidpointRounding.AwayFromZero);
        if (annualRate is <= 0m or > 100m)
        {
            throw new ArgumentException("A taxa anual tem de estar entre 0 e 100 %.", nameof(annualRate));
        }

        if (disposalDate is { } disposal && disposal < startOfUseDate)
        {
            throw new ArgumentException("A alienação não pode ser anterior à entrada em funcionamento.", nameof(disposalDate));
        }

        Description = normalized;
        AcquisitionDate = acquisitionDate;
        StartOfUseDate = startOfUseDate;
        AcquisitionValue = acquisitionValue;
        ResidualValue = residualValue;
        AnnualRate = annualRate;
        ByMonths = byMonths;
        DisposalDate = disposalDate;
    }

    public void LinkAccounts(Guid? assetAccountId, Guid? accumulatedAccountId, Guid? expenseAccountId)
    {
        AssetAccountId = assetAccountId;
        AccumulatedAccountId = accumulatedAccountId;
        ExpenseAccountId = expenseAccountId;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>A depreciação do exercício <paramref name="year"/>.</summary>
    public decimal DepreciationFor(int year) => AccumulatedAtEndOf(year) - AccumulatedAtEndOf(year - 1);

    /// <summary>As depreciações acumuladas no fim do exercício <paramref name="year"/>; nunca passam o valor depreciável.</summary>
    public decimal AccumulatedAtEndOf(int year)
    {
        var annual = decimal.Round(DepreciableValue * AnnualRate / 100m, 2, MidpointRounding.AwayFromZero);
        var accumulated = 0m;
        for (var current = StartOfUseDate.Year; current <= year; current++)
        {
            if (DisposalDate is { } disposal && current > disposal.Year)
            {
                break;
            }

            var quota = annual;
            if (ByMonths)
            {
                // O mês de início conta; na alienação contam os meses até ao anterior.
                var first = current == StartOfUseDate.Year ? StartOfUseDate.Month : 1;
                var last = DisposalDate is { } sold && current == sold.Year ? sold.Month - 1 : 12;
                quota = decimal.Round(annual * Math.Max(0, last - first + 1) / 12m, 2, MidpointRounding.AwayFromZero);
            }
            else if (DisposalDate is { } sold && current == sold.Year)
            {
                quota = 0m;
            }

            accumulated = Math.Min(DepreciableValue, accumulated + quota);
            if (accumulated == DepreciableValue)
            {
                break;
            }
        }

        return accumulated;
    }

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

        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.'))
        {
            throw new ArgumentException("O código só pode ter letras, algarismos, pontos e hífenes.", nameof(code));
        }

        return normalized;
    }
}
