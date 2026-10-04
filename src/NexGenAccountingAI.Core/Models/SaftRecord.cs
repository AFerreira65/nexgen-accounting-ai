namespace NexGenAccountingAI.Core.Models;

public sealed class SaftRecord
{
    public SaftRecord(string sourceFile, string recordType)
    {
        SourceFile = sourceFile;
        RecordType = recordType;
        Values["Ficheiro"] = sourceFile;
    }

    public string SourceFile { get; }

    public string RecordType { get; }

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string this[string field]
    {
        get => Values.TryGetValue(field, out var value) ? value : string.Empty;
        set => Values[field] = value;
    }
}
