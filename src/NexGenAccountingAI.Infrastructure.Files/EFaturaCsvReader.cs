using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NexGenAccountingAI.Application.Efatura;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Lê um CSV de compras do e-Fatura (adquirente). Deteta a codificação (BOM,
/// UTF-8 ou Windows-1252) e o separador (<c>;</c>, <c>,</c> ou tabulação),
/// canoniza os nomes das colunas (o Portal varia-os) e devolve as linhas com
/// os campos normalizados. Porta a lógica do <c>EfaturaCsvImporter</c> do
/// monitor sem depender de <c>Microsoft.VisualBasic</c>.
/// </summary>
public sealed partial class EFaturaCsvReader : IEFaturaCsvReader
{
    private const int BatchWarningThreshold = 300;

    static EFaturaCsvReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<EFaturaCsvResult> ReadAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var text = Decode(bytes);
        var lines = SplitLines(text);
        if (lines.Count == 0)
        {
            throw new EFaturaCsvFormatException("O CSV do e-Fatura está vazio.");
        }

        var delimiter = DetectDelimiter(lines[0]);
        var headers = MakeUnique(ParseLine(lines[0], delimiter).Select(NormalizeForMatch).ToArray());
        if (headers.Length == 0 || headers.All(string.IsNullOrEmpty))
        {
            throw new EFaturaCsvFormatException("O CSV do e-Fatura não tem cabeçalhos.");
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var position = 0; position < headers.Length; position++)
        {
            index.TryAdd(headers[position], position);
        }

        var rows = new List<EFaturaCsvRow>();
        var importable = 0;
        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseLine(line, delimiter);
            var lineNumber = lineIndex + 1;

            string Field(params string[] candidates)
            {
                foreach (var candidate in candidates)
                {
                    if (index.TryGetValue(candidate, out var column) && column < fields.Count)
                    {
                        return fields[column].Trim();
                    }
                }

                return string.Empty;
            }

            var (invoiceNo, atcud) = SplitInvoiceAndAtcud(Field(
                "no fatura / atcud", "numero da fatura / atcud", "numero da fatura", "numero fatura",
                "no da fatura", "documento", "numero do documento"));
            var supplierRaw = Field("emitente", "fornecedor", "nome do emitente", "nome do fornecedor");
            var taxId = Field("nif", "nif emitente", "nif fornecedor", "nif do emitente");
            if (taxId.Length == 0)
            {
                taxId = ExtractTaxId(supplierRaw);
            }

            var supplierName = RemoveLeadingTaxId(supplierRaw, taxId);
            var invoiceDate = ParseDate(Field("data de emissao", "data emissao", "data", "data documento"));
            var invoiceType = Field("tipo", "tipo de documento");
            var status = Field("situacao", "estado");
            var sector = Field("setor", "sector");
            var taxBase = ParseAmount(Field("valor tributavel", "base tributavel", "total liquido", "liquido"));
            var taxAmount = ParseAmount(Field("valor iva", "iva", "imposto", "valor do iva"));
            var grossTotal = ParseAmount(Field("valor total", "total", "total documento"));

            string? error = null;
            if (invoiceNo.Length == 0)
            {
                error = "Sem número de fatura.";
            }
            else if (taxId.Length != 9 || !taxId.All(char.IsAsciiDigit))
            {
                error = $"NIF do emitente inválido ('{taxId}').";
            }
            else if (invoiceDate is null)
            {
                error = "Data de emissão ilegível.";
            }

            rows.Add(new EFaturaCsvRow(
                lineNumber, taxId, supplierName, invoiceNo,
                string.IsNullOrEmpty(atcud) ? null : atcud,
                invoiceDate, invoiceType,
                status.Length == 0 ? null : status,
                sector.Length == 0 ? null : sector,
                taxBase, taxAmount, grossTotal, error));
            importable++;
        }

        var warnings = new List<string>();
        if (importable == BatchWarningThreshold)
        {
            warnings.Add($"O ficheiro tem exatamente {BatchWarningThreshold} registos. A consulta pode ter atingido o limite do Portal; confirme se o período está completo.");
        }
        else if (importable > BatchWarningThreshold)
        {
            warnings.Add($"O ficheiro tem {importable:N0} registos, acima do lote esperado de {BatchWarningThreshold}.");
        }

        return new EFaturaCsvResult(rows, warnings);
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static char DetectDelimiter(string headerLine)
    {
        int Count(char candidate)
        {
            var quoted = false;
            var count = 0;
            foreach (var character in headerLine)
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

        var semicolons = Count(';');
        var commas = Count(',');
        var tabs = Count('\t');
        if (tabs > semicolons && tabs > commas)
        {
            return '\t';
        }

        return semicolons >= commas ? ';' : ',';
    }

    private static List<string> ParseLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (quoted)
            {
                if (character == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string[] MakeUnique(string[] headers)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            used.TryGetValue(header, out var count);
            count++;
            used[header] = count;
            if (count > 1)
            {
                headers[i] = $"{header} ({count})";
            }
        }

        return headers;
    }

    private static string NormalizeForMatch(string value)
    {
        var decomposed = value.Trim().Trim('﻿')
            .Replace('º', 'o').Replace('ª', 'a')
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static (string InvoiceNo, string Atcud) SplitInvoiceAndAtcud(string value)
    {
        var separator = value.LastIndexOf(" / ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return (value.Trim(), string.Empty);
        }

        var possibleAtcud = value[(separator + 3)..].Trim();
        return AtcudPattern().IsMatch(possibleAtcud)
            ? (value[..separator].Trim(), possibleAtcud)
            : (value.Trim(), string.Empty);
    }

    private static string ExtractTaxId(string supplier)
    {
        var match = TaxIdPattern().Match(supplier);
        return match.Success ? match.Value : string.Empty;
    }

    private static string RemoveLeadingTaxId(string supplier, string taxId)
    {
        if (supplier.Length == 0 || taxId.Length == 0)
        {
            return supplier.Trim();
        }

        var position = supplier.IndexOf(taxId, StringComparison.Ordinal);
        return position < 0 ? supplier.Trim() : supplier.Remove(position, taxId.Length).Trim(' ', '-', '–', '—');
    }

    private static DateOnly? ParseDate(string value)
    {
        value = value.Trim();
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso;
        }

        foreach (var format in new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy/MM/dd" })
        {
            if (DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static decimal ParseAmount(string value)
    {
        value = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.Length == 0)
        {
            return 0m;
        }

        // pt-PT: vírgula decimal, ponto como separador de milhares.
        if (value.Contains(',', StringComparison.Ordinal))
        {
            value = value.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    [GeneratedRegex("^[A-Z0-9]+-[A-Z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AtcudPattern();

    [GeneratedRegex("(?<!\\d)\\d{9}(?!\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TaxIdPattern();
}
