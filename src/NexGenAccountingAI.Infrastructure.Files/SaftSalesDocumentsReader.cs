using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Lê os documentos de <c>SourceDocuments/SalesInvoices</c> de um SAF-T (PT) de
/// faturação em streaming com <see cref="XmlReader"/> (DTD proibido, sem resolução
/// externa, Windows-1252 suportado), materializando um <c>Customer</c> ou um
/// <c>Invoice</c> de cada vez. Os clientes vêm antes dos documentos no ficheiro,
/// por isso o cliente de cada documento resolve-se à passagem. A base de cada
/// taxa soma as linhas (<c>CreditAmount</c> menos <c>DebitAmount</c>); o imposto
/// de cada taxa calcula-se pela percentagem e o acerto até aos totais do
/// documento (<c>NetTotal</c>, <c>TaxPayable</c>) fica na taxa com mais base.
/// </summary>
public sealed class SaftSalesDocumentsReader : ISaftSalesDocumentsReader
{
    static SaftSalesDocumentsReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SaftSalesDocumentsResult> ReadAsync(Stream content, CancellationToken cancellationToken = default)
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

        string? companyTaxId = null;
        var customers = new Dictionary<string, Customer>(StringComparer.Ordinal);
        var documents = new List<SaftSalesDocument>();
        var warnings = new List<string>();
        var inSalesInvoices = false;

        try
        {
            using var reader = XmlReader.Create(content, settings);
            var moved = await reader.ReadAsync().ConfigureAwait(false);
            while (moved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.EndElement && Is(reader.LocalName, "SalesInvoices"))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    moved = await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                var consumed = false;
                if (Is(reader.LocalName, "TaxRegistrationNumber") && companyTaxId is null && reader.Depth == 2)
                {
                    companyTaxId = (await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                    consumed = true;
                }
                else if (Is(reader.LocalName, "Customer") && !inSalesInvoices && reader.Depth == 2)
                {
                    var element = await LoadAsync(reader, cancellationToken).ConfigureAwait(false);
                    var id = Child(element, "CustomerID")?.Trim();
                    if (!string.IsNullOrEmpty(id))
                    {
                        var billing = element.Elements().FirstOrDefault(item => Is(item.Name.LocalName, "BillingAddress"));
                        customers[id] = new Customer(
                            (Child(element, "CustomerTaxID") ?? string.Empty).Trim(),
                            (Child(element, "CompanyName") ?? string.Empty).Trim(),
                            billing is null ? null : Child(billing, "Country")?.Trim());
                    }

                    consumed = true;
                }
                else if (Is(reader.LocalName, "SalesInvoices"))
                {
                    inSalesInvoices = true;
                }
                else if (Is(reader.LocalName, "Invoice") && inSalesInvoices)
                {
                    var element = await LoadAsync(reader, cancellationToken).ConfigureAwait(false);
                    if (Build(element, customers, warnings) is { } document)
                    {
                        documents.Add(document);
                    }

                    consumed = true;
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
            warnings.Add("O ficheiro não tem documentos de venda (SalesInvoices): não é um SAF-T de faturação.");
        }

        return new SaftSalesDocumentsResult(companyTaxId, documents, warnings);
    }

    private static SaftSalesDocument? Build(XElement invoice, IReadOnlyDictionary<string, Customer> customers, List<string> warnings)
    {
        var number = (Child(invoice, "InvoiceNo") ?? string.Empty).Trim();
        var date = ParseDate(Child(invoice, "InvoiceDate"));
        if (number.Length == 0 || date is null)
        {
            warnings.Add($"Documento ignorado: sem número ou data legível ('{number}').");
            return null;
        }

        var status = invoice.Elements().FirstOrDefault(item => Is(item.Name.LocalName, "DocumentStatus"));
        var isVoided = string.Equals(
            (status is null ? null : Child(status, "InvoiceStatus"))?.Trim(), "A", StringComparison.OrdinalIgnoreCase);
        var customerId = (Child(invoice, "CustomerID") ?? string.Empty).Trim();
        var customer = customers.GetValueOrDefault(customerId) ?? new Customer(string.Empty, string.Empty, null);

        var bases = new Dictionary<decimal, decimal>();
        foreach (var line in invoice.Elements().Where(item => Is(item.Name.LocalName, "Line")))
        {
            var amount = (ParseDecimal(Child(line, "CreditAmount")) ?? 0m) - (ParseDecimal(Child(line, "DebitAmount")) ?? 0m);
            var tax = line.Elements().FirstOrDefault(item => Is(item.Name.LocalName, "Tax"));
            var percentage = decimal.Round(tax is null ? 0m : ParseDecimal(Child(tax, "TaxPercentage")) ?? 0m, 2);
            bases[percentage] = bases.GetValueOrDefault(percentage) + amount;
        }

        var totals = invoice.Elements().FirstOrDefault(item => Is(item.Name.LocalName, "DocumentTotals"));
        var net = Math.Abs(ParseDecimal(totals is null ? null : Child(totals, "NetTotal")) ?? 0m);
        var taxPayable = Math.Abs(ParseDecimal(totals is null ? null : Child(totals, "TaxPayable")) ?? 0m);
        var gross = Math.Abs(ParseDecimal(totals is null ? null : Child(totals, "GrossTotal")) ?? 0m);

        var taxes = bases
            .Where(pair => pair.Value != 0m)
            .OrderByDescending(pair => Math.Abs(pair.Value))
            .Select(pair =>
            {
                var taxBase = decimal.Round(Math.Abs(pair.Value), 2, MidpointRounding.AwayFromZero);
                return new SaftSalesDocumentTax(
                    pair.Key, taxBase, decimal.Round(taxBase * pair.Key / 100m, 2, MidpointRounding.AwayFromZero));
            })
            .ToList();

        // Os acertos ao cêntimo (arredondamentos, descontos de cabeçalho) ficam na taxa com mais base.
        if (taxes.Count > 0)
        {
            var baseDelta = net - taxes.Sum(item => item.TaxBase);
            var taxDelta = taxPayable - taxes.Sum(item => item.TaxAmount);
            if (baseDelta != 0m || taxDelta != 0m)
            {
                taxes[0] = taxes[0] with { TaxBase = taxes[0].TaxBase + baseDelta, TaxAmount = taxes[0].TaxAmount + taxDelta };
            }
        }
        else if (net != 0m || taxPayable != 0m)
        {
            taxes.Add(new SaftSalesDocumentTax(0m, net, taxPayable));
        }

        return new SaftSalesDocument(
            number,
            Child(invoice, "ATCUD")?.Trim() is { Length: > 0 } atcud ? atcud : null,
            (Child(invoice, "InvoiceType") ?? string.Empty).Trim(),
            date.Value,
            isVoided,
            customerId,
            customer.TaxId,
            customer.Name,
            customer.Country,
            net,
            taxPayable,
            gross,
            taxes);
    }

    private static async Task<XElement> LoadAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        using var subtree = reader.ReadSubtree();
        return await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken).ConfigureAwait(false);
    }

    private static bool Is(string name, string expected) => name.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => Is(element.Name.LocalName, localName))?.Value;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse((value ?? string.Empty).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;

    private sealed record Customer(string TaxId, string Name, string? Country);
}
