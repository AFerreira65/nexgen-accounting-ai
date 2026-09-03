namespace AccountingAI.Application.Saft;

/// <summary>
/// Fronteira para leitura do plano geral de contas (<c>MasterFiles/GeneralLedgerAccounts</c>)
/// de um ficheiro SAF-T (PT). A implementação lê apenas esse bloco e para antes dos
/// movimentos (<c>GeneralLedgerEntries</c>). Um ficheiro sem o bloco devolve lista vazia.
/// </summary>
public interface ISaftAccountsReader
{
    Task<IReadOnlyList<SaftAccount>> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}
