using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AccountingAI.Core.Models;
using AccountingAI.Core.Parsing;

namespace AccountingAI.Core.Efatura;

public sealed class AtEfaturaPurchaseSource : IEfaturaPurchaseSource, IDisposable
{
    private const int PageSize = 5000;
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Security = "http://schemas.xmlsoap.org/ws/2002/12/secext";
    private static readonly XNamespace Fatshare = "http://factemi.at.min_financas.pt/fatshareInvoices";
    private readonly EfaturaAtConnection _connection;
    private readonly HttpClient _httpClient;

    public AtEfaturaPurchaseSource(EfaturaAtConnection connection)
    {
        _connection = connection;
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual
        };
        handler.ClientCertificates.Add(connection.ClientCertificate);
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    public async Task<EfaturaQueryResult> GetPurchasesAsync(
        DateOnly startDate,
        DateOnly endDate,
        IProgress<EfaturaQueryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("A data final não pode ser anterior à data inicial.", nameof(endDate));
        }

        var result = new EfaturaQueryResult();
        var uniqueDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentsRead = 0;

        foreach (var interval in BuildQueryIntervals(startDate, endDate))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new EfaturaQueryProgress(interval.StartDate, page, totalPages, documentsRead,
                    $"A consultar {interval.StartDate:dd-MM-yyyy} a {interval.EndDate:dd-MM-yyyy}, página {page}…"));

                var response = await QueryPageWithRetryAsync(
                        interval.StartDate,
                        interval.EndDate,
                        page,
                        progress,
                        documentsRead,
                        cancellationToken)
                    .ConfigureAwait(false);
                totalPages = response.TotalPages;
                foreach (var invoice in response.Invoices)
                {
                    var document = XmlRecordFlattener.Flatten(invoice, "AT e-Fatura", "Compra e-Fatura", "LineSummary");
                    document["Período inicial consultado"] = interval.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    document["Período final consultado"] = interval.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    document["Página AT"] = page.ToString(CultureInfo.InvariantCulture);
                    document["Origem da consulta"] = "Webservice oficial da AT";
                    var identity = string.Join("|", document["TaxRegistrationNumber"], document["InvoiceNo"],
                        document["InvoiceDate"], document["InvoiceType"], document["ATCUD"]);
                    if (!uniqueDocuments.Add(identity))
                    {
                        continue;
                    }

                    document["DocumentKey"] = identity;
                    result.Purchases.Add(document);
                    documentsRead++;
                    var lineNumber = 0;
                    foreach (var line in invoice.Elements().Where(element => element.Name.LocalName == "LineSummary"))
                    {
                        lineNumber++;
                        var lineRecord = XmlRecordFlattener.Flatten(line, "AT e-Fatura", "Linha da compra");
                        lineRecord["InvoiceNo"] = document["InvoiceNo"];
                        lineRecord["TaxRegistrationNumber"] = document["TaxRegistrationNumber"];
                        lineRecord["InvoiceDate"] = document["InvoiceDate"];
                        lineRecord["DocumentKey"] = identity;
                        lineRecord["Número da linha AT"] = lineNumber.ToString(CultureInfo.InvariantCulture);
                        lineRecord["Origem da consulta"] = "Webservice oficial da AT";
                        result.Lines.Add(lineRecord);
                    }
                }

                progress?.Report(new EfaturaQueryProgress(interval.StartDate, page, totalPages, documentsRead,
                    $"{documentsRead:N0} documento(s) lido(s) até {interval.EndDate:dd-MM-yyyy}."));
                page++;
            }
            while (page <= totalPages);
        }

        result.Messages.Add($"Consulta concluída: {documentsRead:N0} documento(s), de {startDate:dd-MM-yyyy} a {endDate:dd-MM-yyyy}.");
        return result;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<PageResponse> QueryPageWithRetryAsync(
        DateOnly startDate,
        DateOnly endDate,
        int page,
        IProgress<EfaturaQueryProgress>? progress,
        int documentsRead,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await QueryPageAsync(startDate, endDate, page, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 3 && IsTransient(exception, cancellationToken))
            {
                var delay = TimeSpan.FromSeconds(attempt * 2);
                progress?.Report(new EfaturaQueryProgress(startDate, page, 1, documentsRead,
                    $"Falha temporária na AT; nova tentativa {attempt + 1}/3 em {delay.TotalSeconds:N0}s…"));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<PageResponse> QueryPageAsync(
        DateOnly startDate,
        DateOnly endDate,
        int page,
        CancellationToken cancellationToken)
    {
        var envelope = BuildEnvelope(startDate, endDate, page);
        using var request = new HttpRequestMessage(HttpMethod.Post, _connection.Endpoint);
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false), "text/xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"A AT devolveu HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {ExtractFault(xml)}",
                null,
                response.StatusCode);
        }

        return ParseResponse(xml);
    }

    internal XDocument BuildEnvelope(DateOnly startDate, DateOnly endDate, int page)
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.ff'Z'", CultureInfo.InvariantCulture);
        var encryptedPassword = EncryptAes(_connection.PortalPassword, key);
        var encryptedCreated = EncryptAes(timestamp, key);
        var encryptedNonce = Convert.ToBase64String(_connection.AuthenticationPublicKey.Encrypt(key, RSAEncryptionPadding.Pkcs1));

        return new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "S", Soap),
                new XAttribute(XNamespace.Xmlns + "wss", Security),
                new XAttribute(XNamespace.Xmlns + "fat", Fatshare),
                new XElement(Soap + "Header",
                    new XElement(Security + "Security",
                        new XElement(Security + "UsernameToken",
                            new XElement(Security + "Username", _connection.Username),
                            new XElement(Security + "Password", encryptedPassword),
                            new XElement(Security + "Nonce", encryptedNonce),
                            new XElement(Security + "Created", encryptedCreated)))),
                new XElement(Soap + "Body",
                    new XElement(Fatshare + "InvoicesRequest",
                        new XElement(Fatshare + "CustomerTaxID", _connection.CustomerTaxId),
                        new XElement(Fatshare + "StartDate", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(Fatshare + "EndDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(Fatshare + "Pagination",
                            new XElement(Fatshare + "nPage", page),
                            new XElement(Fatshare + "nDocsPage", PageSize))))));
    }

    internal static IReadOnlyList<QueryInterval> BuildQueryIntervals(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("A data final não pode ser anterior à data inicial.", nameof(endDate));
        }

        var intervals = new List<QueryInterval>();
        var current = startDate;
        while (current <= endDate)
        {
            var endOfMonth = new DateOnly(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
            var intervalEnd = endOfMonth < endDate ? endOfMonth : endDate;
            intervals.Add(new QueryInterval(current, intervalEnd));
            current = intervalEnd.AddDays(1);
        }

        return intervals;
    }

    internal static PageResponse ParseResponse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new InvalidDataException("A AT devolveu uma resposta que não é XML válido.", exception);
        }

        var fault = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Fault");
        if (fault is not null)
        {
            throw new InvalidOperationException($"Falha SOAP da AT: {fault.Value.Trim()}");
        }

        var operationText = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "EstadoOperacao")?.Value;
        if (!int.TryParse(operationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var operation) || operation != 200)
        {
            var description = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Desc")?.Value;
            throw new InvalidOperationException($"A consulta e-Fatura foi rejeitada pela AT ({operationText ?? "sem código"}): {description ?? "sem descrição"}");
        }

        var invoiceList = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "InvoicesList");
        var invoices = invoiceList?.Elements().Where(element => element.Name.LocalName == "Invoice").ToList() ?? [];
        var totalPagesText = invoiceList?.Descendants().FirstOrDefault(element => element.Name.LocalName == "totalPages")?.Value;
        var totalPages = int.TryParse(totalPagesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPages)
            ? Math.Max(1, parsedPages)
            : 1;
        return new PageResponse(invoices, totalPages);
    }

    private static string EncryptAes(string value, byte[] key)
    {
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        var plain = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(encryptor.TransformFinalBlock(plain, 0, plain.Length));
    }

    private static string ExtractFault(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Resposta sem conteúdo.";
        }

        try
        {
            var document = XDocument.Parse(content);
            var fault = document.Descendants().FirstOrDefault(element => element.Name.LocalName is "faultstring" or "Fault");
            return fault?.Value.Trim() ?? string.Empty;
        }
        catch (System.Xml.XmlException)
        {
            return content.Length <= 300 ? content : content[..300];
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (exception is TaskCanceledException)
        {
            return true;
        }

        return exception is HttpRequestException requestException &&
               (requestException.StatusCode is null ||
                (int)requestException.StatusCode >= 500 ||
                (int)requestException.StatusCode is 408 or 429);
    }

    internal sealed record PageResponse(IReadOnlyList<XElement> Invoices, int TotalPages);

    internal sealed record QueryInterval(DateOnly StartDate, DateOnly EndDate);
}
