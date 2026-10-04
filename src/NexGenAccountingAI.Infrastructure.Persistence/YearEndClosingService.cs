using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record YearEndClosingLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);

public sealed record YearEndClosingPreview(
    int Year,
    decimal Income,
    decimal Expenses,
    decimal NetResult,
    bool AlreadyClosed,
    string? ClosingReference,
    IReadOnlyList<YearEndClosingLine> Lines);

/// <summary>
/// Apuramento de resultados: fecha as contas das classes 6 e 7 de um exercício
/// contra a conta <c>81 — Resultado líquido do período</c>, no diário de
/// apuramento e com o tipo de documento <c>AP</c>. Só apura uma vez por exercício.
/// </summary>
public sealed class YearEndClosingService(AccountingDbContext context, JournalPostingService posting)
{
    private const string ResultAccountCode = "81";
    private const string ClosingDocumentType = "AP";

    /// <summary>Exercícios da empresa com apuramento (<c>AP</c>) não anulado.</summary>
    public async Task<IReadOnlyList<int>> ClosedYearsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == ClosingDocumentType)
            .Select(entry => entry.EntryDate.Year)
            .Distinct()
            .OrderBy(year => year)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<YearEndClosingPreview> PreviewAsync(Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "O exercício tem de estar entre 2000 e 2200.");
        }

        var closingReference = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate.Year == year &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == ClosingDocumentType)
            .Select(entry => entry.Reference)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var grouped = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate.Year == year &&
                (line.JournalEntry.DocumentType == null || line.JournalEntry.DocumentType.Code != ClosingDocumentType) &&
                (line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7")))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new
            {
                group.Key.Code,
                group.Key.Name,
                Debit = group.Sum(line => line.Debit),
                Credit = group.Sum(line => line.Credit)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        decimal income = 0;
        decimal expenses = 0;
        var lines = new List<YearEndClosingLine>();
        foreach (var item in grouped.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            var net = item.Debit - item.Credit; // saldo devedor positivo
            if (net == 0)
            {
                continue;
            }

            // Uma linha que leva o saldo da conta a zero: o oposto do saldo atual.
            var debit = net < 0 ? -net : 0m;
            var credit = net > 0 ? net : 0m;
            lines.Add(new YearEndClosingLine(item.Code, item.Name, debit, credit));

            if (item.Code.StartsWith("7", StringComparison.Ordinal))
            {
                income += -net;
            }
            else
            {
                expenses += net;
            }
        }

        var netResult = income - expenses;
        if (lines.Count > 0 && netResult != 0)
        {
            var resultName = await context.Accounts
                .AsNoTracking()
                .Where(account => account.CompanyId == companyId && account.Code == ResultAccountCode)
                .Select(account => account.Name)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? "Resultado líquido do período";

            lines.Add(netResult > 0
                ? new YearEndClosingLine(ResultAccountCode, resultName, 0m, netResult)
                : new YearEndClosingLine(ResultAccountCode, resultName, -netResult, 0m));
        }

        return new YearEndClosingPreview(
            year, income, expenses, netResult, closingReference is not null, closingReference, lines);
    }

    public async Task<PostedMovement> RegisterAsync(Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(companyId, year, cancellationToken).ConfigureAwait(false);
        if (preview.AlreadyClosed)
        {
            throw new InvalidOperationException($"O exercício de {year} já foi apurado ({preview.ClosingReference}).");
        }

        if (preview.Lines.Count < 2)
        {
            throw new InvalidOperationException($"Não há rendimentos nem gastos para apurar em {year}.");
        }

        var inputs = preview.Lines
            .Select(line => new JournalEntryLineInput(line.AccountCode, $"Apuramento de {year}", line.Debit, line.Credit))
            .ToList();

        return await posting.PostAsync(
            companyId,
            journalCode: string.Empty,
            new DateOnly(year, 12, 31),
            $"Apuramento {year}",
            $"Apuramento de resultados de {year}",
            reviewed: true,
            inputs,
            cancellationToken,
            documentTypeCode: ClosingDocumentType,
            source: EntrySource.Closing).ConfigureAwait(false);
    }

    /// <summary>Anula o lançamento de apuramento para o exercício poder ser reapurado.</summary>
    public async Task<PostedMovement> ReopenAsync(Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(companyId, year, cancellationToken).ConfigureAwait(false);
        if (!preview.AlreadyClosed || preview.ClosingReference is null)
        {
            throw new InvalidOperationException($"O exercício de {year} não está apurado.");
        }

        var nextYearOpened = await context.JournalEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate.Year == year + 1 &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == "AB", cancellationToken)
            .ConfigureAwait(false);
        if (nextYearOpened)
        {
            throw new InvalidOperationException($"Reabra primeiro o exercício de {year + 1}.");
        }

        return await posting.VoidAsync(companyId, preview.ClosingReference, cancellationToken).ConfigureAwait(false);
    }
}
