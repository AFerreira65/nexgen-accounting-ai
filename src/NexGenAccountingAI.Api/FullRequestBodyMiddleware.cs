using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

namespace NexGenAccountingAI.Api;

/// <summary>
/// Recebe um ficheiro XML inteiro antes de o endpoint o ler. Vários endpoints
/// (o cabeçalho, o plano de contas, os documentos de venda) só precisam de parte do ficheiro e respondem
/// logo, com o browser ainda a enviar o resto; diretamente o Kestrel aguenta, mas
/// um proxy (o do Vite, em desenvolvimento) corta a ligação a meio e o browser mostra
/// «Failed to fetch». O conteúdo fica num ficheiro temporário acima de 1 MB.
/// <para>
/// Só para pedidos autenticados (corre depois da autorização) com XML — os SAF-T de
/// contabilidade e de faturação e os extratos camt.053, cujos endpoints já aceitavam
/// ficheiros sem limite de tamanho. Os anexos do arquivo ficam de fora: têm o seu
/// limite de 15 MB, que tem de continuar a valer.
/// </para>
/// </summary>
public sealed class FullRequestBodyMiddleware(RequestDelegate next)
{
    private const int MemoryThreshold = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        if (HttpMethods.IsPost(request.Method) &&
            context.User.Identity?.IsAuthenticated == true &&
            request.ContentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true &&
            request.Path.Value?.Contains("/attachments", StringComparison.OrdinalIgnoreCase) != true)
        {
            var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeLimit is { IsReadOnly: false })
            {
                sizeLimit.MaxRequestBodySize = null;
            }

            request.EnableBuffering(MemoryThreshold);
            await request.Body.DrainAsync(context.RequestAborted).ConfigureAwait(false);
            request.Body.Position = 0;
        }

        await next(context).ConfigureAwait(false);
    }
}
