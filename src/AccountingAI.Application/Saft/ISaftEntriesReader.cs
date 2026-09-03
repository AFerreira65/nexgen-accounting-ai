namespace AccountingAI.Application.Saft;

/// <summary>
/// Fronteira para o resumo do bloco <c>GeneralLedgerEntries</c> de um SAF-T (PT):
/// contagem de transações, totais declarados, intervalo de datas e diários. A
/// implementação lê em streaming, sem materializar as linhas.
/// </summary>
public interface ISaftEntriesReader
{
    Task<SaftEntriesSummary> ReadSummaryAsync(Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lê as transações do bloco <c>GeneralLedgerEntries</c> com as linhas, uma
    /// transação de cada vez (subárvore por subárvore). Um ficheiro sem o bloco
    /// devolve uma lista vazia.
    /// </summary>
    Task<IReadOnlyList<SaftTransaction>> ReadTransactionsAsync(Stream content, CancellationToken cancellationToken = default);
}
