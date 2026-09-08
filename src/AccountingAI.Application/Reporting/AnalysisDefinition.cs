namespace AccountingAI.Application.Reporting;

/// <summary>A origem de dados de uma análise. Começa nos movimentos; cresce origem a origem.</summary>
public enum AnalysisSource
{
    Movimentos = 0,
    SaldosDeConta = 1,
}

/// <summary>Tipo de um campo do catálogo — decide os operadores de filtro e a formatação.</summary>
public enum AnalysisFieldType
{
    Text = 0,
    Number = 1,
    Money = 2,
    Date = 3,
}

/// <summary>Função de agregação de um agregado.</summary>
public enum AnalysisAggregateFunction
{
    Sum = 0,
    Count = 1,
    Average = 2,
    Min = 3,
    Max = 4,
}

/// <summary>Um campo disponível numa origem: o que a PWA mostra no construtor.</summary>
public sealed record AnalysisField(
    string Key,
    string Label,
    AnalysisFieldType Type,
    bool Groupable,
    bool Aggregatable);

/// <summary>Um filtro: <c>campo operador valor</c> (o valor é sempre texto; o runner converte pelo tipo).</summary>
public sealed record AnalysisFilter(string Field, string Operator, string Value);

/// <summary>Uma ordenação por um campo.</summary>
public sealed record AnalysisSort(string Field, bool Descending);

/// <summary>Um agregado: uma função sobre um campo agregável.</summary>
public sealed record AnalysisAggregate(string Field, AnalysisAggregateFunction Function);

/// <summary>
/// A definição de uma análise: a origem, as colunas a mostrar (por ordem), os
/// filtros, os campos de agrupamento (por ordem, do mais externo ao mais
/// interno), a ordenação, os agregados a totalizar e se inclui total geral.
/// </summary>
public sealed record AnalysisDefinition(
    AnalysisSource Source,
    IReadOnlyList<string> Columns,
    IReadOnlyList<AnalysisFilter> Filters,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<AnalysisSort> Sort,
    IReadOnlyList<AnalysisAggregate> Aggregates,
    bool IncludeGrandTotal)
{
    public static AnalysisDefinition Empty(AnalysisSource source) =>
        new(source, [], [], [], [], [], IncludeGrandTotal: true);
}
