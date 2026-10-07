using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Efatura;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// A consulta de faturas do e-Fatura como adquirente (serviço <c>fatshare</c>, WSDL
/// <c>fatshareInvoices</c> e manual «Aspetos Específicos», secção 2.1.10). O transporte é o dos
/// outros webservices (<see cref="AtSoapClient"/>), mas o cabeçalho é o simples do
/// <see cref="AtWsSecurity.BuildFatshareHeader"/>. A AT só responde a um mês de cada vez (código
/// 434), por isso o período é partido por meses e cada mês paginado.
/// <para>
/// O utilizador tem de ser um subutilizador do sujeito passivo com permissão de consulta (WFA) e o
/// NIF do adquirente tem de ser o do próprio login (código 414). Os endereços vêm da aplicação
/// anterior (portas 725 e 425); o manual não os indica.
/// </para>
/// </summary>
public sealed class EFaturaAtGateway(AtSoapClient client, TimeProvider time) : IEFaturaAtGateway
{
    public const string ProductionEndpoint = "https://servicos.portaldasfinancas.gov.pt:425/fatshare/ws/fatshareFaturas";
    public const string TestEndpoint = "https://servicos.portaldasfinancas.gov.pt:725/fatshare/ws/fatshareFaturas";
    public const int PageSize = 5000;

    private const int Attempts = 3;
    private const int EmptyListCode = 486;
    private static readonly XNamespace Fatshare = "http://factemi.at.min_financas.pt/fatshareInvoices";

    public AtGatewayStatus Status() => client.Status();

    public async Task<EFaturaAtFetchResult> FetchPurchasesAsync(
        DateOnly from, DateOnly to, AtCredential credential, string customerTaxId, bool production, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerTaxId);
        var endpoint = production ? ProductionEndpoint : TestEndpoint;
        var invoices = new List<EFaturaAtInvoice>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long duration = 0;
        int? httpStatus = null;

        foreach (var interval in MonthlyIntervals(from, to))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                var response = await QueryPageAsync(endpoint, credential, customerTaxId, interval, page, cancellationToken).ConfigureAwait(false);
                duration += response.Duration;
                httpStatus = response.HttpStatus ?? httpStatus;
                var where = $"{interval.Start:dd-MM-yyyy} a {interval.End:dd-MM-yyyy}";
                if (!response.Reached)
                {
                    return new EFaturaAtFetchResult(false, null, $"{response.Message} (consulta de {where}).", invoices, warnings, httpStatus, duration);
                }

                if (response.Page.Code != 200)
                {
                    return new EFaturaAtFetchResult(true, response.Page.Code, $"{response.Page.Message} (consulta de {where}).", invoices, warnings, httpStatus, duration);
                }

                totalPages = response.Page.TotalPages;
                warnings.AddRange(response.Page.Warnings);
                foreach (var invoice in response.Page.Invoices)
                {
                    if (seen.Add($"{invoice.SupplierTaxId}|{invoice.InvoiceNo}|{invoice.InvoiceDate:yyyy-MM-dd}|{invoice.InvoiceType}|{invoice.Atcud}"))
                    {
                        invoices.Add(invoice);
                    }
                }

                page++;
            }
            while (page <= totalPages);
        }

        return new EFaturaAtFetchResult(
            true, 200, $"{invoices.Count:N0} documento(s) de {from:dd-MM-yyyy} a {to:dd-MM-yyyy}.", invoices, warnings, httpStatus, duration);
    }

    /// <summary>Os meses (inteiros ou parciais) entre as duas datas, porque a AT só responde a um de cada vez.</summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> MonthlyIntervals(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("A data final não pode ser anterior à inicial.", nameof(to));
        }

        var intervals = new List<(DateOnly, DateOnly)>();
        var current = from;
        while (current <= to)
        {
            var endOfMonth = new DateOnly(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
            var end = endOfMonth < to ? endOfMonth : to;
            intervals.Add((current, end));
            current = end.AddDays(1);
        }

        return intervals;
    }

    /// <summary>O envelope do pedido de um mês e de uma página.</summary>
    public static XDocument Envelope(
        AtCredential credential, string customerTaxId, DateOnly start, DateOnly end, int page, RSA authenticationPublicKey, DateTimeOffset now) =>
        new(new XElement(AtWsSecurity.Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "S", AtWsSecurity.Soap.NamespaceName),
            new XElement(AtWsSecurity.Soap + "Header", AtWsSecurity.BuildFatshareHeader(credential, authenticationPublicKey, now)),
            new XElement(AtWsSecurity.Soap + "Body",
                new XElement(Fatshare + "InvoicesRequest",
                    new XAttribute(XNamespace.Xmlns + "fat", Fatshare.NamespaceName),
                    new XElement(Fatshare + "CustomerTaxID", customerTaxId.Trim()),
                    new XElement(Fatshare + "StartDate", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement(Fatshare + "EndDate", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement(Fatshare + "Pagination",
                        new XElement(Fatshare + "nPage", page),
                        new XElement(Fatshare + "nDocsPage", PageSize))))));

    /// <summary>Lê uma página de resposta; uma falha SOAP ou um estado diferente de 200 vem no <see cref="EFaturaAtPage.Code"/> e na mensagem.</summary>
    public static EFaturaAtPage ParseResponse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return new EFaturaAtPage(null, "A AT respondeu com um conteúdo que não é XML.", [], [], 1);
        }

        var fault = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Fault");
        if (fault is not null)
        {
            var text = fault.Elements().FirstOrDefault(element => element.Name.LocalName == "faultstring")?.Value.Trim();
            return new EFaturaAtPage(null, $"A AT recusou o pedido: {text ?? "erro sem descrição"}.", [], [], 1);
        }

        string? Text(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();

        var status = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "estadoExecucao");
        if (status is null || !int.TryParse(Text(status, "EstadoOperacao"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return new EFaturaAtPage(null, "A resposta da AT não tem o formato esperado.", [], [], 1);
        }

        var description = Text(status, "Desc");
        if (code == EmptyListCode)
        {
            // Um mês sem documentos não vem como sucesso vazio mas com este código («Lista de faturas vazia»), que o manual não lista.
            return new EFaturaAtPage(200, description ?? string.Empty, [], [], 1);
        }

        if (code != 200)
        {
            return new EFaturaAtPage(code, $"A AT recusou a consulta ({code}): {description ?? "sem descrição"}.", [], [], 1);
        }

        var list = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "InvoicesList");
        var invoices = new List<EFaturaAtInvoice>();
        var warnings = new List<string>();
        foreach (var element in list?.Elements().Where(item => item.Name.LocalName == "Invoice") ?? Enumerable.Empty<XElement>())
        {
            var invoice = ReadInvoice(element);
            if (invoice is not null)
            {
                invoices.Add(invoice);
            }
            else
            {
                warnings.Add($"Documento ignorado por estar incompleto: {Text(element, "InvoiceNo") ?? "sem número"}.");
            }
        }

        var pages = list?.Descendants().FirstOrDefault(element => element.Name.LocalName == "totalPages")?.Value;
        var totalPages = int.TryParse(pages, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(1, parsed) : 1;
        return new EFaturaAtPage(200, description ?? string.Empty, invoices, warnings, totalPages);
    }

    private static EFaturaAtInvoice? ReadInvoice(XElement invoice)
    {
        string? Text(string name) =>
            invoice.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

        bool Amount(string name, out decimal value)
        {
            value = 0m;
            return Text(name) is { } text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        var supplier = Text("TaxRegistrationNumber");
        var number = Text("InvoiceNo");
        if (supplier is null || number is null ||
            !DateOnly.TryParseExact(Text("InvoiceDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            !Amount("NetTotal", out var net) || !Amount("TaxPayable", out var tax) || !Amount("GrossTotal", out var gross))
        {
            return null;
        }

        return new EFaturaAtInvoice(supplier, number, Text("ATCUD"), date, Text("InvoiceType") ?? string.Empty, Text("Sector"), net, tax, gross);
    }

    private async Task<PageOutcome> QueryPageAsync(
        string endpoint, AtCredential credential, string customerTaxId, (DateOnly Start, DateOnly End) interval, int page, CancellationToken cancellationToken)
    {
        long duration = 0;
        AtRawResponse raw;
        for (var attempt = 1; ; attempt++)
        {
            raw = await client.PostAsync(
                    endpoint, "\"\"",
                    publicKey => Envelope(credential, customerTaxId, interval.Start, interval.End, page, publicKey, time.GetUtcNow()),
                    cancellationToken)
                .ConfigureAwait(false);
            duration += raw.DurationMilliseconds;
            // Só se repete uma falha de comunicação ou do servidor da AT; um erro de credenciais ou de pedido repetir-se-ia igual.
            var transient = !raw.Reached || (raw.HttpStatus >= 500 && !raw.Content.Contains("Fault", StringComparison.Ordinal));
            if (!transient || attempt >= Attempts)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
        }

        return raw.Reached
            ? new PageOutcome(true, raw.HttpStatus, ParseResponse(raw.Content), raw.Problem ?? string.Empty, duration)
            : new PageOutcome(false, raw.HttpStatus, new EFaturaAtPage(null, string.Empty, [], [], 1), raw.Problem ?? string.Empty, duration);
    }

    public sealed record EFaturaAtPage(
        int? Code, string Message, IReadOnlyList<EFaturaAtInvoice> Invoices, IReadOnlyList<string> Warnings, int TotalPages);

    private sealed record PageOutcome(bool Reached, int? HttpStatus, EFaturaAtPage Page, string Message, long Duration);
}
