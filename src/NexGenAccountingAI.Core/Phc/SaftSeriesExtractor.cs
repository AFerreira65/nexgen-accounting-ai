using System.Text.RegularExpressions;
using NexGenAccountingAI.Core.Models;

namespace NexGenAccountingAI.Core.Phc;

public sealed record SaftDocumentSeries(string DocumentType, string Series, int DocumentCount)
{
    public string Key => $"{DocumentType}\u001f{Series}";

    public string DisplayName =>
        $"{DocumentType} — {Series} ({DocumentCount:N0} {(DocumentCount == 1 ? "documento" : "documentos")})";

    public override string ToString() => DisplayName;
}

public static partial class SaftSeriesExtractor
{
    public static IReadOnlyList<SaftDocumentSeries> ExtractInvoices(SaftDataSet data, string taxRegistrationNumber) =>
        Extract(data, taxRegistrationNumber, SaftTableKeys.Invoices, "InvoiceNo", "InvoiceType");

    public static IReadOnlyList<SaftDocumentSeries> ExtractReceipts(SaftDataSet data, string taxRegistrationNumber) =>
        Extract(data, taxRegistrationNumber, SaftTableKeys.Payments, "PaymentRefNo", "PaymentType");

    public static string ExtractSeries(string documentNumber, string documentType)
    {
        var value = documentNumber.Trim();
        var separator = value.LastIndexOf('/');
        var prefix = separator > 0
            ? value[..separator].Trim()
            : TrailingNumberRegex().Match(value) is { Success: true } match
                ? match.Groups["prefix"].Value.Trim()
                : value;
        if (documentType.Length > 0 && prefix.StartsWith(documentType + " ", StringComparison.OrdinalIgnoreCase))
        {
            prefix = prefix[(documentType.Length + 1)..].Trim();
        }

        return prefix.Length == 0 ? "Sem série" : prefix;
    }

    private static IReadOnlyList<SaftDocumentSeries> Extract(
        SaftDataSet data,
        string taxRegistrationNumber,
        string tableKey,
        string numberField,
        string typeField)
    {
        ArgumentNullException.ThrowIfNull(data);
        var sourceFiles = data.SourceFiles
            .Where(source => source.CompanyId.Equals(taxRegistrationNumber, StringComparison.Ordinal))
            .Select(source => source.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (data.Tables.TryGetValue(SaftTableKeys.Header, out var headers))
        {
            sourceFiles.UnionWith(headers.Rows
                .Where(record => record["TaxRegistrationNumber"].Equals(taxRegistrationNumber, StringComparison.Ordinal))
                .Select(record => record.SourceFile));
        }

        if (sourceFiles.Count == 0 || !data.Tables.TryGetValue(tableKey, out var table))
        {
            return [];
        }

        return table.Rows
            .Where(record => sourceFiles.Contains(record.SourceFile))
            .Select(record => new
            {
                DocumentType = record[typeField].Trim(),
                Series = ExtractSeries(record[numberField], record[typeField].Trim())
            })
            .Where(item => item.DocumentType.Length > 0 && item.Series.Length > 0)
            .GroupBy(item => $"{item.DocumentType}\u001f{item.Series}", StringComparer.OrdinalIgnoreCase)
            .Select(group => new SaftDocumentSeries(
                group.First().DocumentType,
                group.First().Series,
                group.Count()))
            .OrderBy(item => item.DocumentType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Series, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    [GeneratedRegex(@"^(?<prefix>.+?)[\s-]+\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingNumberRegex();
}
