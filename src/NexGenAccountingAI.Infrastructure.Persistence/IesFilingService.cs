using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// As credenciais do Portal das Finanças que a IES pede em cada entrega. O manual da AT manda
/// pedi-las ao contabilista certificado sempre que são precisas: <b>não se guardam</b>.
/// </summary>
/// <param name="AccountantUser">O utilizador do contabilista certificado (NIF ou NIF/subutilizador).</param>
/// <param name="AccountantPassword">A senha do contabilista.</param>
/// <param name="TaxpayerPassword">A senha do sujeito passivo; sem ela, o contabilista tem de ter plenos poderes declarativos e basta o NIF da empresa.</param>
public sealed record IesAtCredentials(string AccountantUser, string AccountantPassword, string? TaxpayerPassword);

/// <summary>
/// A entrega da IES (F4C-07): valida o ficheiro XML no webservice da AT e submete-o. Cada
/// pedido fica registado em <see cref="AtExchange"/>, sem senhas. Como nos outros serviços,
/// <b>submeter</b> só é permitido no ambiente de testes da AT.
/// </summary>
public sealed class IesFilingService(
    AccountingDbContext context,
    IesAnnexAService annex,
    IIesAtGateway gateway,
    ICurrentUserAccessor currentUser)
{
    public const string ServiceName = "ies";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public Task<VatDeclarationAtOutcome?> ValidateAsync(
        Guid userId, Guid companyId, int year, IesAtCredentials credentials, CancellationToken cancellationToken = default) =>
        SendAsync(userId, companyId, year, credentials, submit: false, cancellationToken);

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public Task<VatDeclarationAtOutcome?> SubmitAsync(
        Guid userId, Guid companyId, int year, IesAtCredentials credentials, CancellationToken cancellationToken = default) =>
        SendAsync(userId, companyId, year, credentials, submit: true, cancellationToken);

    private async Task<VatDeclarationAtOutcome?> SendAsync(
        Guid userId, Guid companyId, int year, IesAtCredentials credentials, bool submit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var file = await annex.BuildXmlAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        var stored = await context.CompanyAtCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        var environment = (stored?.Environment ?? AtEnvironment.Testes).ToString();
        var production = stored?.Environment == AtEnvironment.Producao;

        if (string.IsNullOrWhiteSpace(credentials.AccountantUser) || string.IsNullOrEmpty(credentials.AccountantPassword))
        {
            return new VatDeclarationAtOutcome(environment,
                "A IES é entregue pelo contabilista certificado: indique o utilizador e a senha do Portal das Finanças dele. Não ficam guardados.", null);
        }

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

        var taxNumber = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegistrationNumber)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        // O sujeito passivo: o NIF, com a senha se o contabilista não tem plenos poderes.
        IReadOnlyList<AtCredential> headers =
        [
            new AtCredential(credentials.AccountantUser.Trim(), credentials.AccountantPassword, AtActor.CertifiedAccountant),
            new AtCredential(taxNumber, string.IsNullOrEmpty(credentials.TaxpayerPassword) ? null : credentials.TaxpayerPassword, AtActor.Taxpayer),
        ];
        var result = submit
            ? await gateway.SubmitAsync(file.Value.Content, headers, production, cancellationToken).ConfigureAwait(false)
            : await gateway.ValidateAsync(file.Value.Content, headers, production, cancellationToken).ConfigureAwait(false);

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
