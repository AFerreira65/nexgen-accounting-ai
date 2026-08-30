using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record AccountMasterData(
    string Code,
    string Name,
    string? ParentCode,
    bool AllowsPosting,
    string? TaxonomyReference,
    int? TaxonomyCode,
    bool IsActive,
    string? SourceTemplateVersion);

public sealed record JournalMasterData(
    string Code,
    string Name,
    bool IsActive,
    string? SourceTemplateVersion);

public sealed record DocumentTypeMasterData(
    string Code,
    string Name,
    string DefaultJournalCode,
    bool IsActive,
    string? SourceTemplateVersion);

/// <summary>
/// Manutenção dos cadastros contabilísticos próprios da empresa — contas, diários e
/// tipos de documento: criação, renomeação e ativação/desativação, além da
/// classificação de taxonomia nas contas. Todas as operações são filtradas por
/// <c>CompanyId</c> e transacionais.
/// </summary>
public sealed class AccountingMasterDataService(AccountingDbContext context)
{
    public async Task<AccountMasterData> CreateAccountAsync(
        Guid companyId,
        string code,
        string name,
        string? parentCode,
        bool allowsPosting,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        AccountMasterData? result = null;

        await ExecuteInTransactionAsync(async () =>
        {
            var companyExists = await context.Companies
                .AnyAsync(item => item.Id == companyId, cancellationToken)
                .ConfigureAwait(false);
            if (!companyExists)
            {
                throw new InvalidOperationException("A empresa não existe.");
            }

            var codeInUse = await context.Accounts
                .AnyAsync(item => item.CompanyId == companyId && item.Code == normalizedCode, cancellationToken)
                .ConfigureAwait(false);
            if (codeInUse)
            {
                throw new InvalidOperationException("Já existe uma conta com esse código na empresa.");
            }

            string? normalizedParent = null;
            if (!string.IsNullOrWhiteSpace(parentCode))
            {
                normalizedParent = NormalizeCode(parentCode);
                var parent = await context.Accounts
                    .SingleOrDefaultAsync(
                        item => item.CompanyId == companyId && item.Code == normalizedParent,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (parent is null)
                {
                    throw new ArgumentException("A conta agregadora indicada não existe.", nameof(parentCode));
                }

                if (parent.AllowsPosting)
                {
                    throw new ArgumentException("A conta-mãe tem de ser agregadora.", nameof(parentCode));
                }
            }

            var account = new LedgerAccount(
                Guid.NewGuid(),
                companyId,
                normalizedCode,
                name,
                normalizedParent,
                allowsPosting);
            context.Accounts.Add(account);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(account);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public async Task<AccountMasterData> UpdateAccountAsync(
        Guid companyId,
        string code,
        string name,
        bool isActive,
        string? taxonomyReference,
        int? taxonomyCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        AccountMasterData? result = null;

        await ExecuteInTransactionAsync(async () =>
        {
            var account = await context.Accounts
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Code == normalizedCode,
                    cancellationToken)
                .ConfigureAwait(false);
            if (account is null)
            {
                throw new InvalidOperationException("A conta não existe.");
            }

            account.Rename(name);
            account.AssignTaxonomy(taxonomyReference, taxonomyCode);

            if (!isActive && account.IsActive)
            {
                var hasEntries = await context.JournalEntryLines
                    .AnyAsync(
                        item => item.CompanyId == companyId && item.AccountId == account.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (hasEntries)
                {
                    throw new InvalidOperationException("A conta tem movimentos e não pode ser desativada.");
                }

                var hasActiveChildren = await context.Accounts
                    .AnyAsync(
                        item => item.CompanyId == companyId &&
                            item.ParentCode == account.Code &&
                            item.IsActive,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (hasActiveChildren)
                {
                    throw new InvalidOperationException("A conta tem subcontas ativas.");
                }

                account.Deactivate();
            }
            else if (isActive && !account.IsActive)
            {
                if (account.ParentCode is not null)
                {
                    var parentActive = await context.Accounts
                        .AnyAsync(
                            item => item.CompanyId == companyId &&
                                item.Code == account.ParentCode &&
                                item.IsActive,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!parentActive)
                    {
                        throw new InvalidOperationException("A conta-mãe está inativa.");
                    }
                }

                account.Reactivate();
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(account);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    /// <summary>
    /// Reaplica as correspondências de taxonomia inequívocas do referencial da empresa
    /// às contas de movimento ativas que ainda não têm código. É idempotente: uma segunda
    /// execução sem contas novas devolve 0.
    /// </summary>
    public async Task<int> ApplyTemplateTaxonomySuggestionsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var applied = 0;

        await ExecuteInTransactionAsync(async () =>
        {
            applied = 0; // reset for a clean count if the execution strategy retries

            var company = await context.Companies
                .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken)
                .ConfigureAwait(false);
            if (company is null)
            {
                throw new InvalidOperationException("A empresa não existe.");
            }

            var reference = AccountingTemplateCatalog.For(company.AccountingStandard).TaxonomyReference;

            var accounts = await context.Accounts
                .Where(item => item.CompanyId == companyId && item.AllowsPosting && item.IsActive)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var account in accounts)
            {
                if (account.TaxonomyCode is not null)
                {
                    continue;
                }

                var suggestion = TaxonomyCatalog.SuggestFor(reference, account.Code);
                if (suggestion is not null)
                {
                    account.AssignTaxonomy(reference, suggestion);
                    applied++;
                }
            }

            if (applied > 0)
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        return applied;
    }

    public async Task<JournalMasterData> CreateJournalAsync(
        Guid companyId,
        string code,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        JournalMasterData? result = null;

        await ExecuteInTransactionAsync(async () =>
        {
            var companyExists = await context.Companies
                .AnyAsync(item => item.Id == companyId, cancellationToken)
                .ConfigureAwait(false);
            if (!companyExists)
            {
                throw new InvalidOperationException("A empresa não existe.");
            }

            var codeInUse = await context.Journals
                .AnyAsync(item => item.CompanyId == companyId && item.Code == normalizedCode, cancellationToken)
                .ConfigureAwait(false);
            if (codeInUse)
            {
                throw new InvalidOperationException("Já existe um diário com esse código na empresa.");
            }

            var journal = new AccountingJournal(Guid.NewGuid(), companyId, normalizedCode, name);
            context.Journals.Add(journal);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(journal);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public async Task<JournalMasterData> UpdateJournalAsync(
        Guid companyId,
        string code,
        string name,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        JournalMasterData? result = null;

        await ExecuteInTransactionAsync(async () =>
        {
            var journal = await context.Journals
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Code == normalizedCode,
                    cancellationToken)
                .ConfigureAwait(false);
            if (journal is null)
            {
                throw new InvalidOperationException("O diário não existe.");
            }

            journal.Rename(name);

            if (!isActive && journal.IsActive)
            {
                var hasEntries = await context.JournalEntries
                    .AnyAsync(
                        item => item.CompanyId == companyId && item.JournalId == journal.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (hasEntries)
                {
                    throw new InvalidOperationException("O diário tem movimentos e não pode ser desativado.");
                }

                var usedByDocumentType = await context.AccountingDocumentTypes
                    .AnyAsync(
                        item => item.CompanyId == companyId &&
                            item.DefaultJournalId == journal.Id &&
                            item.IsActive,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (usedByDocumentType)
                {
                    throw new InvalidOperationException("O diário está definido como predefinido num tipo de documento ativo.");
                }

                journal.Deactivate();
            }
            else if (isActive && !journal.IsActive)
            {
                journal.Reactivate();
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = Project(journal);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public async Task<DocumentTypeMasterData> CreateDocumentTypeAsync(
        Guid companyId,
        string code,
        string name,
        string defaultJournalCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedJournalCode = NormalizeCode(defaultJournalCode);
        DocumentTypeMasterData? result = null;

        await ExecuteInTransactionAsync(async () =>
        {
            var companyExists = await context.Companies
                .AnyAsync(item => item.Id == companyId, cancellationToken)
                .ConfigureAwait(false);
            if (!companyExists)
            {
                throw new InvalidOperationException("A empresa não existe.");
            }

            var codeInUse = await context.AccountingDocumentTypes
                .AnyAsync(item => item.CompanyId == companyId && item.Code == normalizedCode, cancellationToken)
                .ConfigureAwait(false);
            if (codeInUse)
            {
                throw new InvalidOperationException("Já existe um tipo de documento com esse código na empresa.");
            }

            var journal = await ResolveActiveJournalAsync(companyId, normalizedJournalCode, cancellationToken)
                .ConfigureAwait(false);

            var documentType = new AccountingDocumentType(
                Guid.NewGuid(),
                companyId,
                journal.Id,
                normalizedCode,
                name);
            context.AccountingDocumentTypes.Add(documentType);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = new DocumentTypeMasterData(
                documentType.Code,
                documentType.Name,
                journal.Code,
                documentType.IsActive,
                documentType.SourceTemplateVersion);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public async Task<DocumentTypeMasterData> UpdateDocumentTypeAsync(
        Guid companyId,
        string code,
        string name,
        string defaultJournalCode,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedJournalCode = NormalizeCode(defaultJournalCode);
        DocumentTypeMasterData? result = null;

        await ExecuteInTransactionAsync(async () =>
        {
            var documentType = await context.AccountingDocumentTypes
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Code == normalizedCode,
                    cancellationToken)
                .ConfigureAwait(false);
            if (documentType is null)
            {
                throw new InvalidOperationException("O tipo de documento não existe.");
            }

            var journal = await ResolveActiveJournalAsync(companyId, normalizedJournalCode, cancellationToken)
                .ConfigureAwait(false);

            documentType.Rename(name);
            documentType.ChangeDefaultJournal(journal.Id);

            if (!isActive && documentType.IsActive)
            {
                documentType.Deactivate();
            }
            else if (isActive && !documentType.IsActive)
            {
                documentType.Reactivate();
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = new DocumentTypeMasterData(
                documentType.Code,
                documentType.Name,
                journal.Code,
                documentType.IsActive,
                documentType.SourceTemplateVersion);
        }, cancellationToken).ConfigureAwait(false);

        return result!;
    }

    private async Task<AccountingJournal> ResolveActiveJournalAsync(
        Guid companyId,
        string journalCode,
        CancellationToken cancellationToken)
    {
        var journal = await context.Journals
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.Code == journalCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (journal is null)
        {
            throw new ArgumentException("O diário predefinido indicado não existe na empresa.", nameof(journalCode));
        }

        if (!journal.IsActive)
        {
            throw new ArgumentException("O diário predefinido indicado está inativo.", nameof(journalCode));
        }

        return journal;
    }

    private async Task ExecuteInTransactionAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            await body().ConfigureAwait(false);
            return;
        }

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await body().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static string NormalizeCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return code.Trim().ToUpperInvariant();
    }

    private static AccountMasterData Project(LedgerAccount account) => new(
        account.Code,
        account.Name,
        account.ParentCode,
        account.AllowsPosting,
        account.TaxonomyReference,
        account.TaxonomyCode,
        account.IsActive,
        account.SourceTemplateVersion);

    private static JournalMasterData Project(AccountingJournal journal) => new(
        journal.Code,
        journal.Name,
        journal.IsActive,
        journal.SourceTemplateVersion);
}
