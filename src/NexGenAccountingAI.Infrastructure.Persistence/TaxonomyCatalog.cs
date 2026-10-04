using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record TaxonomyCode(int Code, string Description, IReadOnlyList<string> SncAccounts);

/// <summary>
/// Catálogo de taxonomias SAF-T por referencial, transcrito da Portaria n.º 302/2016
/// (Anexo II — Taxonomia S; Anexo III — Taxonomia M). O referencial <c>N</c> usa os
/// códigos da Taxonomia S; o referencial <c>O</c> usa sempre o código 1.
///
/// A transcrição foi feita automaticamente a partir do Diário da República e deve ser
/// confirmada contra o texto oficial antes de uso em produção.
/// </summary>
public static class TaxonomyCatalog
{
    private static readonly CatalogData Data = Load();

    public static string Source => Data.Source;

    public static string Disclaimer => Data.Disclaimer;

    private static readonly IReadOnlyList<TaxonomyCode> OtherReference =
        [new(1, "Outros referenciais", ["1"])];

    public static IReadOnlyList<TaxonomyCode> For(string? reference) =>
        (reference?.Trim().ToUpperInvariant()) switch
        {
            "S" or "N" => Data.S,
            "M" => Data.M,
            "O" => OtherReference,
            _ => []
        };

    /// <summary>
    /// Código de taxonomia inequívoco para uma conta de movimento: só existe quando
    /// exatamente uma entrada do referencial indica essa conta SNC. Devoluções nulas
    /// deixam a conta por classificar, para escolha manual a partir do catálogo.
    /// </summary>
    public static int? SuggestFor(string? reference, string accountCode)
    {
        var normalized = accountCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (string.Equals(reference?.Trim(), "O", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var entries = For(reference);
        var exact = entries
            .Where(entry => entry.SncAccounts.Contains(normalized, StringComparer.Ordinal))
            .ToList();
        if (exact.Count == 1)
        {
            return exact[0].Code;
        }

        if (exact.Count > 1)
        {
            return null;
        }

        // Sem correspondência exata: uma subconta de movimento herda a taxonomia da
        // conta-razão (código de 2 dígitos) de que é prefixo, se for inequívoca. Só
        // se aplica a razões que a taxonomia não subdivide (ex. 11 → 111, 12 → 121).
        var byPrefix = entries
            .Select(entry => (entry.Code, Prefix: entry.SncAccounts
                .Where(snc => snc.Length == 2 && normalized.Length > 2 && normalized.StartsWith(snc, StringComparison.Ordinal))
                .FirstOrDefault()))
            .Where(match => match.Prefix is not null)
            .ToList();
        if (byPrefix.Count == 0)
        {
            return null;
        }

        var longest = byPrefix.Max(match => match.Prefix!.Length);
        var best = byPrefix.Where(match => match.Prefix!.Length == longest).ToList();
        return best.Count == 1 ? best[0].Code : null;
    }

    private static CatalogData Load()
    {
        using var stream = typeof(TaxonomyCatalog).Assembly
            .GetManifestResourceStream("NexGenAccountingAI.Infrastructure.Persistence.Resources.taxonomy-catalog.json")
            ?? throw new InvalidOperationException("O recurso taxonomy-catalog.json não foi encontrado.");

        var file = JsonSerializer.Deserialize<CatalogFile>(stream, Options)
            ?? throw new InvalidOperationException("O catálogo de taxonomias está vazio.");

        static IReadOnlyList<TaxonomyCode> Map(IEnumerable<CatalogEntry> entries) => entries
            .Select(entry => new TaxonomyCode(entry.Code, entry.Description, entry.Snc))
            .ToList();

        return new CatalogData(file.Source, file.Disclaimer, Map(file.S), Map(file.M));
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record CatalogData(
        string Source,
        string Disclaimer,
        IReadOnlyList<TaxonomyCode> S,
        IReadOnlyList<TaxonomyCode> M);

    private sealed class CatalogFile
    {
        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("disclaimer")]
        public string Disclaimer { get; init; } = string.Empty;

        [JsonPropertyName("S")]
        public List<CatalogEntry> S { get; init; } = [];

        [JsonPropertyName("M")]
        public List<CatalogEntry> M { get; init; } = [];
    }

    private sealed class CatalogEntry
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("snc")]
        public List<string> Snc { get; init; } = [];

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;
    }
}
