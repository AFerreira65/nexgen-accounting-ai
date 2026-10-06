using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Quem mais contribuiu para a variação de uma conta.</summary>
public sealed record VarianceParty(string Name, decimal Current, decimal Previous, decimal Delta);

/// <summary>A variação de uma conta de gastos ou rendimentos entre dois períodos.</summary>
/// <param name="Kind"><c>income</c> (classe 7) ou <c>expense</c> (classe 6).</param>
/// <param name="Delta">Atual menos anterior, com o sinal do valor: um gasto maior dá delta positivo.</param>
/// <param name="Percent"><c>null</c> quando o período anterior é zero.</param>
/// <param name="Favorable">Mais rendimentos ou menos gastos.</param>
public sealed record VarianceRow(
    string AccountCode,
    string AccountName,
    string Kind,
    decimal Current,
    decimal Previous,
    decimal Delta,
    decimal? Percent,
    bool Favorable,
    IReadOnlyList<VarianceParty> TopParties);

public sealed record VarianceTotals(decimal Income, decimal PreviousIncome, decimal Expenses, decimal PreviousExpenses)
{
    public decimal Result => Income - Expenses;

    public decimal PreviousResult => PreviousIncome - PreviousExpenses;
}

public sealed record VarianceReport(
    DateOnly From, DateOnly To, DateOnly CompareFrom, DateOnly CompareTo, VarianceTotals Totals, IReadOnlyList<VarianceRow> Rows);

/// <summary>
/// A comparação de gastos e rendimentos entre dois períodos, conta a conta (F4F-03): o que mudou, de
/// quanto e quem mais contribuiu. É determinística; a explicação em texto, se for pedida, parte destes
/// números e não de dados novos. Os lançamentos anulados, de abertura e de apuramento ficam de fora.
/// </summary>
public sealed class VarianceService(AccountingDbContext context)
{
    /// <summary>O máximo de contas devolvidas e quantos terceiros se detalham nas maiores variações.</summary>
    public const int MaxRows = 200, DetailedRows = 8, PartiesPerRow = 3;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<VarianceReport?> BuildAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken cancellationToken = default)
    {
        if (to < from || compareTo < compareFrom)
        {
            throw new ArgumentException("A data final não pode ser anterior à inicial.");
        }

        if (!await context.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId && company.IsActive &&
                company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var current = await TotalsAsync(companyId, from, to, cancellationToken).ConfigureAwait(false);
        var previous = await TotalsAsync(companyId, compareFrom, compareTo, cancellationToken).ConfigureAwait(false);
        var rows = new List<VarianceRow>();
        foreach (var code in current.Keys.Union(previous.Keys).OrderBy(code => code, StringComparer.Ordinal))
        {
            var now = current.GetValueOrDefault(code);
            var before = previous.GetValueOrDefault(code);
            var first = (current.GetValueOrDefault(code).Name ?? previous.GetValueOrDefault(code).Name) ?? string.Empty;
            var income = code.StartsWith('7');
            var delta = now.Amount - before.Amount;
            if (delta == 0m)
            {
                continue;
            }

            rows.Add(new VarianceRow(
                code, first, income ? "income" : "expense", now.Amount, before.Amount, delta,
                before.Amount == 0m ? null : decimal.Round(delta / Math.Abs(before.Amount) * 100m, 1),
                income ? delta > 0m : delta < 0m, []));
        }

        var top = rows.OrderByDescending(row => Math.Abs(row.Delta)).Take(MaxRows).ToList();
        for (var index = 0; index < Math.Min(DetailedRows, top.Count); index++)
        {
            top[index] = top[index] with { TopParties = await PartiesAsync(companyId, top[index], from, to, compareFrom, compareTo, cancellationToken).ConfigureAwait(false) };
        }

        decimal Sum(Dictionary<string, (string? Name, decimal Amount)> source, char prefix) => source.Where(item => item.Key.StartsWith(prefix)).Sum(item => item.Value.Amount);
        return new VarianceReport(from, to, compareFrom, compareTo, new VarianceTotals(Sum(current, '7'), Sum(previous, '7'), Sum(current, '6'), Sum(previous, '6')), top);
    }

    /// <summary>O valor de cada conta de gastos (débito menos crédito) e de rendimentos (crédito menos débito) num período.</summary>
    private async Task<Dictionary<string, (string? Name, decimal Amount)>> TotalsAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await Lines(companyId, from, to)
            .Where(line => line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7"))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new { group.Key.Code, group.Key.Name, Debit = group.Sum(line => line.Debit), Credit = group.Sum(line => line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Code, row => ((string?)row.Name, row.Code.StartsWith('7') ? row.Credit - row.Debit : row.Debit - row.Credit));
    }

    private async Task<IReadOnlyList<VarianceParty>> PartiesAsync(
        Guid companyId, VarianceRow row, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken cancellationToken)
    {
        var income = row.Kind == "income";
        async Task<Dictionary<string, decimal>> ByParty(DateOnly start, DateOnly end)
        {
            var rows = await Lines(companyId, start, end)
                .Where(line => line.Account.Code == row.AccountCode)
                .GroupBy(line => line.Party == null ? "(sem terceiro)" : line.Party.Name)
                .Select(group => new { Name = group.Key, Debit = group.Sum(line => line.Debit), Credit = group.Sum(line => line.Credit) })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToDictionary(item => item.Name, item => income ? item.Credit - item.Debit : item.Debit - item.Credit);
        }

        var now = await ByParty(from, to).ConfigureAwait(false);
        var before = await ByParty(compareFrom, compareTo).ConfigureAwait(false);
        return now.Keys.Union(before.Keys)
            .Select(name => new VarianceParty(name, now.GetValueOrDefault(name), before.GetValueOrDefault(name), now.GetValueOrDefault(name) - before.GetValueOrDefault(name)))
            .Where(party => party.Delta != 0m)
            .OrderByDescending(party => Math.Abs(party.Delta))
            .Take(PartiesPerRow)
            .ToList();
    }

    private IQueryable<JournalEntryLine> Lines(Guid companyId, DateOnly from, DateOnly to) =>
        context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.EntryDate >= from && line.JournalEntry.EntryDate <= to &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Opening &&
                line.JournalEntry.Source != EntrySource.Closing);
}
