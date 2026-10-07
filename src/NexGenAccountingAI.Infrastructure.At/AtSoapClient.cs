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
        var certificateReady = certificates.TryGetCertificate(out var certificate);
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
        var status = Status();
        if (!status.Ready)
        {
            return new AtDeclarationResult(false, null, status.Problem!, [], [], null, null, 0);
        }

        if (!certificates.TryGetCertificate(out var certificate))
        {
            return new AtDeclarationResult(false, null, "O certificado da AT não abre.", [], [], null, null, 0);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var publicKey = certificates.LoadPublicKey()
                ?? throw new InvalidOperationException("A chave pública da AT não está disponível.");
            var envelope = VatReturnAtGateway.Envelope(body, credentials, publicKey, time.GetUtcNow());

            using var handler = new SocketsHttpHandler();
            handler.SslOptions.ClientCertificates = [certificate];
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"tns:{operation}\"");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return VatReturnAtGateway.Parse(content, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or CryptographicException or InvalidOperationException or IOException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var detail = exception.InnerException?.Message ?? exception.Message;
            return new AtDeclarationResult(false, null, $"Não foi possível comunicar com a AT: {detail}", [], [], null, null, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            certificate.Dispose();
        }
    }

}
