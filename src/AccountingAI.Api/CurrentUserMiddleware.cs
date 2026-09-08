using AccountingAI.Application.Auditing;

namespace AccountingAI.Api;

/// <summary>
/// Preenche o <see cref="ICurrentUserAccessor"/> do pedido com o <c>userId</c> da
/// rota, para o carimbo de auditoria dos registos. Corre depois do
/// encaminhamento (os valores da rota já estão disponíveis) e não exige nada:
/// um pedido sem <c>userId</c> segue com o utilizador por preencher.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUserAccessor currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetRouteValue("userId") is string raw && Guid.TryParse(raw, out var userId))
        {
            currentUser.UserId = userId;
        }

        await next(context).ConfigureAwait(false);
    }
}
