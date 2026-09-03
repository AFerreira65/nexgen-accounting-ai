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

/// <summary>Uma linha de uma transação do SAF-T: uma conta e um débito ou um crédito.</summary>
public sealed record SaftTransactionLine(string AccountCode, string Description, decimal Debit, decimal Credit);

/// <summary>
/// Uma transação completa do bloco <c>GeneralLedgerEntries</c>, com as linhas. É lida
/// em streaming, uma transação de cada vez, para não carregar o ficheiro inteiro.
/// </summary>
public sealed record SaftTransaction(
    string JournalCode,
    string JournalDescription,
    string TransactionId,
    DateOnly? Date,
    string Description,
    string DocumentReference,
    IReadOnlyList<SaftTransactionLine> Lines);

/// <summary>Uma transação do ficheiro que não foi importada e a razão (em pt-PT).</summary>
public sealed record SaftEntrySkipped(string TransactionId, string Reason);

/// <summary>
/// As transações do bloco <c>GeneralLedgerEntries</c> com as linhas, mais os totais
/// declarados no cabeçalho do bloco (<c>NumberOfEntries</c>, <c>TotalDebit</c>,
/// <c>TotalCredit</c>). Lido em streaming, transação a transação.
/// </summary>
public sealed record SaftTransactions(
    int? DeclaredEntryCount,
    decimal? DeclaredTotalDebit,
    decimal? DeclaredTotalCredit,
    IReadOnlyList<SaftTransaction> Items);

/// <summary>
/// Resultado da importação dos movimentos de um SAF-T para uma empresa: quantos
/// lançamentos entraram, quantos foram saltados (com a razão), e os diários criados;
/// mais a reconciliação com os totais declarados no ficheiro — a soma dos débitos e
/// créditos da empresa (sem anulados) no período do ficheiro face ao
/// <c>TotalDebit</c>/<c>TotalCredit</c> declarados.
/// </summary>
public sealed record SaftEntriesImportResult(
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> JournalsCreated,
    IReadOnlyList<SaftEntrySkipped> Skipped,
    int? DeclaredEntryCount,
    decimal? DeclaredTotalDebit,
    decimal? DeclaredTotalCredit,
    DateOnly? FilePeriodFrom,
    DateOnly? FilePeriodTo,
    decimal CompanyPeriodDebit,
    decimal CompanyPeriodCredit)
{
    /// <summary>
    /// `true` quando o ficheiro declara totais e a soma dos débitos e créditos da
    /// empresa no período do ficheiro coincide (a 2 casas) com esses totais;
    /// `null` quando o ficheiro não declara totais.
    /// </summary>
    public bool? MatchesDeclaredTotals =>
        DeclaredTotalDebit is { } declaredDebit && DeclaredTotalCredit is { } declaredCredit
            ? decimal.Round(CompanyPeriodDebit, 2) == decimal.Round(declaredDebit, 2) &&
              decimal.Round(CompanyPeriodCredit, 2) == decimal.Round(declaredCredit, 2)
            : null;
}
