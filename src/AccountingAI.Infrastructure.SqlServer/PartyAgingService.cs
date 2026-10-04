using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>O saldo em aberto de um terceiro, repartido pela idade dos documentos.</summary>
/// <param name="Open">O que está em aberto (a receber de um cliente, a pagar a um fornecedor).</param>
/// <param name="Buckets">O saldo em aberto por intervalo de idade, pela ordem de <see cref="PartyAging.BucketLabels"/>.</param>
/// <param name="Advance">O saldo ao contrário: o que o cliente adiantou, ou o que se adiantou ao fornecedor.</param>
/// <param name="OldestDate">A data do documento mais antigo ainda em aberto.</param>
public sealed record PartyAgingRow(
    Guid PartyId, string CountryCode, string TaxRegistrationNumber, string Name,
    decimal Open, IReadOnlyList<decimal> Buckets, decimal Advance, DateOnly? OldestDate);

/// <param name="Kind"><c>clientes</c> ou <c>fornecedores</c>.</param>
public sealed record PartyAging(
    DateOnly Date,
    string Kind,
    IReadOnlyList<string> BucketLabels,
    IReadOnlyList<PartyAgingRow> Rows,
    decimal TotalOpen,
    IReadOnlyList<decimal> TotalBuckets,
    decimal TotalAdvance,
    string Disclaimer);

/// <summary>
/// A idade dos saldos de clientes (contas 21) ou de fornecedores (contas 22)
/// a uma data. A aplicação não casa cada pagamento com a sua fatura, pelo que
/// a antiguidade é calculada por ordem cronológica: os pagamentos de um
/// terceiro abatem aos seus documentos mais antigos, e o que sobra em aberto
/// é repartido pela idade — contada da data do documento (ou do lançamento,
/// se o documento não a tem) até à data pedida. Só lê; exclui os anulados; só
/// vê as linhas com terceiro.
/// </summary>
public sealed class PartyAgingService(AccountingDbContext context)
{
    public const string Customers = "clientes";

    public const string Suppliers = "fornecedores";

    public const string Disclaimer =
        "A idade é contada da data do documento, não do vencimento, e os pagamentos são abatidos aos documentos mais antigos de cada terceiro " +
        "(a aplicação não casa pagamentos com faturas). Só entram as linhas das contas 21 ou 22 com terceiro indicado.";

    private static readonly (int UpTo, string Label)[] Buckets =
    [
        (30, "Até 30 dias"), (60, "31 a 60 dias"), (90, "61 a 90 dias"), (180, "91 a 180 dias"), (int.MaxValue, "Mais de 180 dias"),
    ];

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PartyAging?> BuildAsync(
        Guid userId, Guid companyId, DateOnly date, string kind, CancellationToken cancellationToken = default)
    {
        if (kind is not (Customers or Suppliers))
        {
            throw new ArgumentException("Indique «clientes» ou «fornecedores».", nameof(kind));
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

        var customers = kind == Customers;
        var prefix = customers ? "21" : "22";
        var lines = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.PartyId != null &&
                line.Account.Code.StartsWith(prefix) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate <= date)
            .Select(line => new
            {
                PartyId = line.PartyId!.Value,
                Date = line.JournalEntry.DocumentDate ?? line.JournalEntry.EntryDate,
                line.Debit,
                line.Credit,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var partyIds = lines.Select(line => line.PartyId).Distinct().ToList();
        var parties = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && partyIds.Contains(party.Id))
            .ToDictionaryAsync(party => party.Id, party => new { party.CountryCode, party.TaxRegistrationNumber, party.Name }, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<PartyAgingRow>();
        foreach (var group in lines.GroupBy(line => line.PartyId))
        {
            // Para um cliente os documentos são os débitos e os pagamentos os créditos; para um fornecedor, ao contrário.
            var documents = group
                .Select(line => (line.Date, Amount: customers ? line.Debit : line.Credit))
                .Where(item => item.Amount != 0m)
                .OrderBy(item => item.Date)
                .ToList();
            var settled = group.Sum(line => customers ? line.Credit : line.Debit);

            var buckets = new decimal[Buckets.Length];
            DateOnly? oldest = null;
            foreach (var (documentDate, amount) in documents)
            {
                var paid = Math.Min(amount, settled);
                settled -= paid;
                var open = amount - paid;
                if (open == 0m)
                {
                    continue;
                }

                oldest ??= documentDate;
                var age = Math.Max(0, date.DayNumber - documentDate.DayNumber);
                buckets[Array.FindIndex(Buckets, bucket => age <= bucket.UpTo)] += open;
            }

            var total = buckets.Sum();
            if (total == 0m && settled == 0m)
            {
                continue;
            }

            var party = parties[group.Key];
            rows.Add(new PartyAgingRow(
                group.Key, party.CountryCode, party.TaxRegistrationNumber, party.Name, total, buckets, settled, oldest));
        }

        rows = rows
            .OrderByDescending(row => row.Open)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
        return new PartyAging(
            date, kind, Buckets.Select(bucket => bucket.Label).ToList(), rows,
            rows.Sum(row => row.Open),
            Enumerable.Range(0, Buckets.Length).Select(index => rows.Sum(row => row.Buckets[index])).ToList(),
            rows.Sum(row => row.Advance),
            Disclaimer);
    }
}
