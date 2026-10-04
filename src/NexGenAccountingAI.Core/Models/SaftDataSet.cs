namespace NexGenAccountingAI.Core.Models;

public sealed class SaftDataSet
{
    private readonly Dictionary<string, RecordTable> _tables = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, RecordTable> Tables => _tables;

    public List<SaftSourceFile> SourceFiles { get; } = [];

    public List<ValidationIssue> ValidationIssues { get; } = [];

    public RecordTable GetOrCreateTable(string key, string title)
    {
        if (!_tables.TryGetValue(key, out var table))
        {
            table = new RecordTable(key, title);
            _tables.Add(key, table);
        }

        return table;
    }

    public int Count(string key) => _tables.TryGetValue(key, out var table) ? table.Rows.Count : 0;

    public void MergeTable(RecordTable source)
    {
        ArgumentNullException.ThrowIfNull(source);
        GetOrCreateTable(source.Key, source.Title).AddRange(source.Rows);
    }
}
