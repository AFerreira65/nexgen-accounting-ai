using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed class CompanyAccountingProvisioner(AccountingDbContext context)
{
    public async Task ProvisionAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            await ProvisionExistingCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await ProvisionExistingCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<Company> CreateCompanyAsync(
        Guid id,
        Guid accountingFirmId,
        string legalName,
        string taxRegistrationNumber,
        TaxRegion taxRegion,
        AccountingStandard accountingStandard,
        string baseCurrency = "EUR",
        CancellationToken cancellationToken = default)
    {
        Company? company = null;
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            company = new Company(
                id,
                accountingFirmId,
                legalName,
                taxRegistrationNumber,
                taxRegion,
                baseCurrency,
                accountingStandard);
            context.Companies.Add(company);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await ProvisionCoreAsync(company, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return company!;
    }

    private async Task ProvisionExistingCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var company = await context.Companies
            .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            throw new InvalidOperationException("The company does not exist.");
        }

        await ProvisionCoreAsync(company, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProvisionCoreAsync(
        Company company,
        CancellationToken cancellationToken)
    {
        var template = AccountingTemplateCatalog.For(company.AccountingStandard);

        var accounts = await context.Accounts
            .Where(item => item.CompanyId == company.Id)
            .ToDictionaryAsync(item => item.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in template.Accounts.Where(item => !accounts.ContainsKey(item.Code)))
        {
            var account = new LedgerAccount(
                Guid.NewGuid(),
                company.Id,
                item.Code,
                item.Name,
                item.ParentCode,
                item.AllowsPosting,
                template.Version,
                item.TaxonomyCode is null ? null : template.TaxonomyReference,
                item.TaxonomyCode);
            context.Accounts.Add(account);
            accounts.Add(account.Code, account);
        }

        foreach (var item in template.Accounts.Where(item => item.TaxonomyCode is not null))
        {
            var account = accounts[item.Code];
            if (account.TaxonomyCode is null)
            {
                account.AssignTaxonomy(template.TaxonomyReference, item.TaxonomyCode);
            }
        }

        var journals = await context.Journals
            .Where(item => item.CompanyId == company.Id)
            .ToDictionaryAsync(item => item.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in template.Journals.Where(item => !journals.ContainsKey(item.Code)))
        {
            var journal = new AccountingJournal(
                Guid.NewGuid(),
                company.Id,
                item.Code,
                item.Name,
                template.Version);
            context.Journals.Add(journal);
            journals.Add(journal.Code, journal);
        }

        var documentCodes = await context.AccountingDocumentTypes
            .Where(item => item.CompanyId == company.Id)
            .Select(item => item.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in template.DocumentTypes.Where(item => !documentCodes.Contains(item.Code)))
        {
            context.AccountingDocumentTypes.Add(new AccountingDocumentType(
                Guid.NewGuid(),
                company.Id,
                journals[item.DefaultJournalCode].Id,
                item.Code,
                item.Name,
                template.Version));
        }

        if (!string.Equals(
                company.AccountingSetupVersion,
                template.Version,
                StringComparison.Ordinal))
        {
            company.MarkAccountingSetup(template.Version);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
