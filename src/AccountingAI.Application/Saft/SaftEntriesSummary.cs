namespace AccountingAI.Application.Saft;

/// <summary>Um diário do bloco <c>GeneralLedgerEntries</c> de um SAF-T e a contagem de transações.</summary>
public sealed record SaftJournalSummary(string Code, string Description, int TransactionCount);

/// <summary>
/// Resumo dos movimentos de um SAF-T (bloco <c>GeneralLedgerEntries</c>), obtido
/// por leitura em streaming sem materializar as linhas. Um ficheiro sem o bloco
/// devolve `HasEntries = false`.
/// </summary>
public sealed record SaftEntriesSummary(
    bool HasEntries,
    int TransactionCount,
    int? DeclaredEntryCount,
    decimal? TotalDebit,
    decimal? TotalCredit,
    DateOnly? FromDate,
    DateOnly? ToDate,
    IReadOnlyList<SaftJournalSummary> Journals)
{
    public bool Balanced => TotalDebit is { } debit && TotalCredit is { } credit &&
        decimal.Round(debit, 2) == decimal.Round(credit, 2);
}

/// <summary>Um diário do resumo com a indicação de se já existe na empresa.</summary>
public sealed record SaftEntriesJournalStatus(string Code, string Description, int TransactionCount, bool Exists);

/// <summary>
/// Reconciliação dos movimentos de um SAF-T com uma empresa: o resumo do ficheiro
/// mais a indicação, por diário, de se já existe na empresa. Só lê.
/// </summary>
public sealed record SaftEntriesReconciliation(
    bool HasEntries,
    int TransactionCount,
    int? DeclaredEntryCount,
    decimal? TotalDebit,
    decimal? TotalCredit,
    bool Balanced,
    DateOnly? FromDate,
    DateOnly? ToDate,
    IReadOnlyList<SaftEntriesJournalStatus> Journals);
