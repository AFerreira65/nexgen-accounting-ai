using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma parcela de uma estrutura: a rubrica ou o terceiro, o valor e o peso no total.</summary>
public sealed record StructureItem(string Label, decimal Amount, decimal Share);

/// <param name="Expenses">Os gastos do exercício por natureza (rubricas da demonstração de resultados), do maior para o menor.</param>
/// <param name="Income">Os rendimentos por natureza.</param>
/// <param name="TopCustomers">Os clientes com mais faturação lançada (débitos nas contas 21, com IVA).</param>
/// <param name="TopSuppliers">Os fornecedores com mais compras lançadas (créditos nas contas 22, com IVA).</param>
public sealed record ExerciseStructure(
    int Year,
    IReadOnlyList<StructureItem> Expenses,
    IReadOnlyList<StructureItem> Income,
    IReadOnlyList<StructureItem> TopCustomers,
    IReadOnlyList<StructureItem> TopSuppliers);

/// <summary>
/// A estrutura de um exercício, para os gráficos de gestão: de que são feitos
/// os gastos e os rendimentos, e quem são os maiores clientes e fornecedores.
/// </summary>
public sealed partial class FinancialStatementsService
{
    private const int TopParties = 10;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<ExerciseStructure?> StructureAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var income = await IncomeStatementAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);
        if (income is null)
        {
            return null;
        }

        static List<StructureItem> Shares(IEnumerable<(string Label, decimal Amount)> items)
        {
            var list = items.Where(item => item.Amount > 0m).OrderByDescending(item => item.Amount).ToList();
            var total = list.Sum(item => item.Amount);
            return list.Select(item => new StructureItem(item.Label, item.Amount, total == 0m ? 0m : decimal.Round(item.Amount / total * 100m, 1))).ToList();
        }

        var lines = income.Lines.Where(line => !line.IsTotal).ToList();

        // A faturação e as compras por terceiro: o que foi lançado nas contas 21 e 22 no exercício, sem a abertura.
        var movements = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.PartyId != null &&
                (line.Account.Code.StartsWith("21") || line.Account.Code.StartsWith("22")) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Opening &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to)
            .GroupBy(line => new { PartyId = line.PartyId!.Value, Customer = line.Account.Code.StartsWith("21") })
            .Select(group => new { group.Key.PartyId, group.Key.Customer, Debit = group.Sum(line => line.Debit), Credit = group.Sum(line => line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var partyIds = movements.Select(item => item.PartyId).Distinct().ToList();
        var names = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && partyIds.Contains(party.Id))
            .ToDictionaryAsync(party => party.Id, party => party.Name, cancellationToken)
            .ConfigureAwait(false);

        return new ExerciseStructure(
            year,
            Shares(lines.Select(line => (line.Label, -line.Amount))),
            Shares(lines.Select(line => (line.Label, line.Amount))),
            Shares(movements.Where(item => item.Customer).Select(item => (names[item.PartyId], item.Debit))).Take(TopParties).ToList(),
            Shares(movements.Where(item => !item.Customer).Select(item => (names[item.PartyId], item.Credit))).Take(TopParties).ToList());
    }
}
