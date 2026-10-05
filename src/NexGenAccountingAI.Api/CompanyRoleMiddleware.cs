using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Api;

/// <summary>
/// Marca um endpoint de escrita (POST/PUT/DELETE) que um utilizador com a função
/// «Consulta» pode chamar, porque não altera os dados da empresa: só calcula, pré-visualiza
/// ou grava as preferências do próprio utilizador. O motivo fica escrito no endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ViewerAllowedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>
/// A função «Consulta» (<see cref="CompanyUserRole.Viewer"/>) é só de leitura: um pedido
/// que não seja de leitura numa rota de empresa (<c>{userId}</c> e <c>{companyId}</c>)
/// é recusado com 403, salvo os endpoints marcados com <see cref="ViewerAllowedAttribute"/>.
/// Corre depois do <see cref="CurrentUserMiddleware"/>, que já garantiu que o
/// <c>userId</c> da rota é o da sessão. Ver ADR-0015.
/// </summary>
public sealed class CompanyRoleMiddleware(RequestDelegate next)
{
    public const string ReadOnlyMessage = "Tem acesso só de consulta a esta empresa: não pode fazer alterações. Peça ao dono ou a um administrador outra função.";

    public async Task InvokeAsync(HttpContext context, AccountingDbContext database)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(database);

        if (IsReadOnlyMethod(context.Request.Method) ||
            context.GetEndpoint()?.Metadata.GetMetadata<ViewerAllowedAttribute>() is not null ||
            context.GetRouteValue("userId") is not string rawUser || !Guid.TryParse(rawUser, out var userId) ||
            context.GetRouteValue("companyId") is not string rawCompany || !Guid.TryParse(rawCompany, out var companyId))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var isViewer = await database.CompanyUsers
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.CompanyId == companyId && item.IsActive && item.Role == CompanyUserRole.Viewer, context.RequestAborted)
            .ConfigureAwait(false);
        if (isViewer)
        {
            await Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["access"] = [ReadOnlyMessage] },
                    statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsReadOnlyMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
