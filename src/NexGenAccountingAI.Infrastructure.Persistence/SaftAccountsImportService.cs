using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Cria na empresa as contas do plano geral de um SAF-T que ainda não existam,
/// por ordem hierárquica de código. A conta-mãe é o código existente mais longo
/// que seja prefixo; contas de 1 ou 2 dígitos ficam de integração, as restantes de
/// movimento (regra F1-47). Não altera as contas já existentes.
/// </summary>
public sealed class SaftAccountsImportService(ISaftAccountsReader accountsReader, AccountingDbContext context)
{
    public async Task<SaftAccountsImportResult?> ImportAsync(
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

        var existingCodes = (await context.Accounts.AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .Select(account => account.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toCreate = fileAccounts
            .Where(account => !existingCodes.Contains(account.Code))
            .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(account => account.Code.Length)
            .ThenBy(account => account.Code, StringComparer.Ordinal)
            .ToList();
        var alreadyExisted = fileAccounts
            .Select(account => account.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(existingCodes.Contains);

        if (toCreate.Count == 0)
        {
            return new SaftAccountsImportResult(0, alreadyExisted, []);
        }

        var known = new SortedSet<string>(existingCodes, StringComparer.Ordinal);
        var created = new List<SaftAccountCreated>(toCreate.Count);

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var account in toCreate)
            {
                var parentCode = known
                    .Where(code => account.Code.Length > code.Length &&
                        account.Code.StartsWith(code, StringComparison.Ordinal))
                    .OrderByDescending(code => code.Length)
                    .FirstOrDefault();
                var allowsPosting = account.Code.Trim().Length > 2;
                var name = string.IsNullOrWhiteSpace(account.Description) ? account.Code : account.Description;

                context.Accounts.Add(new LedgerAccount(
                    Guid.NewGuid(), companyId, account.Code, name, parentCode, allowsPosting));
                known.Add(account.Code);
                created.Add(new SaftAccountCreated(account.Code, name, parentCode, allowsPosting));
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (created.Count > 0)
        {
            await context.RefreshAfterImportAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SaftAccountsImportResult(created.Count, alreadyExisted, created);
    }
}
