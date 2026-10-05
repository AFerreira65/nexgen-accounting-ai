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

    public const int EntryReferenceMaxLength = 48;

    /// <summary>A referência do lançamento da alienação ou do abate, quando foi gerado pela aplicação.</summary>
    public string? DisposalEntryReference { get; private set; }

    /// <summary>O preço de venda (sem IVA) na alienação; zero num abate.</summary>
    public decimal? DisposalPrice { get; private set; }

    /// <summary>
    /// O coeficiente de desvalorização da moeda aplicado na mais-valia fiscal
    /// (artigo 47.º do CIRC), indicado pelo utilizador a partir da portaria do ano;
    /// `null` conta como 1.
    /// </summary>
    public decimal? MonetaryCoefficient { get; private set; }

    /// <summary>O código do ativo nas tabelas anexas ao Decreto Regulamentar n.º 25/2009 (quatro dígitos), para o Modelo 32.</summary>
    public string? TaxTableCode { get; private set; }

    /// <summary>
    /// A taxa máxima fiscal das tabelas do Decreto Regulamentar n.º 25/2009, em
    /// percentagem, indicada pelo utilizador; `null` assume a taxa da contabilidade.
    /// </summary>
    public decimal? TaxRate { get; private set; }

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

    /// <summary>Os dados fiscais do Modelo 32: o código e a taxa máxima das tabelas do DR n.º 25/2009.</summary>
    public void SetTaxData(string? taxTableCode, decimal? taxRate)
    {
        var code = string.IsNullOrWhiteSpace(taxTableCode) ? null : taxTableCode.Trim();
        if (code is not null && (code.Length != 4 || !code.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("O código das tabelas do DR n.º 25/2009 tem quatro dígitos.", nameof(taxTableCode));
        }

        if (taxRate is { } rate && (rate <= 0m || rate > 100m))
        {
            throw new ArgumentException("A taxa fiscal tem de estar entre 0 e 100 %.", nameof(taxRate));
        }

        TaxTableCode = code;
        TaxRate = taxRate is null ? null : decimal.Round(taxRate.Value, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>O coeficiente de desvalorização, indicado depois de lançada a alienação.</summary>
    public void SetMonetaryCoefficient(decimal? coefficient)
    {
        if (coefficient is <= 0m)
        {
            throw new ArgumentException("O coeficiente de desvalorização tem de ser positivo.", nameof(coefficient));
        }

        MonetaryCoefficient = coefficient;
    }

    /// <summary>
    /// O limite fiscal do exercício (coluna 12 do Modelo 32): a taxa máxima — ou,
    /// sem ela, a da contabilidade — sobre o valor de aquisição (coluna 6), com a
    /// mesma regra de início e de fim (duodécimos ou ano inteiro).
    /// </summary>
    public decimal FiscalLimitFor(int year) =>
        AccumulatedWith(year, TaxRate ?? AnnualRate, AcquisitionValue) - AccumulatedWith(year - 1, TaxRate ?? AnnualRate, AcquisitionValue);

    /// <summary>
    /// As depreciações aceites fiscalmente até ao fim do exercício: em cada ano, a
    /// contabilizada até ao limite fiscal. O excesso de um ano não se recupera
    /// aqui (a recuperação do artigo 20.º do DR n.º 25/2009 não está calculada).
    /// </summary>
    public decimal FiscalAcceptedAtEndOf(int year)
    {
        var accepted = 0m;
        for (var current = StartOfUseDate.Year; current <= year; current++)
        {
            accepted += Math.Min(DepreciationFor(current), FiscalLimitFor(current));
        }

        return accepted;
    }

    /// <summary>Regista a saída do ativo: a data (que acerta as depreciações desse ano) e o lançamento que a contabiliza.</summary>
    public void RecordDisposal(DateOnly date, string entryReference, decimal price = 0m, decimal? monetaryCoefficient = null)
    {
        if (price < 0m)
        {
            throw new ArgumentException("O preço de venda não pode ser negativo.", nameof(price));
        }

        if (monetaryCoefficient is <= 0m)
        {
            throw new ArgumentException("O coeficiente de desvalorização tem de ser positivo.", nameof(monetaryCoefficient));
        }

        DisposalPrice = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        MonetaryCoefficient = monetaryCoefficient;
        if (date < StartOfUseDate)
        {
            throw new ArgumentException("A alienação não pode ser anterior à entrada em funcionamento.", nameof(date));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entryReference);
        DisposalDate = date;
        DisposalEntryReference = entryReference.Trim();
    }

    /// <summary>
    /// Simula a saída numa data, sem a registar: as depreciações acumuladas até
    /// ao mês anterior (a regra de <see cref="AccumulatedAtEndOf"/>) e o valor
    /// líquido contabilístico nessa data.
    /// </summary>
    public (decimal Accumulated, decimal NetBookValue) ValuesAtDisposal(DateOnly date)
    {
        var saved = DisposalDate;
        DisposalDate = date;
        try
        {
            var accumulated = AccumulatedAtEndOf(date.Year);
            return (accumulated, AcquisitionValue - accumulated);
        }
        finally
        {
            DisposalDate = saved;
        }
    }

    /// <summary>A depreciação do exercício <paramref name="year"/>.</summary>
    public decimal DepreciationFor(int year) => AccumulatedAtEndOf(year) - AccumulatedAtEndOf(year - 1);

    /// <summary>As depreciações acumuladas no fim do exercício <paramref name="year"/>; nunca passam o valor depreciável.</summary>
    public decimal AccumulatedAtEndOf(int year) => AccumulatedWith(year, AnnualRate, DepreciableValue);

    private decimal AccumulatedWith(int year, decimal rate, decimal baseValue)
    {
        var annual = decimal.Round(baseValue * rate / 100m, 2, MidpointRounding.AwayFromZero);
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

            accumulated = Math.Min(baseValue, accumulated + quota);
            if (accumulated == baseValue)
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
