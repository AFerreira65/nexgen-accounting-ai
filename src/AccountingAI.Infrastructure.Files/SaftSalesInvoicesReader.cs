using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files;

/// <summary>
/// Lê um resumo do bloco <c>SourceDocuments/SalesInvoices</c> de um SAF-T (PT) de
/// faturação totalmente em streaming com <see cref="XmlReader"/> (DTD proibido,
/// sem resolução externa, Windows-1252 suportado): totais declarados no cabeçalho
/// do bloco e a soma das faturas por mês, sem as anuladas. Cada <c>&lt;Invoice&gt;</c>
/// é lida como um subárvore de cada vez, pelo que suporta ficheiros grandes. Um
/// ficheiro sem o bloco devolve <c>HasSalesInvoices = false</c>.
/// </summary>
public sealed class SaftSalesInvoicesReader : ISaftSalesInvoicesReader
{
    static SaftSalesInvoicesReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SaftSalesInvoicesSummary> ReadSummaryAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = new XmlReaderSettings
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
            XmlResolver = null
        };

        var inSalesInvoices = false;
        int? declaredNumberOfEntries = null;
        decimal? declaredTotalDebit = null;
        decimal? declaredTotalCredit = null;
        var invoiceCount = 0;
        var voidedCount = 0;
        var net = 0m;
        var tax = 0m;
        var gross = 0m;
        DateOnly? fromDate = null;
        DateOnly? toDate = null;
        var months = new Dictionary<(int Year, int Month), MonthAccumulator>();

        try
        {
            using var reader = XmlReader.Create(content, settings);
            var moved = await reader.ReadAsync().ConfigureAwait(false);
            while (moved)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.EndElement &&
                    reader.LocalName.Equals("SalesInvoices", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    moved = await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                var consumed = false;
                switch (reader.LocalName)
                {
                    case "SalesInvoices":
                        inSalesInvoices = true;
                        break;
                    case "NumberOfEntries" when inSalesInvoices:
                        declaredNumberOfEntries = ParseInt(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TotalDebit" when inSalesInvoices:
                        declaredTotalDebit = ParseDecimal(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TotalCredit" when inSalesInvoices:
                        declaredTotalCredit = ParseDecimal(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "Invoice" when inSalesInvoices:
                        using (var subtree = reader.ReadSubtree())
                        {
                            var element = await XElement
                                .LoadAsync(subtree, LoadOptions.None, cancellationToken)
                                .ConfigureAwait(false);

                            var status = (Nested(element, "DocumentStatus", "InvoiceStatus") ?? string.Empty).Trim();
                            var isVoided = status.Equals("A", StringComparison.OrdinalIgnoreCase);
                            var date = ParseDate(ChildValue(element, "InvoiceDate") ?? string.Empty);
                            var invoiceNet = ParseDecimal(Nested(element, "DocumentTotals", "NetTotal") ?? string.Empty) ?? 0m;
                            var invoiceTax = ParseDecimal(Nested(element, "DocumentTotals", "TaxPayable") ?? string.Empty) ?? 0m;
                            var invoiceGross = ParseDecimal(Nested(element, "DocumentTotals", "GrossTotal") ?? string.Empty) ?? 0m;

                            if (isVoided)
                            {
                                voidedCount++;
                            }
                            else
                            {
                                invoiceCount++;
                                net += invoiceNet;
                                tax += invoiceTax;
                                gross += invoiceGross;
                                if (date is { } day)
                                {
                                    if (fromDate is null || day < fromDate) { fromDate = day; }
                                    if (toDate is null || day > toDate) { toDate = day; }

                                    var key = (day.Year, day.Month);
                                    if (!months.TryGetValue(key, out var accumulator))
                                    {
                                        accumulator = new MonthAccumulator();
                                        months[key] = accumulator;
                                    }

                                    accumulator.Count++;
                                    accumulator.Net += invoiceNet;
                                    accumulator.Tax += invoiceTax;
                                    accumulator.Gross += invoiceGross;
                                }
                            }
                        }
                        // O leitor exterior fica no fim do elemento <Invoice>.
                        break;
                }

                moved = consumed
                    ? reader.ReadState == ReadState.Interactive
                    : await reader.ReadAsync().ConfigureAwait(false);
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }

        if (!inSalesInvoices)
        {
            return new SaftSalesInvoicesSummary(false, null, null, null, 0, 0, 0m, 0m, 0m, null, null, []);
        }

        var monthList = months
            .OrderBy(pair => pair.Key.Year)
            .ThenBy(pair => pair.Key.Month)
            .Select(pair => new SaftSalesInvoiceMonth(
                pair.Key.Year,
                pair.Key.Month,
                pair.Value.Count,
                decimal.Round(pair.Value.Net, 2),
                decimal.Round(pair.Value.Tax, 2),
                decimal.Round(pair.Value.Gross, 2)))
            .ToList();

        return new SaftSalesInvoicesSummary(
            true,
            declaredNumberOfEntries,
            declaredTotalDebit,
            declaredTotalCredit,
            invoiceCount,
            voidedCount,
            decimal.Round(net, 2),
            decimal.Round(tax, 2),
            decimal.Round(gross, 2),
            fromDate,
            toDate,
            monthList);
    }

    private static string? ChildValue(XElement parent, string localName) =>
        parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?
            .Value;

    private static string? Nested(XElement parent, string outerLocalName, string innerLocalName)
    {
        var outer = parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(outerLocalName, StringComparison.OrdinalIgnoreCase));
        return outer is null ? null : ChildValue(outer, innerLocalName);
    }

    private static int? ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;

    private sealed class MonthAccumulator
    {
        public int Count;
        public decimal Net;
        public decimal Tax;
        public decimal Gross;
    }
}
