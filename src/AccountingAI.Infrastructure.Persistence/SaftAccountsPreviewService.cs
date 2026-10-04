using AccountingAI.Application.Saft;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>
/// Reconcilia o plano de contas de um ficheiro SAF-T com o plano de uma empresa a
/// que o utilizador tem acesso. Só classifica cada conta do ficheiro como já
/// existente ou nova; não cria nem altera nada.
/// </summary>
public sealed class SaftAccountsPreviewService(ISaftAccountsReader accountsReader, AccountingDbContext context)
{
    public async Task<SaftAccountsReconciliation?> PreviewAsync(
        Guid userId,
        Guid companyId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId &&
                    item.CompanyId == companyId &&
                    item.IsActive &&
                    item.Company.IsActive,
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var fileAccounts = await accountsReader.ReadAsync(content, cancellationToken).ConfigureAwait(false);

        var companyCodes = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .Select(account => account.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var companyCodeSet = companyCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var postingCount = await context.Accounts
            .AsNoTracking()
            .CountAsync(account => account.CompanyId == companyId && account.AllowsPosting, cancellationToken)
            .ConfigureAwait(false);

        var statuses = fileAccounts
            .Select(account => new SaftAccountStatus(
                account.Code,
                account.Description,
                companyCodeSet.Contains(account.Code) ? SaftAccountState.Existing : SaftAccountState.New))
            .OrderBy(account => account.Code, StringComparer.Ordinal)
            .ToList();

        return new SaftAccountsReconciliation(
            statuses.Count,
            statuses.Count(status => status.State == SaftAccountState.Existing),
            statuses.Count(status => status.State == SaftAccountState.New),
            postingCount,
            statuses);
    }
}
