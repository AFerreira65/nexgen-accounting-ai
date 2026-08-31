using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record JournalReportLine(
    string AccountCode,
    string AccountName,
    string Description,
    decimal Debit,
    decimal Credit);

public sealed record JournalReportEntry(
    string Reference,
    DateOnly EntryDate,
    string JournalCode,
    string? DocumentType,
    string DocumentReference,
    string Description,
    string Status,
    decimal Amount,
    IReadOnlyList<JournalReportLine> Lines);

public sealed record JournalReport(
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<JournalReportEntry> Entries);

/// <summary>
/// Livro diário: lançamentos de uma empresa por ordem cronológica, com as linhas
/// e os totais, opcionalmente filtrados por diário, tipo de documento, exercício e
/// intervalo de datas. Exclui sempre os lançamentos anulados.
/// </summary>
public sealed class JournalReportService(AccountingDbContext context)
{
    public async Task<JournalReport> BuildAsync(
        Guid companyId,
        string? journalCode,
        string? documentTypeCode,
        int? year,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId && entry.Status != JournalEntryStatus.Voided)
            .Include(entry => entry.Journal)
            .Include(entry => entry.DocumentType)
            .Include(entry => entry.Lines)
                .ThenInclude(line => line.Account)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(journalCode))
        {
            var normalizedJournal = journalCode.Trim().ToUpperInvariant();
            query = query.Where(entry => entry.Journal.Code == normalizedJournal);
        }

        if (!string.IsNullOrWhiteSpace(documentTypeCode))
        {
            var normalizedDocumentType = documentTypeCode.Trim().ToUpperInvariant();
            query = query.Where(entry => entry.DocumentType != null && entry.DocumentType.Code == normalizedDocumentType);
        }

        if (year is not null)
        {
            query = query.Where(entry => entry.EntryDate.Year == year);
        }

        if (fromDate is not null)
        {
            query = query.Where(entry => entry.EntryDate >= fromDate.Value);
        }

        if (toDate is not null)
        {
            query = query.Where(entry => entry.EntryDate <= toDate.Value);
        }

        var entries = await query
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var report = entries.Select(entry => new JournalReportEntry(
            entry.Reference,
            entry.EntryDate,
            entry.Journal.Code,
            entry.DocumentType?.Code,
            entry.DocumentReference,
            entry.Description,
            entry.Status == JournalEntryStatus.Reviewed ? "Conferido" : "Por conferir",
            entry.TotalAmount,
            entry.Lines
                .OrderBy(line => line.Sequence)
                .Select(line => new JournalReportLine(
                    line.Account.Code, line.Account.Name, line.Description, line.Debit, line.Credit))
                .ToList())).ToList();

        return new JournalReport(
            report.Sum(entry => entry.Lines.Sum(line => line.Debit)),
            report.Sum(entry => entry.Lines.Sum(line => line.Credit)),
            report);
    }
}
