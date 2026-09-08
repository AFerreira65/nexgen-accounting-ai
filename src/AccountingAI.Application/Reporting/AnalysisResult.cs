namespace AccountingAI.Application.Reporting;

/// <summary>Uma coluna resolvida do resultado (chave, rótulo pt-PT, tipo).</summary>
public sealed record AnalysisColumn(string Key, string Label, string Type);

/// <summary>O valor de um campo de agrupamento num nível de grupo.</summary>
public sealed record AnalysisGroupKey(string Field, string Label, object? Value);

/// <summary>
/// Um nível de grupo do resultado: os valores das chaves (prefixo dos campos de
/// agrupamento), os subtotais dos agregados e o número de linhas abaixo.
/// </summary>
public sealed record AnalysisGroup(
    IReadOnlyList<AnalysisGroupKey> Keys,
    IReadOnlyDictionary<string, decimal> Subtotals,
    int RowCount);

/// <summary>
/// O resultado de uma análise: as colunas, as linhas (cada uma um dicionário
/// campo→valor), os grupos com subtotais em todos os níveis, o total geral dos
/// agregados, a contagem de linhas e se foi truncada no limite.
/// </summary>
public sealed record AnalysisResult(
    IReadOnlyList<AnalysisColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<AnalysisGroup> Groups,
    IReadOnlyDictionary<string, decimal> GrandTotal,
    int RowCount,
    bool Truncated)
{
    /// <summary>Limite rígido de linhas (ADR-0005): uma análise é um mapa, não uma extração.</summary>
    public const int RowLimit = 5000;
}
