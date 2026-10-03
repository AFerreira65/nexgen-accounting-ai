namespace AccountingAI.Application.Saft;

/// <summary>Se a entidade vem do bloco <c>Customer</c> ou <c>Supplier</c> do SAF-T.</summary>
public enum SaftPartyKind
{
    Customer = 0,
    Supplier = 1,
}

/// <summary>
/// Um cliente ou fornecedor de <c>MasterFiles</c>, tal como está no ficheiro —
/// sem validação. <paramref name="AccountId"/> é a conta do plano a que o
/// ficheiro o liga (só nos ficheiros de contabilidade); <paramref name="Country"/>
/// é o país da morada de faturação.
/// </summary>
public sealed record SaftParty(
    SaftPartyKind Kind,
    string SourceId,
    string? AccountId,
    string TaxId,
    string Name,
    string? Country);

/// <summary>
/// Fronteira para leitura dos clientes e fornecedores (<c>MasterFiles/Customer</c>
/// e <c>MasterFiles/Supplier</c>) de um ficheiro SAF-T (PT). A implementação para
/// antes dos movimentos e dos documentos; um ficheiro sem esses blocos devolve
/// lista vazia.
/// </summary>
public interface ISaftPartiesReader
{
    Task<IReadOnlyList<SaftParty>> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}
