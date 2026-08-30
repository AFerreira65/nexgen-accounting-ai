using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AccountingAI.Core.Models;

namespace AccountingAI.Core.Phc;

public static class PhcAggregatedInvoiceBuilder
{
    public static IReadOnlyList<PhcAggregatedInvoiceDraft> BuildDocuments(
        SaftDataSet data,
        string taxRegistrationNumber)
    {
        var combined = Build(data, taxRegistrationNumber);
        var exemptionGroups = combined.Lines
            .Where(line => line.TaxExemptionCode.Length > 0 || line.TaxExemptionReason.Length > 0)
            .GroupBy(line => (line.TaxExemptionCode, line.TaxExemptionReason))
            .ToList();
        if (exemptionGroups.Count <= 1)
        {
            return [combined];
        }

        var documents = new List<PhcAggregatedInvoiceDraft>();
        var taxableLines = combined.Lines
            .Where(line => line.TaxExemptionCode.Length == 0 && line.TaxExemptionReason.Length == 0)
            .ToList();
        if (taxableLines.Count > 0)
        {
            documents.Add(CreateSplitDraft(combined, taxableLines, "IVA"));
        }

        var fallbackIndex = 0;
        var usedSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IVA" };
        foreach (var group in exemptionGroups.OrderBy(group => group.Key.TaxExemptionCode, StringComparer.OrdinalIgnoreCase))
        {
            fallbackIndex++;
            var suffix = group.Key.TaxExemptionCode.Length > 0
                ? group.Key.TaxExemptionCode
                : $"E{fallbackIndex:00}";
            if (!usedSuffixes.Add(suffix))
            {
                suffix = $"E{fallbackIndex:00}";
                usedSuffixes.Add(suffix);
            }
            documents.Add(CreateSplitDraft(combined, group.ToList(), suffix));
        }

        return documents;
    }

    public static PhcAggregatedInvoiceDraft Build(SaftDataSet data, string taxRegistrationNumber)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(taxRegistrationNumber))
        {
            throw new ArgumentException("Indique o contribuinte da empresa do SAF-T.", nameof(taxRegistrationNumber));
        }

        var sourceFiles = MatchingSourceFiles(data, taxRegistrationNumber);
        if (sourceFiles.Count == 0)
        {
            throw new InvalidDataException($"Não existem ficheiros SAF-T carregados para o contribuinte {taxRegistrationNumber}.");
        }
        var currencyCode = ResolveCurrencyCode(data, sourceFiles);

        if (!data.Tables.TryGetValue(SaftTableKeys.Invoices, out var invoices) ||
            !data.Tables.TryGetValue(SaftTableKeys.InvoiceLines, out var invoiceLines))
        {
            throw new InvalidDataException("O SAF-T carregado não contém faturação e respetivas linhas.");
        }

        var documents = invoices.Rows
            .Where(record => sourceFiles.Contains(record.SourceFile))
            .Where(record => record["InvoiceNo"].Length > 0)
            .ToList();
        var cancelled = documents.Count(record =>
            Value(record, "DocumentStatus/InvoiceStatus", "InvoiceStatus")
                .Equals("A", StringComparison.OrdinalIgnoreCase));
        var activeDocuments = documents
            .Where(record => !Value(record, "DocumentStatus/InvoiceStatus", "InvoiceStatus")
                .Equals("A", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (activeDocuments.Count == 0)
        {
            throw new InvalidDataException("O SAF-T não contém documentos de faturação ativos para agregar.");
        }

        var linesByDocument = invoiceLines.Rows
            .Where(record => sourceFiles.Contains(record.SourceFile))
            .GroupBy(record => DocumentKey(record.SourceFile, record["InvoiceNo"]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<VatTotalKey, MutableVatTotal>();
        foreach (var document in activeDocuments)
        {
            var key = DocumentKey(document.SourceFile, document["InvoiceNo"]);
            if (!linesByDocument.TryGetValue(key, out var lines) || lines.Count == 0)
            {
                throw new InvalidDataException($"O documento {document["InvoiceNo"]} não tem linhas disponíveis para agregação.");
            }

            var documentGroups = lines
                .Select(line => new
                {
                    Base = ParseDecimal(line["CreditAmount"]) - ParseDecimal(line["DebitAmount"]),
                    Rate = ParseRate(Value(line, "Tax/TaxPercentage", "TaxPercentage"), document["InvoiceNo"]),
                    ExemptionCode = Value(line, "Tax/TaxExemptionCode", "TaxExemptionCode").Trim(),
                    ExemptionReason = Value(line, "Tax/TaxExemptionReason", "TaxExemptionReason").Trim()
                })
                .GroupBy(item => new VatTotalKey(item.Rate, item.ExemptionCode, item.ExemptionReason))
                .Select(group => new MutableVatTotal(group.Key, group.Sum(item => item.Base), 0m))
                .ToList();
            var signedBase = documentGroups.Sum(group => group.Net);
            var sign = signedBase < 0m || document["InvoiceType"].Equals("NC", StringComparison.OrdinalIgnoreCase)
                ? -1m
                : 1m;
            foreach (var group in documentGroups)
            {
                group.Vat = decimal.Round(group.Net * group.Rate / 100m, 2, MidpointRounding.AwayFromZero);
            }

            var expectedVat = sign * Math.Abs(ParseDecimal(Value(
                document,
                "DocumentTotals/TaxPayable",
                "TaxPayable")));
            var roundingDifference = expectedVat - documentGroups.Sum(group => group.Vat);
            var adjustmentTarget = documentGroups
                .Where(group => group.Rate > 0m)
                .OrderByDescending(group => Math.Abs(group.Net))
                .FirstOrDefault();
            if (adjustmentTarget is not null)
            {
                adjustmentTarget.Vat += roundingDifference;
            }
            else if (Math.Abs(roundingDifference) > 0.01m)
            {
                throw new InvalidDataException(
                    $"O IVA do documento {document["InvoiceNo"]} não pode ser reconciliado com as taxas das linhas.");
            }

            foreach (var group in documentGroups)
            {
                if (!totals.TryGetValue(group.Key, out var total))
                {
                    total = new MutableVatTotal(group.Key, 0m, 0m);
                    totals.Add(group.Key, total);
                }

                total.Net += group.Net;
                total.Vat += group.Vat;
            }
        }

        var resultLines = totals.Values
            .Where(total => total.Net != 0m || total.Vat != 0m)
            .OrderBy(total => total.Rate)
            .ThenBy(total => total.ExemptionCode, StringComparer.OrdinalIgnoreCase)
            .Select(total => new PhcAggregatedVatLine(
                total.Rate,
                decimal.Round(total.Net, 2, MidpointRounding.AwayFromZero),
                decimal.Round(total.Vat, 2, MidpointRounding.AwayFromZero),
                total.ExemptionCode,
                total.ExemptionReason))
            .ToList();
        if (resultLines.Count == 0)
        {
            throw new InvalidDataException("A agregação do SAF-T não produziu qualquer valor para lançar.");
        }

        var dates = activeDocuments
            .Select(document => DateOnly.TryParseExact(
                document["InvoiceDate"],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed) ? parsed : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToList();
        if (dates.Count == 0)
        {
            throw new InvalidDataException("Não foi possível determinar a data dos documentos do SAF-T.");
        }

        var documentDate = dates.Max();
        var fingerprintSource = string.Join("\n", activeDocuments
            .OrderBy(record => record.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record["InvoiceNo"], StringComparer.OrdinalIgnoreCase)
            .Select(record =>
                $"{record.SourceFile}|{record["InvoiceNo"]}|{record["InvoiceDate"]}|" +
                Value(record, "DocumentTotals/GrossTotal", "GrossTotal")));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{taxRegistrationNumber}|{documentDate:yyyy-MM-dd}|{fingerprintSource}")));
        return new PhcAggregatedInvoiceDraft(
            taxRegistrationNumber,
            documentDate,
            activeDocuments.Count,
            cancelled,
            resultLines,
            $"SAFTIA-{hash[..12]}",
            currencyCode);
    }

    private static HashSet<string> MatchingSourceFiles(SaftDataSet data, string taxRegistrationNumber)
    {
        var files = data.SourceFiles
            .Where(source => source.CompanyId.Equals(taxRegistrationNumber, StringComparison.Ordinal))
            .Select(source => source.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (data.Tables.TryGetValue(SaftTableKeys.Header, out var headers))
        {
            files.UnionWith(headers.Rows
                .Where(record => record["TaxRegistrationNumber"].Equals(taxRegistrationNumber, StringComparison.Ordinal))
                .Select(record => record.SourceFile));
        }

        return files;
    }

    private static string ResolveCurrencyCode(SaftDataSet data, IReadOnlySet<string> sourceFiles)
    {
        if (!data.Tables.TryGetValue(SaftTableKeys.Header, out var headers))
        {
            return string.Empty;
        }

        var codes = headers.Rows
            .Where(record => sourceFiles.Contains(record.SourceFile))
            .Select(record => record["CurrencyCode"].Trim().ToUpperInvariant())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count > 1)
        {
            throw new InvalidDataException(
                $"Os ficheiros SAF-T selecionados usam moedas diferentes: {string.Join(", ", codes)}.");
        }

        return codes.FirstOrDefault() ?? string.Empty;
    }

    private static PhcAggregatedInvoiceDraft CreateSplitDraft(
        PhcAggregatedInvoiceDraft source,
        IReadOnlyList<PhcAggregatedVatLine> lines,
        string suffix)
    {
        var safeSuffix = new string(suffix
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (safeSuffix.Length == 0)
        {
            safeSuffix = "DIV";
        }

        var hashPart = source.ImportReference.StartsWith("SAFTIA-", StringComparison.Ordinal)
            ? source.ImportReference[7..]
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ImportReference)));
        var importReference = $"SAFTIA-{hashPart[..Math.Min(8, hashPart.Length)]}-{safeSuffix}";
        return new PhcAggregatedInvoiceDraft(
            source.TaxRegistrationNumber,
            source.DocumentDate,
            source.DocumentCount,
            source.CancelledDocumentCount,
            lines,
            importReference,
            source.CurrencyCode);
    }

    private static string DocumentKey(string sourceFile, string invoiceNo) => $"{sourceFile}\u001f{invoiceNo}";

    private static string Value(SaftRecord record, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(record[field]))
            {
                return record[field];
            }
        }

        return string.Empty;
    }

    private static decimal ParseRate(string value, string invoiceNo)
    {
        if (SaftVatRateExtractor.TryParseRate(value, out var rate))
        {
            return rate;
        }

        throw new InvalidDataException($"O documento {invoiceNo} contém uma taxa de IVA inválida: '{value}'.");
    }

    private static decimal ParseDecimal(string value)
    {
        var normalized = value.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private sealed record VatTotalKey(decimal Rate, string ExemptionCode, string ExemptionReason);

    private sealed class MutableVatTotal(VatTotalKey key, decimal net, decimal vat)
    {
        public VatTotalKey Key { get; } = key;
        public decimal Rate => Key.Rate;
        public string ExemptionCode => Key.ExemptionCode;
        public string ExemptionReason => Key.ExemptionReason;
        public decimal Net { get; set; } = net;
        public decimal Vat { get; set; } = vat;
    }
}
