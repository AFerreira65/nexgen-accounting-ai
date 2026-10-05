namespace NexGenAccountingAI.Application.Reporting;

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
/// agregados, a contagem de <b>todas</b> as linhas e se o detalhe foi cortado no
/// limite (os subtotais e o total geral são sempre do conjunto completo).
/// </summary>
public sealed record AnalysisResult(
    IReadOnlyList<AnalysisColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<AnalysisGroup> Groups,
    IReadOnlyDictionary<string, decimal> GrandTotal,
    int RowCount,
    bool Truncated)
{
    /// <summary>Limite de linhas de detalhe devolvidas (ADR-0005): uma análise é um mapa, não uma extração.</summary>
    public const int RowLimit = 5000;

    /// <summary>Teto de linhas processadas em memória; acima disto a análise é recusada com explicação.</summary>
    public const int ProcessingLimit = 300_000;
}
