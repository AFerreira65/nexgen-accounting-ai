using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using AccountingAI.Core.Models;

namespace AccountingAI.Core.Efatura;

public sealed class EfaturaCsvImporter
{
    public RecordTable Import(IEnumerable<string> paths)
        => ImportDetailed(paths).Purchases;

    public EfaturaCsvImportResult ImportDetailed(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = new EfaturaCsvImportResult();

        var batchNumber = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            batchNumber++;
            ImportFile(path, batchNumber, result);
        }

        return result;
    }

    private static void ImportFile(string path, int batchNumber, EfaturaCsvImportResult result)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("O ficheiro CSV indicado não existe.", path);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = DetectEncoding(file.FullName);
        var delimiter = DetectDelimiter(file.FullName, encoding);

        using var parser = new TextFieldParser(file.FullName, encoding)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(delimiter);

        var headers = parser.ReadFields();
        if (headers is null || headers.Length == 0)
        {
            throw new InvalidDataException("O CSV não contém cabeçalhos.");
        }

        headers = MakeUnique(headers.Select(NormalizeHeader).ToArray());
        var lineNumber = 1;
        var importedRows = 0;
        while (!parser.EndOfData)
        {
            lineNumber++;
            var fields = parser.ReadFields();
            if (fields is null || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var record = new SaftRecord(file.Name, "Compra e-Fatura");
            record["Lote CSV"] = batchNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            record["Linha no ficheiro"] = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < headers.Length; index++)
            {
                record[headers[index]] = index < fields.Length ? fields[index].Trim() : string.Empty;
            }

            AddCanonicalFields(record, headers);
            result.Purchases.Add(record);
            AddFiscalSummaryLine(result, record, file.Name);
            importedRows++;
        }

        result.Batches.Add(new SaftRecord(file.Name, "Lote CSV")
        {
            ["Lote"] = batchNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Registos"] = importedRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Limite esperado"] = "300",
            ["Estado"] = importedRows switch
            {
                < 300 => "Dentro do limite",
                300 => "No limite — confirmar completude",
                _ => "Acima de 300"
            }
        });

        if (importedRows == 300)
        {
            result.Warnings.Add($"O ficheiro '{file.Name}' contém exatamente 300 registos. A consulta pode ter atingido o limite do Portal; confirme se o período está completo.");
        }
        else if (importedRows > 300)
        {
            result.Warnings.Add($"O ficheiro '{file.Name}' contém {importedRows:N0} registos, acima do lote esperado de 300.");
        }
    }

    private static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252);
        }
    }

    private static string DetectDelimiter(string path, Encoding encoding)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        var firstLine = reader.ReadLine() ?? string.Empty;
        var semicolons = CountOutsideQuotes(firstLine, ';');
        var commas = CountOutsideQuotes(firstLine, ',');
        var tabs = CountOutsideQuotes(firstLine, '\t');

        if (tabs > semicolons && tabs > commas)
        {
            return "\t";
        }

        return semicolons >= commas ? ";" : ",";
    }

    private static int CountOutsideQuotes(string value, char candidate)
    {
        var quoted = false;
        var count = 0;
        foreach (var character in value)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character == candidate)
            {
                count++;
            }
        }

        return count;
    }

    private static string NormalizeHeader(string header)
    {
        var value = header.Trim().Trim('\uFEFF');
        return string.IsNullOrWhiteSpace(value) ? "Coluna" : value;
    }

    private static string[] MakeUnique(string[] headers)
    {
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index];
            used.TryGetValue(header, out var count);
            count++;
            used[header] = count;
            if (count > 1)
            {
                headers[index] = $"{header} ({count})";
            }
        }

        return headers;
    }

    private static void AddCanonicalFields(SaftRecord record, IEnumerable<string> headers)
    {
        var normalized = headers.ToDictionary(NormalizeForMatch, header => header, StringComparer.OrdinalIgnoreCase);
        var invoiceAndAtcud = Find(
            "nº fatura / atcud",
            "no fatura / atcud",
            "numero da fatura / atcud",
            "numero da fatura",
            "numero fatura",
            "documento",
            "numero do documento");
        var (invoiceNo, atcud) = SplitInvoiceAndAtcud(invoiceAndAtcud);
        record["InvoiceNo"] = invoiceNo;
        record["ATCUD"] = atcud;
        record["InvoiceDate"] = Find("data de emissao", "data emissao", "data", "data documento");
        record["InvoiceType"] = Find("tipo", "tipo de documento");
        var supplier = Find("emitente", "fornecedor", "nome do emitente", "nome do fornecedor");
        var taxId = Find("nif", "nif emitente", "nif fornecedor");
        if (taxId.Length == 0)
        {
            taxId = ExtractTaxId(supplier);
        }

        record["TaxRegistrationNumber"] = taxId;
        record["SupplierName"] = RemoveLeadingTaxId(supplier, taxId);
        record["TaxBase"] = Find("base tributavel", "valor tributavel", "total liquido", "liquido");
        record["TaxPayable"] = Find("iva", "valor iva", "imposto", "valor do iva");
        record["GrossTotal"] = Find("valor total", "total", "total documento");
        record["DocumentKey"] = string.Join("|",
            record["TaxRegistrationNumber"],
            record["InvoiceNo"],
            record["InvoiceDate"],
            record["InvoiceType"]);
        return;

        string Find(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (normalized.TryGetValue(candidate, out var header))
                {
                    return record[header];
                }
            }

            return string.Empty;
        }
    }

    private static void AddFiscalSummaryLine(EfaturaCsvImportResult result, SaftRecord document, string sourceFile)
    {
        if (document["TaxBase"].Length == 0 &&
            document["TaxPayable"].Length == 0 &&
            document["GrossTotal"].Length == 0)
        {
            return;
        }

        result.Lines.Add(new SaftRecord(sourceFile, "Linha fiscal da compra e-Fatura")
        {
            ["DocumentKey"] = document["DocumentKey"],
            ["InvoiceNo"] = document["InvoiceNo"],
            ["TaxRegistrationNumber"] = document["TaxRegistrationNumber"],
            ["InvoiceDate"] = document["InvoiceDate"],
            ["LineNumber"] = "1",
            ["Description"] = "Resumo fiscal disponibilizado pelo Portal e-Fatura",
            ["TaxBase"] = document["TaxBase"],
            ["TaxAmount"] = document["TaxPayable"],
            ["GrossTotal"] = document["GrossTotal"],
            ["TaxPercentage"] = string.Empty,
            ["TaxRateStatus"] = "Taxa não discriminada pelo CSV do Portal",
            ["FiscalDataSource"] = "CSV do Portal e-Fatura"
        });
    }

    private static (string InvoiceNo, string Atcud) SplitInvoiceAndAtcud(string value)
    {
        var separator = value.LastIndexOf(" / ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return (value.Trim(), string.Empty);
        }

        var possibleAtcud = value[(separator + 3)..].Trim();
        return Regex.IsMatch(possibleAtcud, "^[A-Z0-9]+-[A-Z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? (value[..separator].Trim(), possibleAtcud)
            : (value.Trim(), string.Empty);
    }

    private static string ExtractTaxId(string supplier)
    {
        var match = Regex.Match(supplier, "(?<!\\d)\\d{9}(?!\\d)", RegexOptions.CultureInvariant);
        return match.Success ? match.Value : string.Empty;
    }

    private static string RemoveLeadingTaxId(string supplier, string taxId)
    {
        if (supplier.Length == 0 || taxId.Length == 0)
        {
            return supplier;
        }

        var index = supplier.IndexOf(taxId, StringComparison.Ordinal);
        if (index < 0)
        {
            return supplier;
        }

        return supplier.Remove(index, taxId.Length).Trim(' ', '-', '–', '—');
    }

    private static string NormalizeForMatch(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
