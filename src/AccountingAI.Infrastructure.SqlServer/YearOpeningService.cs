using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record YearOpeningLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);

public sealed record YearOpeningPreview(
    int Year,
    int PreviousYear,
    int AccountCount,
    decimal Total,
    bool PreviousYearClosed,
    bool AlreadyOpened,
    string? OpeningReference,
    IReadOnlyList<YearOpeningLine> Lines);

/// <summary>
/// Abertura do exercício: transporta os saldos de balanço (classes 1 a 5) do
/// exercício anterior para o novo exercício, num lançamento no diário de abertura
/// (tipo de documento <c>AB</c>), e leva o resultado do exercício anterior
/// (conta <c>81</c>) a <c>56 — Resultados transitados</c>. Só abre uma vez por
/// exercício e exige que o exercício anterior esteja apurado.
/// </summary>
public sealed class YearOpeningService(AccountingDbContext context, JournalPostingService posting)
{
    private const string ResultAccountCode = "81";
    private const string RetainedEarningsCode = "56";
    private const string ClosingDocumentType = "AP";
    private const string OpeningDocumentType = "AB";

    public async Task<YearOpeningPreview> PreviewAsync(Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        if (year is < 2001 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "O exercício tem de estar entre 2001 e 2200.");
        }

        var previousYear = year - 1;

        var previousYearClosed = await context.JournalEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate.Year == previousYear &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == ClosingDocumentType, cancellationToken)
            .ConfigureAwait(false);

        var openingReference = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate.Year == year &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == OpeningDocumentType)
            .Select(entry => entry.Reference)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastDay = new DateOnly(previousYear, 12, 31);
        var grouped = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate <= lastDay &&
                (line.Account.Code.StartsWith("1") || line.Account.Code.StartsWith("2") ||
                 line.Account.Code.StartsWith("3") || line.Account.Code.StartsWith("4") ||
                 line.Account.Code.StartsWith("5") || line.Account.Code == ResultAccountCode))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new
            {
                group.Key.Code,
                group.Key.Name,
                Balance = group.Sum(line => line.Debit - line.Credit)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var carry = new Dictionary<string, (string Name, decimal Balance)>(StringComparer.Ordinal);
        foreach (var item in grouped)
        {
            var code = item.Code == ResultAccountCode ? RetainedEarningsCode : item.Code;
            var name = item.Code == ResultAccountCode ? await RetainedEarningsNameAsync(companyId, cancellationToken).ConfigureAwait(false) : item.Name;
            var current = carry.TryGetValue(code, out var existing) ? existing.Balance : 0m;
            carry[code] = (name, current + item.Balance);
        }

        var lines = carry
            .Where(entry => entry.Value.Balance != 0)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new YearOpeningLine(
                entry.Key,
                entry.Value.Name,
                entry.Value.Balance > 0 ? entry.Value.Balance : 0m,
                entry.Value.Balance < 0 ? -entry.Value.Balance : 0m))
            .ToList();

        return new YearOpeningPreview(
            year,
            previousYear,
            lines.Count,
            lines.Sum(line => line.Debit),
            previousYearClosed,
            openingReference is not null,
            openingReference,
            lines);
    }

    public async Task<PostedMovement> RegisterAsync(Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(companyId, year, cancellationToken).ConfigureAwait(false);
        if (preview.AlreadyOpened)
        {
            throw new InvalidOperationException($"O exercício de {year} já foi aberto ({preview.OpeningReference}).");
        }

        if (!preview.PreviousYearClosed)
        {
            throw new InvalidOperationException($"O exercício de {preview.PreviousYear} ainda não foi apurado.");
        }

        if (preview.Lines.Count < 2)
        {
            throw new InvalidOperationException($"Não há saldos de balanço para abrir o exercício de {year}.");
        }

        var inputs = preview.Lines
            .Select(line => new JournalEntryLineInput(line.AccountCode, $"Abertura de {year}", line.Debit, line.Credit))
            .ToList();

        return await posting.PostAsync(
            companyId,
            journalCode: string.Empty,
            new DateOnly(year, 1, 1),
            $"Abertura {year}",
            $"Abertura do exercício de {year}",
            reviewed: true,
            inputs,
            cancellationToken,
            documentTypeCode: OpeningDocumentType).ConfigureAwait(false);
    }

    private async Task<string> RetainedEarningsNameAsync(Guid companyId, CancellationToken cancellationToken) =>
        await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.Code == RetainedEarningsCode)
            .Select(account => account.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "Resultados transitados";
}
