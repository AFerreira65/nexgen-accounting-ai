using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// A entrega do Modelo 22 (F4C-06): valida o ficheiro XML no webservice da AT e submete-o,
/// com as credenciais do Portal das Finanças guardadas para a empresa. Cada pedido fica
/// registado em <see cref="AtExchange"/>. Como na DP de IVA, <b>submeter</b> só é permitido
/// no ambiente de testes da AT.
/// </summary>
public sealed class Modelo22FilingService(
    AccountingDbContext context,
    Modelo22Service modelo22,
    IModelo22AtGateway gateway,
    IAtSecretProtector protector,
    ICurrentUserAccessor currentUser)
{
    public const string ServiceName = "modelo22";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public Task<VatDeclarationAtOutcome?> ValidateAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default) =>
        SendAsync(userId, companyId, year, submit: false, acceptAlerts: false, cancellationToken);

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public Task<VatDeclarationAtOutcome?> SubmitAsync(
        Guid userId, Guid companyId, int year, bool acceptAlerts, CancellationToken cancellationToken = default) =>
        SendAsync(userId, companyId, year, submit: true, acceptAlerts, cancellationToken);

    private async Task<VatDeclarationAtOutcome?> SendAsync(
        Guid userId, Guid companyId, int year, bool submit, bool acceptAlerts, CancellationToken cancellationToken)
    {
        var file = await modelo22.BuildXmlAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        var credentials = await context.CompanyAtCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        var environment = (credentials?.Environment ?? AtEnvironment.Testes).ToString();
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.SubUser))
        {
            return new VatDeclarationAtOutcome(environment,
                "A empresa não tem as credenciais do Portal das Finanças guardadas. Indique o subutilizador e a senha em «Comunicação com a AT».", null);
        }

        var production = credentials.Environment == AtEnvironment.Producao;
        if (submit && production)
        {
            return new VatDeclarationAtOutcome(environment,
                "A submissão em produção ainda não está ativa nesta aplicação: só se submete no ambiente de testes da AT. Pode validar, e descarregar o ficheiro para o entregar no Portal das Finanças.", null);
        }

        var status = gateway.Status();
        if (!status.Ready)
        {
            return new VatDeclarationAtOutcome(environment, status.Problem, null);
        }

        var credential = new AtCredential(credentials.SubUser, protector.Unprotect(credentials.EncryptedPassword), AtActor.Taxpayer);
        var result = submit
            ? await gateway.SubmitAsync(file.Value.Content, [credential], production, acceptAlerts, cancellationToken).ConfigureAwait(false)
            : await gateway.ValidateAsync(file.Value.Content, [credential], production, cancellationToken).ConfigureAwait(false);

        context.ChangeTracker.Clear();
        context.AtExchanges.Add(new AtExchange(
            Guid.NewGuid(), companyId, ServiceName, submit ? "submeterDeclaracao" : "validarDeclaracao", production,
            year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow, currentUser.UserId ?? userId, result.DurationMilliseconds,
            result.Reached, result.HttpStatus, result.Code, result.Message, result.DeclarationId, result.Errors.Count, result.Alerts.Count));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new VatDeclarationAtOutcome(environment, null, result);
    }
}
