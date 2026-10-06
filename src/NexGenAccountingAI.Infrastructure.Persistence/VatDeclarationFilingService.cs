using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O que se pede para gerar ou enviar a declaração de um período.</summary>
public sealed record VatDeclarationFilingRequest(
    DateOnly From, DateOnly To, decimal? PriorCredit, bool WithinDeadline, bool FiledRecapitulativeStatement);

/// <summary>O resultado de validar ou submeter a declaração na AT, com o que a aplicação sabe do pedido.</summary>
/// <param name="Environment">«Testes» ou «Producao»: o ambiente da AT usado.</param>
/// <param name="Refusal">Preenchido quando a aplicação não chegou a pedir nada à AT (falta de configuração, regra da aplicação).</param>
public sealed record VatDeclarationAtOutcome(string Environment, string? Refusal, AtDeclarationResult? Result);

public sealed record AtExchangeRow(
    string Service, string Operation, bool Production, string Period, DateTimeOffset RequestedAtUtc, string? RequestedByName,
    long DurationMilliseconds, bool Reached, int? ResultCode, string Message, long? DeclarationId, int ErrorCount, int AlertCount);

/// <summary>
/// A entrega da declaração periódica de IVA (F4A-02): gera o ficheiro XML oficial a
/// partir do quadro 06 que a aplicação apura, valida-o no webservice da AT e submete-o.
/// Autentica-se como o sujeito passivo, com o subutilizador e a senha guardados para a
/// empresa (a autenticação do contabilista certificado com plenos poderes fica para
/// depois). Cada pedido à AT fica registado em <see cref="AtExchange"/>.
/// <para>
/// <b>Submeter</b> só é permitido no ambiente de testes da AT: a submissão em
/// produção fica fechada até a aplicação aderir ao serviço com o certificado próprio
/// e a validação ter sido confirmada contra a AT.
/// </para>
/// </summary>
public sealed class VatDeclarationFilingService(
    AccountingDbContext context,
    VatDeclarationService declarations,
    IVatReturnAtGateway gateway,
    IAtSecretProtector protector,
    ICurrentUserAccessor currentUser)
{
    public const string ServiceName = "dpiva";

    /// <returns><c>null</c> sem acesso à empresa. Lança <see cref="InvalidOperationException"/> quando a declaração não pode ir para ficheiro.</returns>
    public async Task<(byte[] Content, string FileName)?> BuildXmlAsync(
        Guid userId, Guid companyId, VatDeclarationFilingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var declaration = await declarations.BuildAsync(userId, companyId, request.From, request.To, request.PriorCredit, cancellationToken).ConfigureAwait(false);
        if (declaration is null)
        {
            return null;
        }

        var taxNumber = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegistrationNumber)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        // O prazo, a recapitulativa e o contabilista vêm do que o utilizador preencheu no impresso (F4C-01).
        var inputs = declaration.Inputs ?? NexGenAccountingAI.Application.Fiscal.VatDeclarationInputs.Empty;
        var content = VatDeclarationXmlWriter.Write(
            declaration, new VatDeclarationFiling(taxNumber, inputs.CertifiedAccountantTaxNumber, inputs.WithinDeadline, inputs.FiledRecapitulativeStatement));
        return (content, $"dpiva-{taxNumber}-{request.To.Year}-{declaration.Period}.xml");
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public Task<VatDeclarationAtOutcome?> ValidateAsync(
        Guid userId, Guid companyId, VatDeclarationFilingRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(userId, companyId, request, submit: false, acceptAlerts: false, cancellationToken);

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public Task<VatDeclarationAtOutcome?> SubmitAsync(
        Guid userId, Guid companyId, VatDeclarationFilingRequest request, bool acceptAlerts, CancellationToken cancellationToken = default) =>
        SendAsync(userId, companyId, request, submit: true, acceptAlerts, cancellationToken);

    /// <summary>Os últimos pedidos à AT desta empresa. <c>null</c> sem acesso.</summary>
    public async Task<IReadOnlyList<AtExchangeRow>?> RecentAsync(Guid userId, Guid companyId, int take, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await context.AtExchanges
            .AsNoTracking()
            .Where(exchange => exchange.CompanyId == companyId)
            .OrderByDescending(exchange => exchange.RequestedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var userIds = rows.Where(row => row.RequestedBy is not null).Select(row => row.RequestedBy!.Value).Distinct().ToList();
        var users = await context.UserAccounts
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => new AtExchangeRow(
            row.Service, row.Operation, row.Production, row.Period, row.RequestedAtUtc,
            row.RequestedBy is { } id ? users.GetValueOrDefault(id) : null,
            row.DurationMilliseconds, row.Reached, row.ResultCode, row.Message, row.DeclarationId, row.ErrorCount, row.AlertCount)).ToList();
    }

    private async Task<VatDeclarationAtOutcome?> SendAsync(
        Guid userId, Guid companyId, VatDeclarationFilingRequest request, bool submit, bool acceptAlerts, CancellationToken cancellationToken)
    {
        var file = await BuildXmlAsync(userId, companyId, request, cancellationToken).ConfigureAwait(false);
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
            $"{request.To.Year}-{VatDeclarationService.PeriodOf(request.From, request.To)}",
            DateTimeOffset.UtcNow, currentUser.UserId ?? userId, result.DurationMilliseconds,
            result.Reached, result.HttpStatus, result.Code, result.Message, result.DeclarationId, result.Errors.Count, result.Alerts.Count));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new VatDeclarationAtOutcome(environment, null, result);
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.CompanyId == companyId && item.IsActive && item.Company.IsActive, cancellationToken);
}
