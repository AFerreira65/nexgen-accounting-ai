using AccountingAI.Application.Saft;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>
/// Resume os movimentos de um SAF-T (contagem, totais, período e diários) e diz,
/// por diário, se já existe na empresa. Só lê; não importa nada.
/// </summary>
public sealed class SaftEntriesPreviewService(ISaftEntriesReader entriesReader, AccountingDbContext context)
{
    public async Task<SaftEntriesReconciliation?> PreviewAsync(
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

        var summary = await entriesReader.ReadSummaryAsync(content, cancellationToken).ConfigureAwait(false);

        var companyJournalCodes = (await context.Journals.AsNoTracking()
            .Where(journal => journal.CompanyId == companyId)
            .Select(journal => journal.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var journals = summary.Journals
            .Select(journal => new SaftEntriesJournalStatus(
                journal.Code,
                journal.Description,
                journal.TransactionCount,
                companyJournalCodes.Contains(journal.Code)))
            .OrderBy(journal => journal.Code, StringComparer.Ordinal)
            .ToList();

        return new SaftEntriesReconciliation(
            summary.HasEntries,
            summary.TransactionCount,
            summary.DeclaredEntryCount,
            summary.TotalDebit,
            summary.TotalCredit,
            summary.Balanced,
            summary.FromDate,
            summary.ToDate,
            journals);
    }
}
