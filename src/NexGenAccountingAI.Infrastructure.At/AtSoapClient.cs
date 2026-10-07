using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>
/// O transporte comum dos webservices SOAP da AT (DP de IVA, Modelo 22…): HTTPS com o
/// certificado do produtor de software, as credenciais cifradas no cabeçalho
/// (<see cref="AtWsSecurity"/>) e a leitura da resposta. Cada webservice só traz o seu
/// endereço, o nome da operação e o corpo do pedido. Nenhuma senha nem o conteúdo
/// cifrado ficam em logs.
/// </summary>
public sealed class AtSoapClient(
    AtCertificateProvider certificates,
    TimeProvider time)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public AtGatewayStatus Status()
    {
        var certificateReady = certificates.TryGetCertificate(out var certificate, forTls: false);
        certificate?.Dispose();
        using var publicKey = certificates.LoadPublicKey();
        var keyReady = publicKey is not null;
        var problem = (certificateReady, keyReady) switch
        {
            (false, _) => "O certificado da AT não está configurado ou não abre. " + certificates.Describe().Problem,
            (_, false) => "A chave pública do sistema de autenticação da AT não está configurada ou não abre. " + certificates.DescribePublicKey().Problem,
            _ => null,
        };
        return new AtGatewayStatus(certificateReady, keyReady, problem);
    }

    /// <summary>Envia um pedido SOAP à AT e lê a resposta; nunca lança por falhas de comunicação.</summary>
    /// <param name="operation">O nome da operação, para o cabeçalho <c>SOAPAction</c> (<c>tns:operação</c>).</param>
    public async Task<AtDeclarationResult> SendAsync(
        string endpoint, string operation, XElement body, IReadOnlyList<AtCredential> credentials, CancellationToken cancellationToken)
    {
        var raw = await PostAsync(
                endpoint, $"\"tns:{operation}\"",
                publicKey => VatReturnAtGateway.Envelope(body, credentials, publicKey, time.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
        return raw.Reached
            ? VatReturnAtGateway.Parse(raw.Content, raw.HttpStatus, raw.DurationMilliseconds)
            : new AtDeclarationResult(false, null, raw.Problem!, [], [], null, raw.HttpStatus, raw.DurationMilliseconds);
    }

    /// <summary>
    /// O transporte sem interpretar a resposta (os serviços cujo corpo não segue o formato das
    /// declarações, como a consulta de faturas). <paramref name="soapAction"/> vai tal como está
    /// no cabeçalho, com aspas. Nunca lança por falhas de comunicação.
    /// </summary>
    public async Task<AtRawResponse> PostAsync(
        string endpoint, string soapAction, Func<RSA, XDocument> buildEnvelope, CancellationToken cancellationToken)
    {
        var status = Status();
        if (!status.Ready)
        {
            return new AtRawResponse(false, null, string.Empty, status.Problem, 0);
        }

        if (!certificates.TryGetCertificate(out var certificate))
        {
            return new AtRawResponse(false, null, string.Empty, "O certificado da AT não abre.", 0);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var publicKey = certificates.LoadPublicKey()
                ?? throw new InvalidOperationException("A chave pública da AT não está disponível.");
            var envelope = buildEnvelope(publicKey);

            using var handler = new SocketsHttpHandler();
            handler.SslOptions.ClientCertificates = [certificate];
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
            request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new AtRawResponse(true, (int)response.StatusCode, content, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or CryptographicException or InvalidOperationException or IOException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // A mensagem útil está na exceção mais funda («Authentication failed, see inner exception» não diz nada).
            var root = exception;
            while (root.InnerException is not null)
            {
                root = root.InnerException;
            }

            var detail = root.Message;
            return new AtRawResponse(false, null, string.Empty, $"Não foi possível comunicar com a AT: {detail}", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            certificate.Dispose();
        }
    }
}

/// <summary>A resposta HTTP da AT, por interpretar. <paramref name="Problem"/> explica a falha quando <paramref name="Reached"/> é falso.</summary>
public sealed record AtRawResponse(bool Reached, int? HttpStatus, string Content, string? Problem, long DurationMilliseconds);
