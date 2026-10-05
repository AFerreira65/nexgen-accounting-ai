using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha do Modelo 30, pelas colunas do impresso (Portaria n.º 92-A/2011).</summary>
public sealed record Modelo30Line(
    string AccountCode,
    string AccountName,
    int Group,
    bool GroupIndicated,
    decimal Opening,
    decimal TaxedOpening,
    decimal AcceptedOpening,
    decimal TaxedUsed,
    decimal AcceptedUsed,
    decimal TaxedReversed,
    decimal AcceptedReversed,
    decimal Constituted,
    decimal Closing,
    decimal Used,
    decimal Reversed);

public sealed record Modelo30Map(int Year, IReadOnlyList<Modelo30Line> Lines, IReadOnlyList<string> Warnings, string Source)
{
    public decimal Total(int group) => Lines.Where(line => line.Group == group).Sum(line => line.Closing);
}

public sealed record ProvisionMapUpdate(int Group, decimal TaxedOpening, decimal TaxedUsed, decimal TaxedReversed);

/// <summary>
/// O Modelo 30 de um exercício (F3-14C): uma linha por conta de perdas por
/// imparidade acumuladas em créditos (219, 229, 239, 269, 279) e em inventários
/// (329 a 369) e de provisões (29). Da contabilidade vêm o saldo inicial (2), a
/// constituição ou reforço — os créditos do exercício (9) — e os débitos: uma
/// reversão quando o lançamento tem uma conta 76, uma utilização nos outros
/// casos. As partes já tributadas (3, 5, 7) e o grupo das provisões indicam-se à
/// mão; (4), (6) e (8) são as diferenças e (10) = (2) − (5) − (6) − (7) − (8) + (9).
/// O grupo é I para as imparidades e, por omissão, II (não dedutíveis) para as
/// provisões — classificar uma provisão como dedutível (III) é decisão do
/// utilizador, pelos artigos 39.º e 40.º do CIRC.
/// </summary>
public sealed class ProvisionMapService(AccountingDbContext context)
{
    public const string Source = "Portaria n.º 92-A/2011, de 28 de fevereiro (impresso e instruções do Modelo 30) — Diário da República n.º 41, 1.ª série, suplemento.";

    private static readonly string[] Prefixes = ["219", "229", "239", "269", "279", "329", "339", "349", "359", "369", "29"];

    public async Task<Modelo30Map?> BuildAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Closing &&
                line.JournalEntry.EntryDate <= yearEnd &&
                (line.Account.Code.StartsWith("219") || line.Account.Code.StartsWith("229") || line.Account.Code.StartsWith("239") ||
                 line.Account.Code.StartsWith("269") || line.Account.Code.StartsWith("279") || line.Account.Code.StartsWith("329") ||
                 line.Account.Code.StartsWith("339") || line.Account.Code.StartsWith("349") || line.Account.Code.StartsWith("359") ||
                 line.Account.Code.StartsWith("369") || line.Account.Code.StartsWith("29")))
            .Select(line => new
            {
                line.Account.Code,
                line.Account.Name,
                line.JournalEntry.EntryDate,
                line.JournalEntry.Source,
                line.Debit,
                line.Credit,
                HasReversal = line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("76")),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var inputs = await context.ProvisionMapInputs
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Year == year)
            .ToDictionaryAsync(item => item.AccountCode, cancellationToken)
            .ConfigureAwait(false);

        var warnings = new List<string>();
        var lines = new List<Modelo30Line>();
        foreach (var account in rows.GroupBy(row => (row.Code, row.Name)).OrderBy(group => group.Key.Code, StringComparer.Ordinal))
        {
            // O saldo inicial: o que vem de antes do exercício e a abertura do próprio exercício.
            var opening = account.Where(row => row.EntryDate < yearStart || row.Source == EntrySource.Opening).Sum(row => row.Credit - row.Debit);
            var inYear = account.Where(row => row.EntryDate >= yearStart && row.Source != EntrySource.Opening).ToList();
            var constituted = inYear.Sum(row => row.Credit);
            var reversed = inYear.Where(row => row.HasReversal).Sum(row => row.Debit);
            var used = inYear.Where(row => !row.HasReversal).Sum(row => row.Debit);
            if (opening == 0m && constituted == 0m && reversed == 0m && used == 0m)
            {
                continue;
            }

            inputs.TryGetValue(account.Key.Code, out var input);
            var isProvision = account.Key.Code.StartsWith("29", StringComparison.Ordinal);
            var group = input?.Group ?? (isProvision ? 2 : 1);
            var taxedOpening = input?.TaxedOpening ?? 0m;
            var taxedUsed = input?.TaxedUsed ?? 0m;
            var taxedReversed = input?.TaxedReversed ?? 0m;
            if (taxedOpening > opening || taxedUsed > used || taxedReversed > reversed)
            {
                warnings.Add($"Na conta {account.Key.Code}, uma parte tributada indicada é maior do que o valor da contabilidade.");
            }

            lines.Add(new Modelo30Line(
                account.Key.Code, account.Key.Name, group, input is not null,
                opening, taxedOpening, opening - taxedOpening,
                taxedUsed, used - taxedUsed,
                taxedReversed, reversed - taxedReversed,
                constituted,
                opening - used - reversed + constituted,
                used, reversed));
        }

        if (lines.Any(line => line.Group == 2 && !line.GroupIndicated))
        {
            warnings.Add("As provisões estão no grupo II (não dedutíveis) por omissão: indique o grupo III nas que são fiscalmente dedutíveis (artigo 39.º do CIRC).");
        }

        if (lines.Count > 0 && !lines.Any(line => line.GroupIndicated))
        {
            warnings.Add("As partes já tributadas (colunas 3, 5 e 7) estão a zero: indique-as a partir do mapa do exercício anterior e do quadro 07 da declaração Modelo 22.");
        }

        return new Modelo30Map(year, lines, warnings, Source);
    }

    public async Task<bool> UpdateAsync(
        Guid userId, Guid companyId, int year, string accountCode, ProvisionMapUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var code = (accountCode ?? string.Empty).Trim();
        if (!Prefixes.Any(prefix => code.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new ArgumentException("A conta não é de perdas por imparidade acumuladas em créditos ou inventários nem de provisões.", nameof(accountCode));
        }

        if (update.Group == 1 && code.StartsWith("29", StringComparison.Ordinal))
        {
            throw new ArgumentException("Uma provisão vai para o grupo II ou III.", nameof(update));
        }

        var input = await context.ProvisionMapInputs
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Year == year && item.AccountCode == code, cancellationToken)
            .ConfigureAwait(false);
        if (input is null)
        {
            input = ProvisionMapInput.Create(companyId, year, code);
            context.ProvisionMapInputs.Add(input);
        }

        input.Update(update.Group, update.TaxedOpening, update.TaxedUsed, update.TaxedReversed);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
