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
    IReadOnlyList<SaftTransactionLine> Lines,
    string? CustomerId = null,
    string? SupplierId = null);

/// <summary>
/// O resultado de ligar (ou analisar, com <see cref="Applied"/> falso) os
/// terceiros de um SAF-T aos lançamentos que dele já foram importados. Só as
/// linhas sem terceiro são preenchidas; nenhum valor nem conta é alterado.
/// </summary>
/// <param name="Applied">Se as ligações foram gravadas.</param>
/// <param name="TransactionCount">Transações do ficheiro que indicam um cliente ou um fornecedor.</param>
/// <param name="EntriesMatched">Dessas, as que têm um lançamento importado igual ao do ficheiro.</param>
/// <param name="EntriesNotFound">As que não têm lançamento importado com essa referência.</param>
/// <param name="EntriesChanged">As cujo lançamento já não tem as mesmas linhas do ficheiro (foi corrigido) e fica como está.</param>
/// <param name="LinesLinked">Linhas que ficam (ou ficariam) com terceiro.</param>
/// <param name="LinesAlreadyLinked">Linhas que já tinham terceiro e não se alteram.</param>
/// <param name="TransactionsWithUnknownParty">Transações cujo terceiro ainda não existe na empresa.</param>
public sealed record SaftPartyLinkResult(
    bool Applied,
    int TransactionCount,
    int EntriesMatched,
    int EntriesNotFound,
    int EntriesChanged,
    int LinesLinked,
    int LinesAlreadyLinked,
    int TransactionsWithUnknownParty);

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
    IReadOnlyList<SaftTransaction> Items,
    IReadOnlyList<SaftParty>? Parties = null);

/// <summary>
/// Análise (sem escrever) do que aconteceria ao importar os movimentos de um SAF-T
/// para uma empresa: quantos lançamentos entrariam, quantos seriam saltados (com a
/// razão), que diários seriam criados, e a reconciliação prevista — a soma atual da
/// empresa no período do ficheiro mais os débitos e créditos que entrariam, face
/// aos totais declarados.
/// </summary>
public sealed record SaftEntriesAnalysis(
    int WouldImportCount,
    int WouldSkipCount,
    IReadOnlyList<string> JournalsToCreate,
    IReadOnlyList<SaftEntrySkipped> Skipped,
    int? DeclaredEntryCount,
    decimal? DeclaredTotalDebit,
    decimal? DeclaredTotalCredit,
    DateOnly? FilePeriodFrom,
    DateOnly? FilePeriodTo,
    decimal ProjectedPeriodDebit,
    decimal ProjectedPeriodCredit)
{
    /// <summary>
    /// `true` quando o ficheiro declara totais e a soma prevista da empresa no
    /// período (o que já lá está mais o que entraria) coincide, a 2 casas, com
    /// esses totais; `null` quando o ficheiro não declara totais.
    /// </summary>
    public bool? WouldMatchDeclaredTotals =>
        DeclaredTotalDebit is { } declaredDebit && DeclaredTotalCredit is { } declaredCredit
            ? decimal.Round(ProjectedPeriodDebit, 2) == decimal.Round(declaredDebit, 2) &&
              decimal.Round(ProjectedPeriodCredit, 2) == decimal.Round(declaredCredit, 2)
            : null;
}

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
    decimal CompanyPeriodCredit,
    int LinesWithParty = 0,
    int TransactionsWithUnknownParty = 0)
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
