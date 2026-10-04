using System.Xml.Linq;
using NexGenAccountingAI.Core.Models;

namespace NexGenAccountingAI.Core.Parsing;

internal static class XmlRecordFlattener
{
    public static SaftRecord Flatten(
        XElement element,
        string sourceFile,
        string recordType,
        params string[] excludedSubtrees)
    {
        var record = new SaftRecord(sourceFile, recordType);
        var excluded = new HashSet<string>(excludedSubtrees, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        Visit(element, element, excluded, values);

        foreach (var pair in values)
        {
            record[pair.Key] = string.Join(" | ", pair.Value.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return record;
    }

    private static void Visit(
        XElement root,
        XElement current,
        HashSet<string> excluded,
        Dictionary<string, List<string>> values)
    {
        foreach (var child in current.Elements())
        {
            if (excluded.Contains(child.Name.LocalName))
            {
                continue;
            }

            if (child.HasElements)
            {
                Visit(root, child, excluded, values);
                continue;
            }

            var path = string.Join(
                "/",
                child.AncestorsAndSelf()
                    .TakeWhile(ancestor => ancestor != root)
                    .Reverse()
                    .Select(ancestor => ancestor.Name.LocalName));

            if (string.IsNullOrEmpty(path))
            {
                path = child.Name.LocalName;
            }

            if (!values.TryGetValue(path, out var list))
            {
                list = [];
                values.Add(path, list);
            }

            list.Add(child.Value.Trim());
        }
    }
}
