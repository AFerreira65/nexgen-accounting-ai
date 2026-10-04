using AccountingAI.Application.CrossCheck;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>
/// Junta as faturas de compra do e-Fatura persistidas com os lançamentos da
/// contabilidade do período e produz o <see cref="CrossCheckReport"/>. Só lê;
/// a lógica de correspondência e de agregação vive em
/// <see cref="CrossCheckBuilder"/>.
/// </summary>
public sealed class CrossCheckService(AccountingDbContext context) : ICrossCheckService
{
    public async Task<CrossCheckReport?> BuildAsync(
        Guid userId,
        Guid companyId,
        DateOnly from,
        DateOnly to,
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

        var invoices = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId &&
                item.InvoiceDate >= from &&
                item.InvoiceDate <= to)
            .Select(item => new CrossCheckInvoiceInput(
                item.Id,
                item.SupplierTaxId,
                item.SupplierName,
                item.InvoiceNo,
                item.Atcud,
                item.InvoiceDate,
                item.TaxBase,
                item.TaxAmount,
                item.GrossTotal))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Um lançamento é «de compra» quando tem pelo menos uma linha numa conta
        // de gasto (classe 6) ou de IVA dedutível (2432*). Trazem-se as linhas
        // para somar a base e o IVA em memória. A abertura e o apuramento
        // contra-movimentam as classes 6 e 7 sem serem operações reais, por isso
        // ficam de fora das três perspetivas.
        var purchaseEntryRows = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.Source != EntrySource.Opening &&
                entry.Source != EntrySource.Closing &&
                entry.EntryDate >= from &&
                entry.EntryDate <= to &&
                entry.Lines.Any(line =>
                    line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("2432")))
            .Select(entry => new
            {
                entry.Reference,
                entry.EntryDate,
                entry.DocumentReference,
                entry.Description,
                PurchaseBase = entry.Lines
                    .Where(line => line.Account.Code.StartsWith("6"))
                    .Sum(line => (decimal?)(line.Debit - line.Credit)) ?? 0m,
                DeductibleVat = entry.Lines
                    .Where(line => line.Account.Code.StartsWith("2432"))
                    .Sum(line => (decimal?)(line.Debit - line.Credit)) ?? 0m
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var purchaseEntries = purchaseEntryRows
            .Select(row => new CrossCheckPurchaseEntryInput(
                row.Reference,
                row.EntryDate,
                row.DocumentReference,
                row.Description,
                row.PurchaseBase,
                row.DeductibleVat))
            .ToList();

        var periodLines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Opening &&
                line.JournalEntry.Source != EntrySource.Closing &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to);

        var salesRows = await periodLines
            .Where(line =>
                line.Account.Code.StartsWith("71") ||
                line.Account.Code.StartsWith("72") ||
                line.Account.Code.StartsWith("2433"))
            .Select(line => new
            {
                line.JournalEntry.EntryDate.Year,
                line.JournalEntry.EntryDate.Month,
                IsVat = line.Account.Code.StartsWith("2433"),
                Net = line.Credit - line.Debit
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var salesMonths = salesRows
            .GroupBy(row => (row.Year, row.Month))
            .Select(group => new CrossCheckSalesMonthInput(
                group.Key.Year,
                group.Key.Month,
                group.Where(row => !row.IsVat).Sum(row => row.Net),
                group.Where(row => row.IsVat).Sum(row => row.Net)))
            .ToList();

        var periodDebit = await periodLines
            .SumAsync(line => (decimal?)line.Debit, cancellationToken)
            .ConfigureAwait(false) ?? 0m;
        var periodCredit = await periodLines
            .SumAsync(line => (decimal?)line.Credit, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        return CrossCheckBuilder.Build(
            from, to, invoices, purchaseEntries, salesMonths, periodDebit, periodCredit);
    }
}
