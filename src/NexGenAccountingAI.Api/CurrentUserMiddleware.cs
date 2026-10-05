using System.Security.Claims;
using NexGenAccountingAI.Application.Auditing;

namespace NexGenAccountingAI.Api;

/// <summary>
/// Preenche o <see cref="ICurrentUserAccessor"/> do pedido com o <c>userId</c> da
/// rota, para o carimbo de auditoria dos registos, e garante que esse
/// <c>userId</c> é o da sessão autenticada — sem isto, um utilizador com sessão
/// válida podia agir em nome de outro só trocando o <c>userId</c> no URL. Corre
/// depois de <c>UseAuthorization</c> (ver ADR-0011): a essa altura, qualquer
/// pedido que chegue aqui com um endpoint que exija autenticação já está
/// autenticado — só os endpoints com <c>[AllowAnonymous]</c> (nenhum deles tem
/// <c>userId</c> na rota) podiam não estar.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUserAccessor currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetRouteValue("userId") is string raw && Guid.TryParse(raw, out var userId))
        {
            var sessionUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(sessionUserId, out var sessionId) || sessionId != userId)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            currentUser.UserId = userId;
        }
        else if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var sessionUserId))
        {
            // Sem `userId` na rota (o cadastro organizacional, por exemplo), o
            // carimbo de auditoria fica com o utilizador da sessão.
            currentUser.UserId = sessionUserId;
        }

        await next(context).ConfigureAwait(false);
    }
}
