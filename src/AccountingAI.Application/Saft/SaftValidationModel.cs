namespace AccountingAI.Application.Saft;

/// <summary>
/// O que uma passagem única em streaming a um SAF-T recolhe para as regras
/// analíticas correrem sem materializar o ficheiro: campos do cabeçalho, o
/// conjunto de contas do plano, contagens e totais do bloco de movimentos, e
/// listas curtas (limitadas) das transações problemáticas.
/// </summary>
public sealed record SaftValidationModel
{
    public string? AuditFileVersion { get; init; }

    public string? TaxRegistrationNumber { get; init; }

    public string? TaxAccountingBasis { get; init; }

    public int? FiscalYear { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public string? ProductId { get; init; }

    public string? ProductCompanyTaxId { get; init; }

    public string? SoftwareCertificateNumber { get; init; }

    /// <summary>`AccountID` de <c>MasterFiles/GeneralLedgerAccounts</c>.</summary>
    public IReadOnlySet<string> AccountIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Número de diários distintos no bloco de movimentos.</summary>
    public int JournalCount { get; init; }

    public bool HasGeneralLedgerEntries { get; init; }

    public int? DeclaredNumberOfEntries { get; init; }

    public decimal? DeclaredTotalDebit { get; init; }

    public decimal? DeclaredTotalCredit { get; init; }

    public int TransactionCount { get; init; }

    /// <summary>Soma de todas as linhas de débito das transações.</summary>
    public decimal LineDebitTotal { get; init; }

    /// <summary>Soma de todas as linhas de crédito das transações.</summary>
    public decimal LineCreditTotal { get; init; }

    public IReadOnlyList<SaftFlaggedTransaction> UnbalancedTransactions { get; init; } = [];

    public IReadOnlyList<SaftUnresolvedAccountRef> UnresolvedAccountRefs { get; init; } = [];

    public IReadOnlyList<SaftFlaggedTransaction> OutOfPeriodTransactions { get; init; } = [];

    /// <summary>Verdadeiro se alguma das listas de problemas foi cortada por atingir o limite.</summary>
    public bool FlaggedListsTruncated { get; init; }
}

/// <param name="TransactionId">`TransactionID` da transação.</param>
/// <param name="Date">`TransactionDate`, quando legível.</param>
/// <param name="DebitTotal">Soma das linhas de débito da transação.</param>
/// <param name="CreditTotal">Soma das linhas de crédito da transação.</param>
public sealed record SaftFlaggedTransaction(string TransactionId, DateOnly? Date, decimal DebitTotal, decimal CreditTotal);

/// <param name="TransactionId">Transação que refere a conta.</param>
/// <param name="AccountId">`AccountID` que não existe no plano do ficheiro.</param>
public sealed record SaftUnresolvedAccountRef(string TransactionId, string AccountId);
