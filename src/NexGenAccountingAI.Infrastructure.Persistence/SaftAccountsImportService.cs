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

        // As contas que já existem sem taxonomia recebem a do ficheiro (as que já têm uma
        // ficam como estão): um plano importado antes de a importação guardar a taxonomia,
        // ou o plano base da empresa, ficam prontos para as demonstrações e para exportar.
        var taxonomyByCode = fileAccounts
            .Where(account => account.TaxonomyCode is not null)
            .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var taxonomyAssigned = 0;
        if (taxonomyByCode.Count > 0)
        {
            var unclassified = await context.Accounts
                .Where(account => account.CompanyId == companyId && account.AllowsPosting && account.TaxonomyCode == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var existing in unclassified)
            {
                if (taxonomyByCode.TryGetValue(existing.Code, out var fileAccount) && ApplyTaxonomy(existing, fileAccount))
                {
                    taxonomyAssigned++;
                }
            }

            if (taxonomyAssigned > 0)
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (toCreate.Count == 0)
        {
            return new SaftAccountsImportResult(0, alreadyExisted, [], taxonomyAssigned);
        }

        var known = new HashSet<string>(existingCodes, StringComparer.Ordinal);
        var created = new List<SaftAccountCreated>(toCreate.Count);

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var account in toCreate)
            {
                // A conta-mãe é o prefixo mais comprido que já existe. Procura-se por prefixos
                // (percorrer todas as contas conhecidas para cada uma levava perto de um minuto
                // num plano de 44 mil contas).
                string? parentCode = null;
                for (var length = account.Code.Length - 1; length >= 1 && parentCode is null; length--)
                {
                    var prefix = account.Code[..length];
                    if (known.Contains(prefix))
                    {
                        parentCode = prefix;
                    }
                }

                // De movimento é a que o ficheiro diz que é (GroupingCategory «GM»); as «GR» e
                // «GA» são agregadoras. Sem categoria no ficheiro, vale a regra do plano base:
                // 3 ou mais dígitos.
                var category = account.GroupingCategory?.Trim();
                var allowsPosting = string.IsNullOrEmpty(category)
                    ? account.Code.Trim().Length > 2
                    : string.Equals(category, "GM", StringComparison.OrdinalIgnoreCase);
                var name = string.IsNullOrWhiteSpace(account.Description) ? account.Code : account.Description;

                var ledgerAccount = new LedgerAccount(
                    Guid.NewGuid(), companyId, account.Code, name, parentCode, allowsPosting);
                ApplyTaxonomy(ledgerAccount, account);
                context.Accounts.Add(ledgerAccount);
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

        return new SaftAccountsImportResult(created.Count, alreadyExisted, created, taxonomyAssigned);
    }

    /// <summary>Dá à conta de movimento a taxonomia que o ficheiro indica; ignora uma combinação inválida.</summary>
    private static bool ApplyTaxonomy(LedgerAccount account, SaftAccount fileAccount)
    {
        if (!account.AllowsPosting || fileAccount.TaxonomyCode is not { } code || fileAccount.TaxonomyReference is not { } reference)
        {
            return false;
        }

        try
        {
            account.AssignTaxonomy(reference, code);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
