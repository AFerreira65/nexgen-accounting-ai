using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// Uma linha do resumo de IVA: o movimento de um período das contas com uma
/// dada classificação, agregado por natureza/taxa/região. <see cref="Kind"/> ∈
/// <c>base | imposto | outras</c> — <c>base</c> para as contas 6/7, <c>imposto</c>
/// para as <c>243*</c>.
/// </summary>
public sealed record VatSummaryRow(
    string Kind,
    string Nature,
    string Rate,
    string Region,
    decimal Debit,
    decimal Credit,
    decimal Net,
    int AccountCount);

/// <summary>O resumo de IVA de um período, pronto a transcrever para a declaração periódica.</summary>
public sealed record VatSummary(DateOnly From, DateOnly To, IReadOnlyList<VatSummaryRow> Rows);

/// <summary>
/// Soma o movimento do período das contas classificadas
/// (<see cref="AccountVatClassification"/>), agrupado por natureza/taxa/região
/// e separado em base (classes 6/7) e imposto (<c>243*</c>) — a desagregação
/// que se transcreve para a declaração periódica. Só conta o que teve
/// movimento no período, pelo que as contas agregadas sem lançamentos próprios
/// não aparecem. Só lê; exclui os lançamentos anulados. Não é a declaração
/// periódica oficial da AT.
/// </summary>
public sealed class VatSummaryService(AccountingDbContext context)
{
    public async Task<VatSummary?> BuildAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var grouped = await context.JournalEntryLines
            .AsNoTracking()
            .Join(
                context.AccountVatClassifications.AsNoTracking(),
                line => line.AccountId,
                classification => classification.AccountId,
                (line, classification) => new { line, classification })
            .Where(item => item.line.CompanyId == companyId &&
                item.classification.CompanyId == companyId &&
                item.line.JournalEntry.Status != JournalEntryStatus.Voided &&
                item.line.JournalEntry.EntryDate >= from &&
                item.line.JournalEntry.EntryDate <= to)
            .GroupBy(item => new
            {
                Kind = item.line.Account.Code.StartsWith("243")
                    ? "imposto"
                    : item.line.Account.Code.StartsWith("6") || item.line.Account.Code.StartsWith("7")
                        ? "base"
                        : "outras",
                item.classification.Nature,
                item.classification.Rate,
                item.classification.Region,
            })
            .Select(group => new
            {
                group.Key.Kind,
                group.Key.Nature,
                group.Key.Rate,
                group.Key.Region,
                Debit = group.Sum(item => item.line.Debit),
                Credit = group.Sum(item => item.line.Credit),
                AccountCount = group.Select(item => item.line.AccountId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => new VatSummaryRow(
                row.Kind,
                row.Nature.ToString(),
                row.Rate.ToString(),
                row.Region.ToString(),
                decimal.Round(row.Debit, 2),
                decimal.Round(row.Credit, 2),
                decimal.Round(row.Debit - row.Credit, 2),
                row.AccountCount))
            .OrderBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Nature, StringComparer.Ordinal)
            .ThenBy(row => row.Rate, StringComparer.Ordinal)
            .ThenBy(row => row.Region, StringComparer.Ordinal)
            .ToList();

        return new VatSummary(from, to, rows);
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
