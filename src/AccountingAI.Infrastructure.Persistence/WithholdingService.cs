using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma conta de retenções (242…) no exercício.</summary>
/// <param name="Opening">O que vinha por entregar do exercício anterior (lançamento de abertura).</param>
/// <param name="Withheld">O retido no exercício (créditos).</param>
/// <param name="Delivered">O entregue ao Estado no exercício (débitos).</param>
/// <param name="Balance">O que está por entregar no fim do exercício.</param>
public sealed record WithholdingAccountRow(string AccountCode, string AccountName, decimal Opening, decimal Withheld, decimal Delivered, decimal Balance);

/// <summary>O retido e o entregue num mês, numa conta.</summary>
public sealed record WithholdingMonthRow(int Month, string AccountCode, decimal Withheld, decimal Delivered);

/// <summary>O retido a um beneficiário numa conta, no exercício.</summary>
/// <param name="TaxNumber">Vazio na linha das retenções sem terceiro identificado.</param>
/// <param name="IncomeEstimate">Os gastos (classe 6) dos mesmos lançamentos: uma aproximação do rendimento pago, a conferir.</param>
public sealed record WithholdingBeneficiaryRow(
    string CountryCode, string TaxNumber, string Name, string AccountCode, decimal Withheld, decimal IncomeEstimate, int Entries);

public sealed record WithholdingReport(
    int Year,
    IReadOnlyList<WithholdingAccountRow> Accounts,
    IReadOnlyList<WithholdingMonthRow> Months,
    IReadOnlyList<WithholdingBeneficiaryRow> Beneficiaries,
    decimal TotalWithheld,
    decimal TotalDelivered,
    decimal TotalBalance,
    decimal WithheldWithoutParty,
    string Disclaimer);

/// <summary>
/// As retenções na fonte de um exercício, lidas das contas 242 («Retenção de
/// impostos sobre rendimentos»): por conta, por mês e por beneficiário. As
/// subcontas não são normalizadas, pelo que a natureza do rendimento é a que
/// o plano de contas da empresa lhes der. O beneficiário é o terceiro da
/// linha ou, se a linha não o tem, o único terceiro do lançamento. Só lê;
/// exclui os anulados. Serve de base à entrega mensal das retenções e ao
/// Modelo 10, que não preenche.
/// </summary>
public sealed class WithholdingService(AccountingDbContext context)
{
    public const string Disclaimer =
        "Mapa de apoio, não validado pela AT. Não é a declaração de retenções na fonte, a DMR nem o Modelo 10: não classifica os rendimentos " +
        "por categoria nem por código, e o rendimento de cada beneficiário é uma estimativa pelos gastos dos mesmos lançamentos.";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<WithholdingReport?> BuildAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
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

        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var lines = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.Account.Code.StartsWith("242") &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to)
            .Select(line => new
            {
                line.JournalEntryId,
                line.JournalEntry.EntryDate.Month,
                Opening = line.JournalEntry.Source == EntrySource.Opening,
                line.Account.Code,
                line.Account.Name,
                line.PartyId,
                line.Debit,
                line.Credit,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accounts = lines
            .GroupBy(line => (line.Code, line.Name))
            .Select(group =>
            {
                var opening = group.Where(line => line.Opening).Sum(line => line.Credit - line.Debit);
                var withheld = group.Where(line => !line.Opening).Sum(line => line.Credit);
                var delivered = group.Where(line => !line.Opening).Sum(line => line.Debit);
                return new WithholdingAccountRow(group.Key.Code, group.Key.Name, opening, withheld, delivered, opening + withheld - delivered);
            })
            .OrderBy(row => row.AccountCode, StringComparer.Ordinal)
            .ToList();

        var months = lines
            .Where(line => !line.Opening)
            .GroupBy(line => (line.Month, line.Code))
            .Select(group => new WithholdingMonthRow(group.Key.Month, group.Key.Code, group.Sum(line => line.Credit), group.Sum(line => line.Debit)))
            .OrderBy(row => row.Month)
            .ThenBy(row => row.AccountCode, StringComparer.Ordinal)
            .ToList();

        // Beneficiários: só o que foi retido (créditos fora da abertura).
        var withheldLines = lines.Where(line => !line.Opening && line.Credit != 0m).ToList();
        var entryIds = withheldLines.Select(line => line.JournalEntryId).Distinct().ToList();
        var entryLines = entryIds.Count == 0
            ? []
            : await context.JournalEntryLines
                .AsNoTracking()
                .Where(line => line.CompanyId == companyId && entryIds.Contains(line.JournalEntryId))
                .Select(line => new { line.JournalEntryId, line.PartyId, Expense = line.Account.Code.StartsWith("6"), line.Debit, line.Credit })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var entryParties = entryLines
            .Where(line => line.PartyId != null)
            .GroupBy(line => line.JournalEntryId)
            .ToDictionary(group => group.Key, group => group.Select(line => line.PartyId!.Value).Distinct().ToList());
        var entryExpenses = entryLines
            .Where(line => line.Expense)
            .GroupBy(line => line.JournalEntryId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Debit - line.Credit));

        var partyIds = entryParties.Values.SelectMany(ids => ids).Distinct().ToList();
        var parties = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && partyIds.Contains(party.Id))
            .ToDictionaryAsync(party => party.Id, party => new { party.CountryCode, party.TaxRegistrationNumber, party.Name }, cancellationToken)
            .ConfigureAwait(false);

        var beneficiaries = withheldLines
            .Select(line =>
            {
                var partyId = line.PartyId
                    ?? (entryParties.GetValueOrDefault(line.JournalEntryId) is { Count: 1 } single ? single[0] : (Guid?)null);
                return new { line.JournalEntryId, line.Code, line.Credit, PartyId = partyId };
            })
            .GroupBy(item => (item.PartyId, item.Code))
            .Select(group =>
            {
                var party = group.Key.PartyId is { } id ? parties[id] : null;
                var entries = group.Select(item => item.JournalEntryId).Distinct().ToList();
                return new WithholdingBeneficiaryRow(
                    party?.CountryCode ?? string.Empty,
                    party?.TaxRegistrationNumber ?? string.Empty,
                    party?.Name ?? "Sem terceiro identificado",
                    group.Key.Code,
                    group.Sum(item => item.Credit),
                    // Num lançamento com vários beneficiários os gastos não se repartem: a estimativa só vale com um terceiro por lançamento.
                    party is null ? 0m : entries.Sum(entry => entryExpenses.GetValueOrDefault(entry)),
                    entries.Count);
            })
            .OrderBy(row => row.TaxNumber.Length == 0)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.AccountCode, StringComparer.Ordinal)
            .ToList();

        return new WithholdingReport(
            year, accounts, months, beneficiaries,
            accounts.Sum(row => row.Withheld), accounts.Sum(row => row.Delivered), accounts.Sum(row => row.Balance),
            beneficiaries.Where(row => row.TaxNumber.Length == 0).Sum(row => row.Withheld),
            Disclaimer);
    }
}
