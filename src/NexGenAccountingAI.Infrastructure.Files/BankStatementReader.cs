using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NexGenAccountingAI.Application.Banking;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Lê um extrato bancário. Um ficheiro que começa por <c>&lt;</c> é tratado como
/// ISO 20022 <c>camt.053</c> (qualquer versão — os elementos leem-se pelo nome
/// local), com DTD proibido e sem resolução externa; os movimentos que não estão
/// contabilizados pelo banco (<c>Sts</c> diferente de <c>BOOK</c>) ficam de fora.
/// Os restantes são CSV: a linha de cabeçalho procura-se nas primeiras linhas (os
/// bancos põem antes os dados da conta), as colunas reconhecem-se pelo nome sem
/// acentos e os valores aceitam a vírgula decimal portuguesa e o ponto.
/// </summary>
public sealed class BankStatementReader : IBankStatementReader
{
    private const int HeaderSearchLines = 30;

    private static readonly string[] DateHeaders = ["data movimento", "data mov", "data de movimento", "data lancamento", "data de lancamento", "data operacao", "data da operacao", "data contabilistica", "data"];
    private static readonly string[] ValueDateHeaders = ["data valor", "data-valor", "data de valor"];
    private static readonly string[] DescriptionHeaders = ["descricao", "descritivo", "descricao do movimento", "movimento", "designacao", "detalhes", "descricao da operacao"];
    private static readonly string[] DebitHeaders = ["debito", "debitos", "montante a debito", "debito eur", "debito (eur)"];
    private static readonly string[] CreditHeaders = ["credito", "creditos", "montante a credito", "credito eur", "credito (eur)"];
    private static readonly string[] AmountHeaders = ["montante", "valor", "importancia", "montante eur", "montante (eur)", "valor (eur)", "montante da operacao"];
    private static readonly string[] BalanceHeaders = ["saldo", "saldo contabilistico", "saldo disponivel", "saldo apos movimento", "saldo (eur)"];
    private static readonly string[] ReferenceHeaders = ["referencia", "n documento", "no documento", "numero documento", "documento"];

    static BankStatementReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<BankStatementFile> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        var text = Decode(bytes);
        if (text.TrimStart().StartsWith('<'))
        {
            return ReadCamt(text, cancellationToken);
        }

        return ReadCsv(text, cancellationToken);
    }

    private static BankStatementFile ReadCamt(string text, CancellationToken cancellationToken)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new BankStatementFormatException($"O ficheiro não é um XML legível: {exception.Message}");
        }

        var statements = document.Descendants().Where(element => Is(element, "Stmt")).ToList();
        if (statements.Count == 0)
        {
            throw new BankStatementFormatException("O XML não é um extrato ISO 20022 camt.053 (não tem «Stmt»).");
        }

        var warnings = new List<string>();
        var ibans = statements
            .Select(statement => Path(statement, "Acct", "Id", "IBAN")?.Value.Trim())
            .Where(iban => !string.IsNullOrEmpty(iban))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ibans.Count > 1)
        {
            warnings.Add($"O ficheiro tem extratos de {ibans.Count} contas ({string.Join(", ", ibans)}); todos os movimentos vão para a conta escolhida.");
        }

        decimal? opening = null;
        decimal? closing = null;
        DateOnly? closingDate = null;
        var rows = new List<BankStatementRow>();
        var lineNumber = 0;
        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var balance in statement.Elements().Where(element => Is(element, "Bal")))
            {
                var code = Path(balance, "Tp", "CdOrPrtry", "Cd")?.Value.Trim().ToUpperInvariant();
                var amount = SignedAmount(balance);
                if (amount is null)
                {
                    continue;
                }

                if (code is "OPBD" or "PRCD" && opening is null)
                {
                    opening = amount;
                }
                else if (code is "CLBD")
                {
                    closing = amount;
                    closingDate = ParseDate(Child(balance, "Dt") is { } date ? (Child(date, "Dt") ?? Child(date, "DtTm"))?.Value : null);
                }
            }

            foreach (var entry in statement.Elements().Where(element => Is(element, "Ntry")))
            {
                lineNumber++;
                var status = Child(entry, "Sts") is { } sts ? (Child(sts, "Cd")?.Value ?? sts.Value).Trim().ToUpperInvariant() : "BOOK";
                if (status != "BOOK")
                {
                    continue;
                }

                var amount = SignedAmount(entry);
                var booking = ParseDate(DateOf(Child(entry, "BookgDt")));
                var value = ParseDate(DateOf(Child(entry, "ValDt")));
                var remittance = entry.Descendants().Where(element => Is(element, "Ustrd")).Select(element => element.Value.Trim()).Where(item => item.Length > 0).ToList();
                var description = remittance.Count > 0
                    ? string.Join(' ', remittance)
                    : (Child(entry, "AddtlNtryInf")?.Value.Trim() ?? string.Empty);
                var reference = Child(entry, "AcctSvcrRef")?.Value.Trim()
                    ?? entry.Descendants().FirstOrDefault(element => Is(element, "EndToEndId"))?.Value.Trim();

                string? error = null;
                if (amount is null)
                {
                    error = "Movimento sem montante legível.";
                }
                else if (booking is null && value is null)
                {
                    error = "Movimento sem data.";
                }

                rows.Add(new BankStatementRow(
                    lineNumber, booking ?? value, value, Clip(description), amount ?? 0m, null,
                    string.IsNullOrEmpty(reference) || reference == "NOTPROVIDED" ? null : Clip(reference), error));
            }
        }

        return new BankStatementFile("camt.053", ibans.FirstOrDefault(), opening, closing, closingDate, rows, warnings);
    }

    private static BankStatementFile ReadCsv(string text, CancellationToken cancellationToken)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var headerIndex = -1;
        var delimiter = ';';
        Dictionary<string, int>? columns = null;
        for (var i = 0; i < Math.Min(lines.Length, HeaderSearchLines) && columns is null; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var candidateDelimiter = DetectDelimiter(lines[i]);
            var headers = ParseLine(lines[i], candidateDelimiter).Select(Normalize).ToList();
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            void Find(string key, string[] names)
            {
                // Primeiro o nome exato; depois um cabeçalho que comece pelo nome (ex.: «Débito (EUR)»).
                var position = headers.FindIndex(header => names.Contains(header));
                if (position < 0)
                {
                    position = headers.FindIndex(header => names.Any(name => header.StartsWith(name + " ", StringComparison.Ordinal)));
                }

                if (position >= 0 && !found.ContainsValue(position))
                {
                    found[key] = position;
                }
            }

            Find("value", ValueDateHeaders);
            Find("date", DateHeaders);
            Find("description", DescriptionHeaders);
            Find("debit", DebitHeaders);
            Find("credit", CreditHeaders);
            Find("amount", AmountHeaders);
            Find("balance", BalanceHeaders);
            Find("reference", ReferenceHeaders);
            if (found.ContainsKey("date") && found.ContainsKey("description") &&
                (found.ContainsKey("amount") || (found.ContainsKey("debit") && found.ContainsKey("credit"))))
            {
                columns = found;
                headerIndex = i;
                delimiter = candidateDelimiter;
            }
        }

        if (columns is null)
        {
            throw new BankStatementFormatException(
                "Não foi possível reconhecer o extrato: o CSV precisa de colunas de data, descrição e montante (ou débito e crédito).");
        }

        var rows = new List<BankStatementRow>();
        var warnings = new List<string>();
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = ParseLine(lines[i], delimiter);
            string Field(string key) => columns.TryGetValue(key, out var position) && position < fields.Count ? fields[position].Trim() : string.Empty;

            var dateText = Field("date");
            var date = ParseDate(dateText);
            if (dateText.Length == 0)
            {
                // Linhas de totais ou de rodapé: sem data, não são movimentos.
                continue;
            }

            decimal? amount;
            if (columns.ContainsKey("amount"))
            {
                amount = ParseAmount(Field("amount"));
            }
            else
            {
                var debit = ParseAmount(Field("debit"));
                var credit = ParseAmount(Field("credit"));
                amount = debit is null && credit is null ? null : (credit ?? 0m) - Math.Abs(debit ?? 0m);
            }

            string? error = null;
            if (date is null)
            {
                error = $"Data ilegível ('{dateText}').";
            }
            else if (amount is null)
            {
                error = "Montante ilegível.";
            }

            rows.Add(new BankStatementRow(
                i + 1, date, ParseDate(Field("value")), Clip(Field("description")), amount ?? 0m,
                columns.ContainsKey("balance") ? ParseAmount(Field("balance")) : null,
                Field("reference") is { Length: > 0 } reference ? Clip(reference) : null,
                error));
        }

        // O saldo de cada linha permite deduzir os saldos do extrato, quando o banco os dá.
        decimal? opening = null;
        decimal? closing = null;
        DateOnly? closingDate = null;
        var valid = rows.Where(row => row.IsValid && row.Balance is not null).ToList();
        if (valid.Count > 0)
        {
            var ascending = valid.Count < 2 || valid[0].BookingDate <= valid[^1].BookingDate;
            var first = ascending ? valid[0] : valid[^1];
            var last = ascending ? valid[^1] : valid[0];
            opening = first.Balance - first.Amount;
            closing = last.Balance;
            closingDate = last.BookingDate;
        }

        if (rows.Count == 0)
        {
            warnings.Add("O extrato não tem movimentos.");
        }

        return new BankStatementFile("CSV", null, opening, closing, closingDate, rows, warnings);
    }

    private static decimal? SignedAmount(XElement element)
    {
        var amountElement = Child(element, "Amt");
        if (amountElement is null ||
            !decimal.TryParse(amountElement.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var indicator = Child(element, "CdtDbtInd")?.Value.Trim().ToUpperInvariant();
        return indicator == "DBIT" ? -amount : amount;
    }

    private static string? DateOf(XElement? element) =>
        element is null ? null : (Child(element, "Dt") ?? Child(element, "DtTm"))?.Value;

    private static bool Is(XElement element, string localName) => element.Name.LocalName == localName;

    private static XElement? Child(XElement element, string localName) => element.Elements().FirstOrDefault(child => Is(child, localName));

    private static XElement? Path(XElement element, params string[] localNames)
    {
        var current = element;
        foreach (var name in localNames)
        {
            current = current is null ? null : Child(current, name);
        }

        return current;
    }

    private static string Clip(string value) => value.Length <= 500 ? value : value[..500];

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

    private static char DetectDelimiter(string line)
    {
        var counts = new[] { ';', ',', '\t' }.ToDictionary(candidate => candidate, candidate =>
        {
            var quoted = false;
            var count = 0;
            foreach (var character in line)
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
        });
        if (counts['\t'] > counts[';'] && counts['\t'] > counts[','])
        {
            return '\t';
        }

        return counts[';'] >= counts[','] ? ';' : ',';
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
                if (character == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (character == '"')
                {
                    quoted = false;
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

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().Trim('﻿').Replace('º', 'o').Replace('ª', 'a').Replace(".", string.Empty, StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateOnly? ParseDate(string? value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length > 10 && (value[10] == 'T' || value[10] == ' '))
        {
            value = value[..10];
        }

        foreach (var format in new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "dd.MM.yyyy", "d/M/yyyy", "d-M-yyyy" })
        {
            if (DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>
    /// Aceita «1.234,56», «1234,56», «1,234.56», «1234.56», «-12,30» e «12,30-».
    /// Com ponto e vírgula, o último dos dois é o separador decimal.
    /// </summary>
    private static decimal? ParseAmount(string value)
    {
        value = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("€", string.Empty, StringComparison.Ordinal).Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (value.Length == 0)
        {
            return null;
        }

        var negative = false;
        if (value.EndsWith('-'))
        {
            negative = true;
            value = value[..^1];
        }

        var lastComma = value.LastIndexOf(',');
        var lastDot = value.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            value = lastComma > lastDot
                ? value.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : value.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (lastComma >= 0)
        {
            value = value.Replace(',', '.');
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            return null;
        }

        return negative ? -result : result;
    }
}
