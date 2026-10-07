using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Application.Efatura;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// O resultado de importar as compras da AT.
/// </summary>
/// <param name="Environment">«Testes» ou «Producao»: o ambiente da AT usado.</param>
/// <param name="Problem">Preenchido quando nada foi importado: falta de configuração, regra da aplicação ou recusa da AT.</param>
/// <param name="ReachedAt">Se a AT chegou a responder.</param>
/// <param name="Fetched">Os documentos que a AT devolveu.</param>
/// <param name="Import">O que se gravou (ou gravaria, numa pré-visualização).</param>
public sealed record EFaturaAtImportOutcome(
    string Environment, string? Problem, bool ReachedAt, int Fetched, EFaturaImportResult? Import);

/// <summary>
/// Importa as compras do e-Fatura diretamente da AT (serviço de consulta de faturas, como
/// adquirente), em vez de um CSV do Portal. Usa o subutilizador e a senha guardados para a empresa
/// (<see cref="CompanyAtCredentials"/>), que têm de ser do NIF da própria empresa e ter permissão
/// de consulta (WFA). A gravação é a do CSV (<see cref="EFaturaPurchaseImportService.StoreAsync"/>):
/// idempotente por <c>NIF|nº|data</c>. Cada pedido fica registado em <see cref="AtExchange"/>, sem
/// senhas. A consulta só lê: é permitida em produção.
/// </summary>
public sealed class EFaturaAtImportService(
    AccountingDbContext context,
    EFaturaPurchaseImportService store,
    IEFaturaAtGateway gateway,
    IAtSecretProtector protector,
    ICurrentUserAccessor currentUser)
{
    public const string ServiceName = "efatura";
    public const string SourceName = "AT (webservice e-Fatura)";

    /// <summary>A AT limita cada pedido a um mês; mais do que isto seriam dezenas de pedidos numa só chamada.</summary>
    public const int MaxDays = 400;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<EFaturaAtImportOutcome?> ImportAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, bool dryRun, CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.CompanyId == companyId && item.IsActive && item.Company.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var credentials = await context.CompanyAtCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        var environment = (credentials?.Environment ?? AtEnvironment.Testes).ToString();
        EFaturaAtImportOutcome Refuse(string problem) => new(environment, problem, false, 0, null);

        if (to < from)
        {
            return Refuse("A data final não pode ser anterior à inicial.");
        }

        if (to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Refuse($"Peça no máximo {MaxDays} dias de cada vez: a AT responde a um mês por pedido.");
        }

        if (credentials is null || string.IsNullOrWhiteSpace(credentials.SubUser))
        {
            return Refuse("A empresa não tem as credenciais do Portal das Finanças guardadas. Indique o subutilizador (com permissão de consulta de faturas, WFA) e a senha em «Comunicação com a AT».");
        }

        var taxNumber = (await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegistrationNumber)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false)).Trim();
        var loginTaxNumber = credentials.SubUser.Split('/', 2)[0].Trim();
        if (!string.Equals(loginTaxNumber, taxNumber, StringComparison.Ordinal))
        {
            return Refuse($"O subutilizador guardado é do NIF {loginTaxNumber}, não do da empresa ({taxNumber}). A AT só entrega as faturas do próprio NIF do utilizador.");
        }

        var status = gateway.Status();
        if (!status.Ready)
        {
            return Refuse(status.Problem ?? "A comunicação com a AT não está configurada.");
        }

        var production = credentials.Environment == AtEnvironment.Producao;
        var fetched = await gateway
            .FetchPurchasesAsync(from, to, new AtCredential(credentials.SubUser, protector.Unprotect(credentials.EncryptedPassword), AtActor.Taxpayer),
                taxNumber, production, cancellationToken)
            .ConfigureAwait(false);

        context.ChangeTracker.Clear();
        context.AtExchanges.Add(new AtExchange(
            Guid.NewGuid(), companyId, ServiceName, "consultarFaturas", production,
            $"{from:yyyyMMdd}-{to:yyyyMMdd}",
            DateTimeOffset.UtcNow, currentUser.UserId ?? userId, fetched.DurationMilliseconds,
            fetched.Reached, fetched.HttpStatus, fetched.Code, fetched.Message, null, 0, 0));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!fetched.Succeeded)
        {
            return new EFaturaAtImportOutcome(environment, fetched.Message, fetched.Reached, fetched.Invoices.Count, null);
        }

        var names = await KnownNamesAsync(companyId, fetched.Invoices.Select(item => item.SupplierTaxId).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);
        var candidates = fetched.Invoices
            .Select(item => new EFaturaPurchaseCandidate(
                item.SupplierTaxId,
                // A AT não diz o nome do emitente: usa o que já se conhece, ou o NIF.
                names.GetValueOrDefault(item.SupplierTaxId) ?? $"NIF {item.SupplierTaxId}",
                item.InvoiceNo, item.Atcud, item.InvoiceDate, item.InvoiceType, null, item.Sector,
                item.NetTotal, item.TaxPayable, item.GrossTotal))
            .ToList();
        var import = await store
            .StoreAsync(companyId, candidates, SourceName, dryRun, 0, [], fetched.Warnings, cancellationToken)
            .ConfigureAwait(false);
        return new EFaturaAtImportOutcome(environment, null, true, fetched.Invoices.Count, import);
    }

    /// <summary>Os nomes já conhecidos destes NIF: das compras anteriores e do cadastro de terceiros.</summary>
    private async Task<Dictionary<string, string>> KnownNamesAsync(Guid companyId, List<string> taxIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (taxIds.Count == 0)
        {
            return names;
        }

        var parties = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && taxIds.Contains(party.TaxRegistrationNumber) && party.Name != "")
            .Select(party => new { Id = party.TaxRegistrationNumber, party.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var party in parties)
        {
            names.TryAdd(party.Id, party.Name);
        }

        var previous = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && taxIds.Contains(item.SupplierTaxId) && item.SupplierName != "" && !item.SupplierName.StartsWith("NIF "))
            .Select(item => new { Id = item.SupplierTaxId, Name = item.SupplierName })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in previous)
        {
            names.TryAdd(item.Id, item.Name);
        }

        return names;
    }
}
