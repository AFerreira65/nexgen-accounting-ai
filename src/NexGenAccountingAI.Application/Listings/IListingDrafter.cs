namespace NexGenAccountingAI.Application.Listings;

/// <summary>
/// O rascunho de uma listagem que a IA montou a partir de um pedido em português (F4D-05).
/// Nunca se guarda sozinho: o utilizador vê-o, corrige-o e guarda se quiser.
/// </summary>
/// <param name="Definition"><c>null</c> quando a IA não conseguiu montar uma definição válida.</param>
/// <param name="Problems">Os problemas da definição, se a IA não chegou a acertar.</param>
public sealed record ListingDraft(
    string? Name,
    string Explanation,
    ListingDefinition? Definition,
    IReadOnlyList<string> Problems,
    string Model,
    int PromptTokens,
    int OutputTokens);

/// <summary>
/// Monta uma listagem a partir de um pedido em português. A fronteira que conhece o SDK vive em
/// <c>NexGenAccountingAI.Infrastructure.Ai</c> (ADR-0006). Só recebe o pedido e o catálogo de campos:
/// nenhum dado da empresa vai para o modelo.
/// </summary>
public interface IListingDrafter
{
    /// <summary><c>true</c> quando há uma chave de API configurada.</summary>
    bool IsConfigured { get; }

    string Model { get; }

    Task<ListingDraft> DraftAsync(string request, DateOnly today, CancellationToken cancellationToken = default);
}
