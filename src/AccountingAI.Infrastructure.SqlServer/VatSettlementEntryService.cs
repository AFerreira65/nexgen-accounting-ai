using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>As contas do apuramento: a de apuramento (2435), a do IVA a pagar (2436) e a do IVA a recuperar (2437).</summary>
public sealed record VatSettlementAccounts(string Settlement = "2435", string Payable = "2436", string Recoverable = "2437");

public sealed record VatSettlementEntryLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <param name="Period">O período (<c>03</c>, <c>06T</c>…), ou <c>null</c> se as datas não formam um mês nem um trimestre.</param>
/// <param name="Payable">O IVA a pagar que o lançamento leva à conta 2436.</param>
/// <param name="Recoverable">O IVA a recuperar que o lançamento leva à conta 2437.</param>
/// <param name="PriorCredit">O saldo da conta 2437 antes do período, que entra no apuramento.</param>
/// <param name="PostedReference">A referência do lançamento de apuramento já gerado para o período, se existir.</param>
/// <param name="Blocker">A razão por que o lançamento não pode ser gerado; <c>null</c> quando pode.</param>
public sealed record VatSettlementEntryPreview(
    DateOnly From,
    DateOnly To,
    string? Period,
    IReadOnlyList<VatSettlementEntryLine> Lines,
    decimal Payable,
    decimal Recoverable,
    decimal PriorCredit,
    string? PostedReference,
    string? Blocker);

/// <summary>
/// Prepara e gera o lançamento de apuramento do IVA de um período: salda as
/// contas de IVA dedutível (2432), liquidado (2433) e regularizações (2434)
/// por contrapartida da conta de apuramento (2435), junta o IVA a recuperar
/// que vinha de trás (2437) e leva o resultado ao IVA a pagar (2436) ou a
/// recuperar (2437). Trabalha sobre o que ainda está por saldar no período,
/// pelo que não duplica um apuramento que já esteja na contabilidade.
/// </summary>
public sealed class VatSettlementEntryService(AccountingDbContext context, JournalPostingService posting)
{
    /// <summary>A referência de documento do lançamento gerado para um período.</summary>
    public static string DocumentReference(DateOnly from, DateOnly to) =>
        $"Apuramento IVA {from.Year} {VatDeclarationService.PeriodOf(from, to) ?? $"{from:MMdd}-{to:MMdd}"}";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<VatSettlementEntryPreview?> PreviewAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, VatSettlementAccounts accounts, CancellationToken cancellationToken = default)
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

        var period = VatDeclarationService.PeriodOf(from, to);
        var documentReference = DocumentReference(from, to);
        var posted = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.DocumentReference == documentReference)
            .Select(entry => entry.Reference)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // O que ainda está por saldar no período — contando com apuramentos que já lá estejam.
        var balances = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                (line.Account.Code.StartsWith("2432") || line.Account.Code.StartsWith("2433") || line.Account.Code.StartsWith("2434")))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new { group.Key.Code, group.Key.Name, Net = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var carried = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate < from &&
                line.Account.Code.StartsWith("2437"))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new { group.Key.Code, group.Key.Name, Net = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId &&
                (account.Code == accounts.Settlement || account.Code == accounts.Payable || account.Code == accounts.Recoverable))
            .ToDictionaryAsync(account => account.Code, account => new { account.Name, account.AllowsPosting }, cancellationToken)
            .ConfigureAwait(false);

        var lines = new List<VatSettlementEntryLine>();
        decimal toDebit = 0m, toCredit = 0m;
        void Clear(string code, string name, decimal net)
        {
            // Saldar é lançar ao contrário do saldo, por contrapartida da conta de apuramento.
            if (net > 0m)
            {
                lines.Add(new VatSettlementEntryLine(code, name, 0m, net));
                toDebit += net;
            }
            else if (net < 0m)
            {
                lines.Add(new VatSettlementEntryLine(code, name, -net, 0m));
                toCredit += -net;
            }
        }

        foreach (var balance in balances.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            Clear(balance.Code, balance.Name, decimal.Round(balance.Net, 2));
        }

        var movement = lines.Count > 0;
        var priorCredit = 0m;
        foreach (var balance in carried.Where(item => item.Net > 0m).OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            priorCredit += decimal.Round(balance.Net, 2);
            Clear(balance.Code, balance.Name, decimal.Round(balance.Net, 2));
        }

        string Name(string code) => names.GetValueOrDefault(code)?.Name ?? code;
        if (toDebit != 0m)
        {
            lines.Add(new VatSettlementEntryLine(accounts.Settlement, Name(accounts.Settlement), toDebit, 0m));
        }

        if (toCredit != 0m)
        {
            lines.Add(new VatSettlementEntryLine(accounts.Settlement, Name(accounts.Settlement), 0m, toCredit));
        }

        var result = toCredit - toDebit;
        if (result > 0m)
        {
            lines.Add(new VatSettlementEntryLine(accounts.Settlement, Name(accounts.Settlement), result, 0m));
            lines.Add(new VatSettlementEntryLine(accounts.Payable, Name(accounts.Payable), 0m, result));
        }
        else if (result < 0m)
        {
            lines.Add(new VatSettlementEntryLine(accounts.Settlement, Name(accounts.Settlement), 0m, -result));
            lines.Add(new VatSettlementEntryLine(accounts.Recoverable, Name(accounts.Recoverable), -result, 0m));
        }

        var missing = new[] { accounts.Settlement, accounts.Payable, accounts.Recoverable }
            .Where(code => !names.TryGetValue(code, out var account) || !account.AllowsPosting)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var blocker = posted is not null ? $"O apuramento deste período já foi lançado ({posted}). Anule esse lançamento para o gerar de novo."
            : period is null ? "As datas não correspondem a um mês nem a um trimestre."
            : !movement ? "Não há IVA por apurar no período: as contas 2432, 2433 e 2434 estão saldadas ou sem movimento."
            : missing.Count > 0 ? $"Falta no plano de contas, como conta de movimento: {string.Join(", ", missing)}."
            : null;

        return new VatSettlementEntryPreview(
            from, to, period, lines, Math.Max(0m, result), Math.Max(0m, -result), priorCredit, posted, blocker);
    }

    /// <summary>Gera o lançamento, com a data do fim do período; fica por conferir.</summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PostedMovement?> PostAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, VatSettlementAccounts accounts, string journalCode,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(userId, companyId, from, to, accounts, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return null;
        }

        if (preview.Blocker is not null)
        {
            throw new InvalidOperationException(preview.Blocker);
        }

        var description = $"Apuramento do IVA de {from:dd/MM/yyyy} a {to:dd/MM/yyyy}";
        var lines = preview.Lines
            .Select(line => new JournalEntryLineInput(line.AccountCode, description, line.Debit, line.Credit))
            .ToList();
        return await posting
            .PostAsync(companyId, journalCode, to, DocumentReference(from, to), description, reviewed: false, lines, cancellationToken)
            .ConfigureAwait(false);
    }
}
