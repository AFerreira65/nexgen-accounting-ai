using AccountingAI.Application.Vat;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// Calcula o apuramento contabilístico de IVA de um período a partir dos
/// movimentos das contas 24.3 (ADR-0007). Só lê; exclui os anulados. Não é a
/// declaração periódica oficial da AT.
/// </summary>
public sealed class VatReturnService(AccountingDbContext context) : IVatReturnService
{
    private const string VatPrefix = "243";
    private const string OutputPrefix = "2433";
    private const string InputPrefix = "2432";
    private const string RegularisationPrefix = "2434";
    private const string CarryForwardPrefix = "2437";

    public async Task<VatReturn?> BuildAsync(
        Guid userId,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        decimal? priorPeriodCredit = null,
        CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var grouped = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.Account.Code.StartsWith(VatPrefix))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new
            {
                group.Key.Code,
                group.Key.Name,
                Debit = group.Sum(line => line.Debit),
                Credit = group.Sum(line => line.Credit),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        decimal priorCredit;
        bool priorCreditAssumed;
        if (priorPeriodCredit is { } provided)
        {
            priorCredit = decimal.Round(provided, 2);
            priorCreditAssumed = false;
        }
        else
        {
            var priorCarryForward = await context.JournalEntryLines
                .AsNoTracking()
                .Where(line => line.CompanyId == companyId &&
                    line.JournalEntry.Status != JournalEntryStatus.Voided &&
                    line.JournalEntry.EntryDate < from &&
                    line.Account.Code.StartsWith(CarryForwardPrefix))
                .SumAsync(line => line.Debit - line.Credit, cancellationToken)
                .ConfigureAwait(false);
            priorCredit = priorCarryForward > 0m ? decimal.Round(priorCarryForward, 2) : 0m;
            priorCreditAssumed = true;
        }

        var lines = grouped
            .Select(row => new VatReturnAccountLine(
                row.Code,
                row.Name,
                RoleOf(row.Code),
                decimal.Round(row.Debit, 2),
                decimal.Round(row.Credit, 2),
                decimal.Round(row.Debit - row.Credit, 2)))
            .OrderBy(line => line.AccountCode, StringComparer.Ordinal)
            .ToList();

        decimal Sum(string role, Func<VatReturnAccountLine, decimal> selector) =>
            decimal.Round(lines.Where(line => line.Role == role).Sum(selector), 2);

        var outputVat = Sum("liquidado", line => line.Credit - line.Debit);
        var inputVat = Sum("dedutivel", line => line.Debit - line.Credit);
        var regularisationsForState = Sum("regularizacoes", line => line.Credit);
        var regularisationsForTaxpayer = Sum("regularizacoes", line => line.Debit);

        var periodResult = decimal.Round(
            (outputVat + regularisationsForState) - (inputVat + regularisationsForTaxpayer), 2);
        var afterCarry = decimal.Round(periodResult - priorCredit, 2);

        return new VatReturn(
            from,
            to,
            outputVat,
            inputVat,
            regularisationsForState,
            regularisationsForTaxpayer,
            priorCredit,
            periodResult,
            afterCarry > 0m ? afterCarry : 0m,
            afterCarry < 0m ? -afterCarry : 0m,
            priorCreditAssumed,
            lines);
    }

    private static string RoleOf(string accountCode) => accountCode switch
    {
        _ when accountCode.StartsWith(OutputPrefix) => "liquidado",
        _ when accountCode.StartsWith(InputPrefix) => "dedutivel",
        _ when accountCode.StartsWith(RegularisationPrefix) => "regularizacoes",
        _ when accountCode.StartsWith(CarryForwardPrefix) => "reporte",
        _ => "outras",
    };
}
