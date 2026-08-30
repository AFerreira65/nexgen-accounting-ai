using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public enum TaxonomyReviewStatus
{
    /// <summary>Conta de movimento com referencial e código coerentes com a norma.</summary>
    Assigned,

    /// <summary>Sem código, mas o plano base do referencial tem uma correspondência inequívoca.</summary>
    SuggestionAvailable,

    /// <summary>Conta de movimento por classificar e sem sugestão do plano base.</summary>
    Unclassified,

    /// <summary>Tem código, mas o referencial da conta difere do referencial da norma da empresa.</summary>
    ReferenceMismatch
}

public sealed record TaxonomyReviewAccount(
    string Code,
    string Name,
    string? TaxonomyReference,
    int? TaxonomyCode,
    int? SuggestedCode,
    TaxonomyReviewStatus Status);

public sealed record TaxonomyReview(
    string Reference,
    string TaxonomyName,
    string TaxonomyLegalSource,
    int PostingAccountCount,
    int ClassifiedCount,
    IReadOnlyList<TaxonomyReviewAccount> Accounts);

/// <summary>
/// Constrói a cobertura de taxonomia das contas de movimento de uma empresa a partir
/// da norma escolhida e do plano de contas base versionado. Não inventa classificações:
/// só assinala as contas por classificar e as correspondências inequívocas já incluídas
/// no catálogo inicial (<see cref="AccountingTemplateCatalog"/>).
/// </summary>
public sealed class TaxonomyReviewService(AccountingDbContext context)
{
    public async Task<TaxonomyReview?> BuildAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var template = AccountingTemplateCatalog.For(company.AccountingStandard);

        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.AllowsPosting && item.IsActive)
            .OrderBy(item => item.Code)
            .Select(item => new
            {
                item.Code,
                item.Name,
                item.TaxonomyReference,
                item.TaxonomyCode
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = accounts.Select(item =>
        {
            var suggestion = TaxonomyCatalog.SuggestFor(template.TaxonomyReference, item.Code);

            var status = item.TaxonomyCode is not null
                ? (string.Equals(item.TaxonomyReference, template.TaxonomyReference, StringComparison.Ordinal)
                    ? TaxonomyReviewStatus.Assigned
                    : TaxonomyReviewStatus.ReferenceMismatch)
                : suggestion is not null
                    ? TaxonomyReviewStatus.SuggestionAvailable
                    : TaxonomyReviewStatus.Unclassified;

            return new TaxonomyReviewAccount(
                item.Code,
                item.Name,
                item.TaxonomyReference,
                item.TaxonomyCode,
                suggestion,
                status);
        }).ToList();

        return new TaxonomyReview(
            template.TaxonomyReference,
            template.TaxonomyName,
            template.TaxonomyLegalSource,
            lines.Count,
            lines.Count(item => item.TaxonomyCode is not null),
            lines);
    }
}
