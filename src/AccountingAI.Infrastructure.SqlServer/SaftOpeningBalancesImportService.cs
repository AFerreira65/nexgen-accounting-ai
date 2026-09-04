using System.Globalization;
using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// Importa os saldos de abertura do plano de contas de um SAF-T
/// (<c>OpeningDebitBalance</c>/<c>OpeningCreditBalance</c>) como um único lançamento
/// no diário de abertura (tipo de documento <c>AB</c>), datado de 1 de janeiro do
/// exercício. Só considera as contas de movimento da empresa (as agregadoras têm o
/// saldo somado das subcontas) com saldo de abertura não nulo. É idempotente: se já
/// houver abertura para o exercício, devolve a referência existente sem escrever.
/// </summary>
public sealed class SaftOpeningBalancesImportService(
    ISaftAccountsReader accountsReader,
    JournalPostingService posting,
    AccountingDbContext context)
{
    private const string OpeningDocumentType = "AB";

    public async Task<SaftOpeningBalancesImportResult?> ImportAsync(
        Guid userId,
        Guid companyId,
        int fiscalYear,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (fiscalYear is < 2001 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalYear), fiscalYear, "O exercício tem de estar entre 2001 e 2200.");
        }

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

        var existing = await context.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate.Year == fiscalYear &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == OpeningDocumentType)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new SaftOpeningBalancesImportResult(
                existing.Lines.Count,
                existing.Lines.Sum(line => line.Debit),
                existing.Lines.Sum(line => line.Credit),
                existing.Reference,
                AlreadyImported: true);
        }

        var fileAccounts = await accountsReader.ReadAsync(content, cancellationToken).ConfigureAwait(false);

        var postingAccounts = (await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.IsActive && account.AllowsPosting)
            .Select(account => account.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lines = new List<JournalEntryLineInput>();
        var totalDebit = 0m;
        var totalCredit = 0m;
        foreach (var account in fileAccounts)
        {
            if (!postingAccounts.Contains(account.Code))
            {
                continue;
            }

            var net = decimal.Round(account.OpeningDebit - account.OpeningCredit, 2, MidpointRounding.AwayFromZero);
            if (net == 0m)
            {
                continue;
            }

            if (net > 0m)
            {
                lines.Add(new JournalEntryLineInput(account.Code, "Saldo inicial", net, 0m));
                totalDebit += net;
            }
            else
            {
                lines.Add(new JournalEntryLineInput(account.Code, "Saldo inicial", 0m, -net));
                totalCredit += -net;
            }
        }

        if (lines.Count == 0)
        {
            return new SaftOpeningBalancesImportResult(0, 0m, 0m, null, AlreadyImported: false);
        }

        if (decimal.Round(totalDebit, 2) != decimal.Round(totalCredit, 2))
        {
            throw new InvalidOperationException(
                $"Os saldos de abertura das contas de movimento do ficheiro não estão equilibrados " +
                $"(débito {totalDebit.ToString("0.00", CultureInfo.InvariantCulture)}, " +
                $"crédito {totalCredit.ToString("0.00", CultureInfo.InvariantCulture)}).");
        }

        var posted = await posting.PostAsync(
            companyId,
            journalCode: string.Empty,
            new DateOnly(fiscalYear, 1, 1),
            $"Saldos iniciais {fiscalYear} (SAF-T)",
            $"Saldos iniciais do exercício de {fiscalYear} importados do SAF-T",
            reviewed: true,
            lines,
            cancellationToken,
            documentTypeCode: OpeningDocumentType,
            source: EntrySource.Opening).ConfigureAwait(false);

        return new SaftOpeningBalancesImportResult(
            posted.Lines.Count,
            posted.Lines.Sum(line => line.Debit),
            posted.Lines.Sum(line => line.Credit),
            posted.Reference,
            AlreadyImported: false);
    }
}
