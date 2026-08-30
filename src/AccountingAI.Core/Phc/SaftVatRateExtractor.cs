using System.Globalization;
using AccountingAI.Core.Models;

namespace AccountingAI.Core.Phc;

public sealed record SaftVatRate(decimal Rate)
{
    public string Key => Rate.ToString("0.00", CultureInfo.InvariantCulture);

    public string DisplayName => $"{Rate.ToString("0.00", CultureInfo.GetCultureInfo("pt-PT"))} %";

    public override string ToString() => DisplayName;
}

public static class SaftVatRateExtractor
{
    public static IReadOnlyList<SaftVatRate> Extract(SaftDataSet data, string taxRegistrationNumber)
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

        if (sourceFiles.Count == 0)
        {
            return [];
        }

        var rates = new HashSet<decimal>();
        AddRates(data, SaftTableKeys.Taxes, sourceFiles, rates);
        AddRates(data, SaftTableKeys.InvoiceLines, sourceFiles, rates);
        return rates
            .OrderBy(rate => rate)
            .Select(rate => new SaftVatRate(rate))
            .ToList();
    }

    private static void AddRates(
        SaftDataSet data,
        string tableKey,
        IReadOnlySet<string> sourceFiles,
        ISet<decimal> rates)
    {
        if (!data.Tables.TryGetValue(tableKey, out var table))
        {
            return;
        }

        foreach (var record in table.Rows.Where(record => sourceFiles.Contains(record.SourceFile)))
        {
            var taxType = record["TaxType"].Trim();
            if (taxType.Length > 0 && !taxType.Equals("IVA", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseRate(record["TaxPercentage"], out var rate))
            {
                rates.Add(rate);
            }
        }
    }

    internal static bool TryParseRate(string value, out decimal rate)
    {
        var normalized = value.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out rate) &&
               rate is >= 0m and <= 100m;
    }
}
