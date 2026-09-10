using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>Uma conta relevante para o IVA e a sua classificação (ou vazia).</summary>
public sealed record AccountVatClassificationRow(
    string AccountCode,
    string AccountName,
    string Class,
    string Rate,
    string Nature,
    string Region,
    bool Classified);

/// <summary>
/// Lê e escreve a <see cref="AccountVatClassification"/> das contas relevantes
/// (movimento das classes 6 e 7 e contas <c>243*</c>). Definir tudo «nenhuma»
/// apaga a linha. Só lê/escreve; não altera o plano de contas.
/// </summary>
public sealed class AccountVatClassificationService(AccountingDbContext context)
{
    public async Task<IReadOnlyList<AccountVatClassificationRow>?> ListAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.IsActive && account.AllowsPosting &&
                (account.Code.StartsWith("6") || account.Code.StartsWith("7") ||
                 account.Code.StartsWith("2432") || account.Code.StartsWith("2433") || account.Code.StartsWith("2434")))
            .Select(account => new { account.Id, account.Code, account.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var classifications = await context.AccountVatClassifications
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToDictionaryAsync(item => item.AccountId, cancellationToken)
            .ConfigureAwait(false);

        return accounts
            .OrderBy(account => account.Code, StringComparer.Ordinal)
            .Select(account =>
            {
                classifications.TryGetValue(account.Id, out var classification);
                return new AccountVatClassificationRow(
                    account.Code,
                    account.Name,
                    account.Code.Length > 0 ? account.Code[..1] : string.Empty,
                    (classification?.Rate ?? VatRate.Nenhuma).ToString(),
                    (classification?.Nature ?? VatOperationNature.Nenhuma).ToString(),
                    (classification?.Region ?? VatRegion.Continente).ToString(),
                    classification is not null);
            })
            .ToList();
    }

    /// <returns><c>false</c> sem acesso ou conta desconhecida; <c>true</c> caso contrário.</returns>
    public async Task<bool> SetAsync(
        Guid userId, Guid companyId, string accountCode,
        VatRate rate, VatOperationNature nature, VatRegion region, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var normalized = accountCode.Trim().ToUpperInvariant();
        var account = await context.Accounts
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            return false;
        }

        var row = await context.AccountVatClassifications
            .SingleOrDefaultAsync(item => item.AccountId == account.Id, cancellationToken)
            .ConfigureAwait(false);
        var empty = rate == VatRate.Nenhuma && nature == VatOperationNature.Nenhuma;

        if (row is null)
        {
            if (!empty)
            {
                context.AccountVatClassifications.Add(
                    AccountVatClassification.Create(companyId, account.Id, rate, nature, region));
            }
        }
        else if (empty)
        {
            context.AccountVatClassifications.Remove(row);
        }
        else
        {
            row.Update(rate, nature, region);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <returns>-1 sem acesso; senão o número de contas relevantes atingidas.</returns>
    public async Task<int> BulkSetAsync(
        Guid userId, Guid companyId, string prefix,
        VatRate rate, VatOperationNature nature, VatRegion region, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return -1;
        }

        var normalizedPrefix = prefix.Trim().ToUpperInvariant();
        var accountIds = await context.Accounts
            .Where(account => account.CompanyId == companyId && account.IsActive && account.AllowsPosting &&
                account.Code.StartsWith(normalizedPrefix) &&
                (account.Code.StartsWith("6") || account.Code.StartsWith("7") ||
                 account.Code.StartsWith("2432") || account.Code.StartsWith("2433") || account.Code.StartsWith("2434")))
            .Select(account => account.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (accountIds.Count == 0)
        {
            return 0;
        }

        var existing = await context.AccountVatClassifications
            .Where(item => accountIds.Contains(item.AccountId))
            .ToDictionaryAsync(item => item.AccountId, cancellationToken)
            .ConfigureAwait(false);
        var empty = rate == VatRate.Nenhuma && nature == VatOperationNature.Nenhuma;

        foreach (var accountId in accountIds)
        {
            existing.TryGetValue(accountId, out var row);
            if (row is null)
            {
                if (!empty)
                {
                    context.AccountVatClassifications.Add(
                        AccountVatClassification.Create(companyId, accountId, rate, nature, region));
                }
            }
            else if (empty)
            {
                context.AccountVatClassifications.Remove(row);
            }
            else
            {
                row.Update(rate, nature, region);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return accountIds.Count;
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
