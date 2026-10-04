namespace NexGenAccountingAI.Application.DataDictionary;

/// <summary>Uma coluna de uma tabela da base de dados, no dicionário de dados.</summary>
public sealed record DataDictionaryColumn(
    string Name,
    string ClrType,
    string SqlType,
    bool Nullable,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool PrimaryKey,
    bool ForeignKey,
    string? References,
    bool Unique,
    bool Indexed,
    string Description);

/// <summary>Uma tabela com a estrutura completa das colunas.</summary>
public sealed record DataDictionaryTable(
    string Schema,
    string Name,
    string Purpose,
    IReadOnlyList<DataDictionaryColumn> Columns)
{
    public int ColumnCount => Columns.Count;
}

/// <summary>Uma tabela na lista do dicionário, sem o detalhe das colunas.</summary>
public sealed record DataDictionaryTableSummary(
    string Schema,
    string Name,
    string Purpose,
    int ColumnCount);
