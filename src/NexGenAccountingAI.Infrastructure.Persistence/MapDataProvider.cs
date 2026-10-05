using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Os valores de que o motor de mapas precisa (F4B-01): por conta com movimento e por
/// exercício, o débito e o crédito da abertura, de cada mês e do apuramento de resultados.
/// Uma leitura agregada por exercício — nunca traz linhas de lançamento.
/// <para>
/// A abertura é o lançamento de abertura do exercício; se ainda não foi lançado, as
/// contas de balanço (classes 1 a 5) trazem o saldo acumulado desde a última abertura
/// (a mesma regra do balancete). O apuramento é o lançamento marcado como tal ou, vindo
/// de um SAF-T, o que movimenta a 811 ou a 818.
/// </para>
/// </summary>
public sealed class MapDataProvider(AccountingDbContext context)
{
    /// <summary>Não verifica o acesso: quem chama já sabe que o utilizador pode ver a empresa.</summary>
    public async Task<IReadOnlyList<MapAccount>> LoadAsync(
        Guid companyId, IReadOnlyCollection<int> years, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(years);
        var byAccount = new Dictionary<Guid, Dictionary<int, MapYearAmounts>>();
        MapYearAmounts Slots(Guid accountId, int year)
        {
            if (!byAccount.TryGetValue(accountId, out var perYear))
            {
                byAccount[accountId] = perYear = [];
            }

            if (!perYear.TryGetValue(year, out var amounts))
            {
                perYear[year] = amounts = MapYearAmounts.Empty();
            }

            return amounts;
        }

        var lines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.JournalEntry.Status != JournalEntryStatus.Voided);

        foreach (var year in years.Distinct())
        {
            var start = new DateOnly(year, 1, 1);
            var end = new DateOnly(year, 12, 31);
            var inYear = lines.Where(line => line.JournalEntry.EntryDate >= start && line.JournalEntry.EntryDate <= end);

            var sums = await inYear
                .GroupBy(line => new { line.AccountId, line.JournalEntry.EntryDate.Month, line.JournalEntry.Source })
                .Select(group => new
                {
                    group.Key.AccountId,
                    group.Key.Month,
                    group.Key.Source,
                    Debit = group.Sum(line => line.Debit),
                    Credit = group.Sum(line => line.Credit),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var hasOpening = false;
            foreach (var sum in sums)
            {
                var slot = sum.Source switch
                {
                    EntrySource.Opening => MapYearAmounts.Opening,
                    EntrySource.Closing => MapYearAmounts.Closing,
                    _ => sum.Month,
                };
                hasOpening |= sum.Source == EntrySource.Opening;
                var amounts = Slots(sum.AccountId, year);
                amounts.Debit[slot] += sum.Debit;
                amounts.Credit[slot] += sum.Credit;
            }

            // O apuramento que veio de um SAF-T não está marcado: reconhece-se por movimentar
            // a 811 ou a 818, e passa do mês em que foi lançado para o lugar do apuramento.
            var importedClosing = await inYear
                .Where(line => line.JournalEntry.Source != EntrySource.Closing &&
                    line.JournalEntry.Source != EntrySource.Opening &&
                    line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")))
                .GroupBy(line => new { line.AccountId, line.JournalEntry.EntryDate.Month })
                .Select(group => new
                {
                    group.Key.AccountId,
                    group.Key.Month,
                    Debit = group.Sum(line => line.Debit),
                    Credit = group.Sum(line => line.Credit),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var sum in importedClosing)
            {
                var amounts = Slots(sum.AccountId, year);
                amounts.Debit[sum.Month] -= sum.Debit;
                amounts.Credit[sum.Month] -= sum.Credit;
                amounts.Debit[MapYearAmounts.Closing] += sum.Debit;
                amounts.Credit[MapYearAmounts.Closing] += sum.Credit;
            }

            if (!hasOpening)
            {
                await CarryForwardAsync(companyId, year, lines, Slots, cancellationToken).ConfigureAwait(false);
            }
        }

        if (byAccount.Count == 0)
        {
            return [];
        }

        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .Select(account => new { account.Id, account.Code, account.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return accounts
            .Where(account => byAccount.ContainsKey(account.Id))
            .OrderBy(account => account.Code, StringComparer.Ordinal)
            .Select(account => new MapAccount(account.Code, account.Name, byAccount[account.Id]))
            .ToList();
    }

    /// <summary>
    /// Sem abertura lançada, o saldo inicial das contas de balanço é o acumulado desde a
    /// última abertura anterior (ou desde sempre, se nunca houve). Entra pelo saldo, do
    /// lado que tiver.
    /// </summary>
    private async Task CarryForwardAsync(
        Guid companyId, int year, IQueryable<JournalEntryLine> lines, Func<Guid, int, MapYearAmounts> slots,
        CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, 1, 1);
        var lastOpening = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.Source == EntrySource.Opening &&
                entry.EntryDate < start)
            .Select(entry => (DateOnly?)entry.EntryDate)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        var since = lastOpening is { } date ? new DateOnly(date.Year, 1, 1) : DateOnly.MinValue;

        var balances = await lines
            .Where(line => line.JournalEntry.EntryDate < start && line.JournalEntry.EntryDate >= since &&
                !(line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7") || line.Account.Code.StartsWith("8")))
            .GroupBy(line => line.AccountId)
            .Select(group => new { AccountId = group.Key, Balance = group.Sum(line => line.Debit - line.Credit) })
            .Where(item => item.Balance != 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in balances)
        {
            var amounts = slots(item.AccountId, year);
            if (item.Balance > 0m)
            {
                amounts.Debit[MapYearAmounts.Opening] += item.Balance;
            }
            else
            {
                amounts.Credit[MapYearAmounts.Opening] -= item.Balance;
            }
        }
    }
}
