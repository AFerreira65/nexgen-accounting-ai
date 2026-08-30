using System.Data;

namespace AccountingAI.Core.Models;

public sealed class RecordTable(string key, string title)
{
    private readonly List<string> _columns = ["Ficheiro"];
    private readonly HashSet<string> _columnSet = new(StringComparer.OrdinalIgnoreCase) { "Ficheiro" };

    public string Key { get; } = key;

    public string Title { get; } = title;

    public IReadOnlyList<string> Columns => _columns;

    public List<SaftRecord> Rows { get; } = [];

    public void Add(SaftRecord record)
    {
        foreach (var column in record.Values.Keys)
        {
            if (_columnSet.Add(column))
            {
                _columns.Add(column);
            }
        }

        Rows.Add(record);
    }

    public void AddRange(IEnumerable<SaftRecord> records)
    {
        foreach (var record in records)
        {
            Add(record);
        }
    }

    public DataTable ToDataTable()
    {
        var table = new DataTable(Title) { Locale = System.Globalization.CultureInfo.GetCultureInfo("pt-PT") };
        foreach (var column in _columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var record in Rows)
        {
            var row = table.NewRow();
            foreach (var pair in record.Values)
            {
                row[pair.Key] = pair.Value;
            }

            table.Rows.Add(row);
        }

        return table;
    }
}
