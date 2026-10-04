namespace AccountingAI.Domain.Accounting;

/// <summary>
/// O valor orçamentado para um mês de um exercício, numa conta de gastos ou
/// de rendimentos (ou num grupo de contas, pelo código que as agrega) e,
/// opcionalmente, num centro de custo. O valor é sempre positivo: a classe da
/// conta diz se é gasto ou rendimento.
/// </summary>
public sealed class BudgetLine
{
    public const int AccountCodeMaxLength = 30;

    private BudgetLine()
    {
    }

    private BudgetLine(Guid id, Guid companyId, int year, int month, string accountCode, Guid? costCenterId)
    {
        Id = id;
        CompanyId = companyId;
        Year = year;
        Month = month;
        AccountCode = accountCode;
        CostCenterId = costCenterId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    /// <summary>O código da conta, ou o prefixo que agrega as contas orçamentadas em conjunto (ex.: <c>62</c>).</summary>
    public string AccountCode { get; private set; } = string.Empty;

    public Guid? CostCenterId { get; private set; }

    public decimal Amount { get; private set; }

    public uint RowVersion { get; private set; }

    public CostCenter? CostCenter { get; private set; }

    public static BudgetLine Create(Guid companyId, int year, int month, string accountCode, Guid? costCenterId, decimal amount)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentException("O mês tem de estar entre 1 e 12.", nameof(month));
        }

        var line = new BudgetLine(Guid.NewGuid(), companyId, year, month, NormalizeAccountCode(accountCode), costCenterId);
        line.SetAmount(amount);
        return line;
    }

    public void SetAmount(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("O valor orçamentado não pode ser negativo.", nameof(amount));
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Só se orçamentam gastos (classe 6) e rendimentos (classe 7).</summary>
    public static string NormalizeAccountCode(string? accountCode)
    {
        var normalized = (accountCode ?? string.Empty).Trim();
        if (normalized.Length is 0 or > AccountCodeMaxLength || !normalized.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Indique o código de uma conta.", nameof(accountCode));
        }

        if (normalized[0] is not ('6' or '7'))
        {
            throw new ArgumentException("Só se orçamentam contas de gastos (classe 6) e de rendimentos (classe 7).", nameof(accountCode));
        }

        return normalized;
    }

    /// <summary>Reparte um valor anual por doze meses iguais; o acerto do arredondamento fica em dezembro.</summary>
    public static IReadOnlyList<decimal> SpreadEvenly(decimal annualAmount)
    {
        var monthly = decimal.Round(annualAmount / 12m, 2, MidpointRounding.AwayFromZero);
        var months = Enumerable.Repeat(monthly, 11).ToList();
        months.Add(annualAmount - (monthly * 11));
        return months;
    }
}
