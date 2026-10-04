namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Padrões para <c>EF.Functions.ILike</c>. O PostgreSQL compara texto com distinção de
/// maiúsculas, ao contrário da collation CI do SQL Server, por isso as pesquisas livres
/// usam ILIKE com os metacaracteres do termo escapados.
/// </summary>
public static class TextSearch
{
    public static string ContainsPattern(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        var escaped = term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
