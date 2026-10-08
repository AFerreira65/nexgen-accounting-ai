using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O resultado de pedir um rascunho: a recusa (IA desligada ou sem chave) ou o rascunho.</summary>
public sealed record ListingDraftOutcome(string? Refusal, ListingDraft? Draft);

/// <summary>
/// Cria uma listagem a partir de um pedido em português (F4D-05). Exige acesso à empresa, o
/// interruptor da IA ligado nessa empresa e uma chave configurada. Ao modelo só vai o pedido e o
/// catálogo de campos: nenhum dado da empresa. O rascunho nunca se guarda sozinho.
/// </summary>
public sealed class ListingDraftService(AccountingDbContext context, IListingDrafter drafter, TimeProvider time, AiGovernor? governor = null)
{
    public const int MaxRequestLength = 1000;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<ListingDraftOutcome?> DraftAsync(Guid userId, Guid companyId, string request, CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.Companies
            .AsNoTracking()
            .AnyAsync(company => company.Id == companyId && company.IsActive &&
                company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive), cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request))
        {
            return new ListingDraftOutcome("Escreva o que quer ver, por exemplo «vendas por cliente em 2026, por mês».", null);
        }

        if (request.Length > MaxRequestLength)
        {
            return new ListingDraftOutcome($"O pedido tem mais de {MaxRequestLength} caracteres: resuma-o.", null);
        }

        var enabled = await context.CompanyAiSettings
            .AsNoTracking()
            .AnyAsync(item => item.CompanyId == companyId && item.Enabled, cancellationToken)
            .ConfigureAwait(false);
        if (!enabled)
        {
            return new ListingDraftOutcome("A IA está desligada para esta empresa. Ligue-a nas definições da empresa para criar listagens a pedir em português.", null);
        }

        if (!drafter.IsConfigured)
        {
            return new ListingDraftOutcome("A IA não está configurada neste servidor (falta a chave da Anthropic).", null);
        }

        if (governor is not null && await governor.RefusalAsync(companyId, cancellationToken).ConfigureAwait(false) is { } limit)
        {
            return new ListingDraftOutcome(limit, null);
        }

        var draft = await drafter.DraftAsync(request.Trim(), DateOnly.FromDateTime(time.GetLocalNow().DateTime), cancellationToken).ConfigureAwait(false);
        if (governor is not null)
        {
            await governor.RecordAsync(companyId, userId, NexGenAccountingAI.Domain.Reporting.AiFeature.Listing, draft.Model, draft.PromptTokens, draft.OutputTokens, cancellationToken).ConfigureAwait(false);
        }

        return new ListingDraftOutcome(null, draft);
    }
}
