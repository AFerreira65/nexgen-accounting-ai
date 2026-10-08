using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O que a empresa decidiu sobre os seus dados na IA, com o protetor já carregado se pediu para esconder nomes.</summary>
/// <param name="Protector"><c>null</c> quando a empresa desligou o escondimento de nomes.</param>
public sealed record AiPrivacy(bool AnonymizeNames, int? ConversationRetentionDays, AiTextProtector? Protector);

/// <summary>
/// O que todas as funções de IA têm de fazer à volta de um pedido: ver se a empresa ainda tem saldo (<see cref="RefusalAsync"/>),
/// carregar as regras de privacidade da empresa (<see cref="PrivacyAsync"/>) e registar o consumo (<see cref="RecordAsync"/>).
/// </summary>
public sealed class AiGovernor(AccountingDbContext context, AiBudgetService budget)
{
    /// <summary>A mensagem a mostrar se a empresa já gastou o que podia, ou <c>null</c> se a IA pode correr.</summary>
    public Task<string?> RefusalAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        budget.RefusalAsync(companyId, cancellationToken);

    public Task RecordAsync(Guid companyId, Guid? userId, AiFeature feature, string model, int inputTokens, int outputTokens, CancellationToken cancellationToken = default) =>
        budget.RecordAsync(companyId, userId, feature, model, inputTokens, outputTokens, cancellationToken);

    /// <summary>
    /// As regras de privacidade da empresa. Por omissão (sem linha) escondem-se os nomes. Com o escondimento ligado, carrega
    /// o nome da empresa e os dos seus terceiros para o protetor os trocar por marcadores.
    /// </summary>
    public async Task<AiPrivacy> PrivacyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var settings = await context.CompanyAiSettings.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.AnonymizeNames, item.ConversationRetentionDays })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var anonymize = settings?.AnonymizeNames ?? true;
        if (!anonymize)
        {
            return new AiPrivacy(false, settings?.ConversationRetentionDays, null);
        }

        var companyName = await context.Companies.AsNoTracking().Where(company => company.Id == companyId).Select(company => company.LegalName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var parties = await context.Parties.AsNoTracking()
            .Where(party => party.CompanyId == companyId)
            .Select(party => party.Name)
            .Take(AiTextProtector.MaxThirdParties * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new AiPrivacy(true, settings?.ConversationRetentionDays, new AiTextProtector(companyName, parties));
    }
}
