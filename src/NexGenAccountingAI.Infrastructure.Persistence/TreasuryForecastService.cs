using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um mês da previsão: saldo inicial, recebimentos, pagamentos, gastos fixos e saldo final.</summary>
public sealed record TreasuryForecastMonth(int Year, int Month, decimal Opening, decimal Receipts, decimal Payments, decimal Fixed, decimal Closing);

/// <summary>Um documento em aberto, com a data em que se espera receber ou pagar.</summary>
public sealed record TreasuryForecastItem(string PartyName, string PartyTaxNumber, DateOnly DocumentDate, DateOnly ExpectedDate, decimal Amount);

/// <summary>
/// A previsão de tesouraria (F3-25A) a uma data, para os meses seguintes, com os
/// critérios usados à vista: os prazos de recebimento e de pagamento, os gastos
/// fixos por mês e o limite a partir do qual um documento fica como duvidoso.
/// </summary>
public sealed record TreasuryForecast(
    DateOnly Date,
    decimal OpeningCash,
    int CollectionDays,
    int PaymentDays,
    decimal FixedMonthly,
    bool FixedFromHistory,
    int DoubtfulAfterDays,
    IReadOnlyList<TreasuryForecastMonth> Months,
    decimal DoubtfulReceivables,
    decimal DoubtfulPayables,
    IReadOnlyList<TreasuryForecastItem> TopReceipts,
    IReadOnlyList<TreasuryForecastItem> TopPayments)
{
    public decimal LowestClosing => Months.Count == 0 ? OpeningCash : Months.Min(month => month.Closing);
}

/// <summary>
/// Previsão de tesouraria para os próximos meses, por critérios simples e à
/// vista — não é um modelo estatístico. Parte do saldo de caixa e bancos (11,
/// 12, 13) na data; os documentos de clientes (21) e de fornecedores (22) em
/// aberto — pela mesma regra da idade de saldos, que abate os pagamentos aos
/// documentos mais antigos de cada terceiro — entram no mês da data do
/// documento mais o prazo indicado (ou no primeiro mês, se essa data já
/// passou); os com mais de <see cref="DoubtfulAfterDays"/> dias ficam de fora,
/// como duvidosos. Os gastos fixos por mês são indicados pelo utilizador ou, por
/// omissão, a média dos gastos com o pessoal (63) dos últimos três meses.
/// </summary>
public sealed class TreasuryForecastService(AccountingDbContext context)
{
    public const int DoubtfulAfterDays = 180;

    public const int MaxMonths = 12;

    private const int TopItems = 8;

    public async Task<TreasuryForecast?> BuildAsync(
        Guid userId, Guid companyId, DateOnly date, int months, int collectionDays, int paymentDays, decimal? fixedMonthly,
        CancellationToken cancellationToken = default)
    {
        if (months is < 1 or > MaxMonths)
        {
            throw new ArgumentException($"Indique entre 1 e {MaxMonths} meses.", nameof(months));
        }

        if (collectionDays is < 0 or > 365 || paymentDays is < 0 or > 365)
        {
            throw new ArgumentException("Os prazos têm de estar entre 0 e 365 dias.");
        }

        if (fixedMonthly is < 0m)
        {
            throw new ArgumentException("Os gastos fixos não podem ser negativos.", nameof(fixedMonthly));
        }

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

        var lines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate <= date);

        // A abertura repõe o saldo de cada exercício: conta-se o saldo do exercício da data (abertura incluída).
        var yearStart = new DateOnly(date.Year, 1, 1);
        var opening = await lines
            .Where(line => line.JournalEntry.EntryDate >= yearStart &&
                line.JournalEntry.Source != EntrySource.Closing &&
                (line.Account.Code.StartsWith("11") || line.Account.Code.StartsWith("12") || line.Account.Code.StartsWith("13")))
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var fromHistory = fixedMonthly is null;
        var fixedAmount = fixedMonthly ?? await PersonnelAverageAsync(companyId, date, cancellationToken).ConfigureAwait(false);

        var receivables = await OpenDocumentsAsync(companyId, date, customers: true, cancellationToken).ConfigureAwait(false);
        var payables = await OpenDocumentsAsync(companyId, date, customers: false, cancellationToken).ConfigureAwait(false);

        TreasuryForecastItem Expect(OpenDocument document, int days)
        {
            var expected = document.Date.AddDays(days);
            return new TreasuryForecastItem(document.PartyName, document.PartyTaxNumber, document.Date, expected < date ? date : expected, document.Amount);
        }

        bool Doubtful(OpenDocument document) => date.DayNumber - document.Date.DayNumber > DoubtfulAfterDays;
        var receipts = receivables.Where(document => !Doubtful(document)).Select(document => Expect(document, collectionDays)).ToList();
        var payments = payables.Where(document => !Doubtful(document)).Select(document => Expect(document, paymentDays)).ToList();

        var result = new List<TreasuryForecastMonth>();
        var balance = decimal.Round(opening, 2);
        var first = new DateOnly(date.Year, date.Month, 1);
        for (var index = 0; index < months; index++)
        {
            var monthStart = first.AddMonths(index);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            bool InMonth(TreasuryForecastItem item) =>
                (index == 0 ? item.ExpectedDate <= monthEnd : item.ExpectedDate >= monthStart && item.ExpectedDate <= monthEnd);
            var inflow = receipts.Where(InMonth).Sum(item => item.Amount);
            var outflow = payments.Where(InMonth).Sum(item => item.Amount);

            // No mês da data, os gastos fixos contam só pela parte do mês que falta.
            var share = index == 0 ? (decimal)(monthEnd.DayNumber - date.DayNumber) / DateTime.DaysInMonth(monthStart.Year, monthStart.Month) : 1m;
            var fixedCost = decimal.Round(fixedAmount * share, 2);
            var closing = balance + inflow - outflow - fixedCost;
            result.Add(new TreasuryForecastMonth(monthStart.Year, monthStart.Month, balance, decimal.Round(inflow, 2), decimal.Round(outflow, 2), fixedCost, decimal.Round(closing, 2)));
            balance = decimal.Round(closing, 2);
        }

        var horizonEnd = first.AddMonths(months).AddDays(-1);
        return new TreasuryForecast(
            date,
            decimal.Round(opening, 2),
            collectionDays,
            paymentDays,
            decimal.Round(fixedAmount, 2),
            fromHistory,
            DoubtfulAfterDays,
            result,
            decimal.Round(receivables.Where(Doubtful).Sum(document => document.Amount), 2),
            decimal.Round(payables.Where(Doubtful).Sum(document => document.Amount), 2),
            receipts.Where(item => item.ExpectedDate <= horizonEnd).OrderByDescending(item => item.Amount).Take(TopItems).ToList(),
            payments.Where(item => item.ExpectedDate <= horizonEnd).OrderByDescending(item => item.Amount).Take(TopItems).ToList());
    }

    /// <summary>A média mensal dos gastos com o pessoal (63) nos três meses completos antes da data.</summary>
    private async Task<decimal> PersonnelAverageAsync(Guid companyId, DateOnly date, CancellationToken cancellationToken)
    {
        var to = new DateOnly(date.Year, date.Month, 1).AddDays(-1);
        var from = new DateOnly(to.Year, to.Month, 1).AddMonths(-2);
        var total = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Closing &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.Account.Code.StartsWith("63"))
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;
        return Math.Max(0m, decimal.Round(total / 3m, 2));
    }

    /// <summary>Os documentos em aberto por terceiro, abatendo os pagamentos aos mais antigos (a regra da idade de saldos).</summary>
    private async Task<List<OpenDocument>> OpenDocumentsAsync(Guid companyId, DateOnly date, bool customers, CancellationToken cancellationToken)
    {
        var prefix = customers ? "21" : "22";
        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.PartyId != null &&
                line.Account.Code.StartsWith(prefix) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate <= date)
            .Select(line => new
            {
                PartyId = line.PartyId!.Value,
                PartyName = line.Party!.Name,
                PartyTaxNumber = line.Party.TaxRegistrationNumber,
                Date = line.JournalEntry.DocumentDate ?? line.JournalEntry.EntryDate,
                line.Debit,
                line.Credit,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<OpenDocument>();
        foreach (var group in rows.GroupBy(row => row.PartyId))
        {
            var settled = group.Sum(row => customers ? row.Credit : row.Debit);
            foreach (var document in group
                .Select(row => (row.Date, Amount: customers ? row.Debit : row.Credit))
                .Where(item => item.Amount != 0m)
                .OrderBy(item => item.Date))
            {
                var paid = Math.Min(document.Amount, settled);
                settled -= paid;
                var open = document.Amount - paid;
                if (open > 0m)
                {
                    result.Add(new OpenDocument(group.First().PartyName, group.First().PartyTaxNumber, document.Date, open));
                }
            }
        }

        return result;
    }

    private sealed record OpenDocument(string PartyName, string PartyTaxNumber, DateOnly Date, decimal Amount);
}
