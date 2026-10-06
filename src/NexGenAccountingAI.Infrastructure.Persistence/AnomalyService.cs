using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um achado: algo nos lançamentos que merece uma segunda vista, com a chave estável para o poder dispensar.</summary>
/// <param name="Severity"><c>high</c>, <c>medium</c> ou <c>low</c>.</param>
public sealed record AnomalyFinding(
    string Key,
    string Rule,
    string Severity,
    string Title,
    string Detail,
    IReadOnlyList<string> EntryReferences,
    string? AccountCode,
    string? PartyName,
    decimal? Amount,
    DateOnly? Date,
    bool Dismissed,
    string? DismissReason);

public sealed record AnomalyRuleSummary(string Rule, string Label, string Description, int Count);

public sealed record AnomalyReport(
    DateOnly From, DateOnly To, IReadOnlyList<AnomalyFinding> Findings, IReadOnlyList<AnomalyRuleSummary> Rules, bool Truncated);

/// <summary>
/// A deteção de anomalias (F4F-01): regras determinísticas sobre os lançamentos de um período, que
/// apontam o que merece uma segunda vista antes de fechar. Nenhuma regra altera dados nem usa IA, e
/// cada achado leva as razões à vista. O utilizador pode dispensar um achado com uma razão; volta a
/// aparecer se os dados mudarem. Os lançamentos anulados, de abertura e de apuramento ficam de fora.
/// </summary>
public sealed class AnomalyService(AccountingDbContext context)
{
    public const string Duplicate = "duplicado", RepeatedDocument = "documento-repetido", Outlier = "valor-atipico", NoParty = "sem-terceiro", InvertedBalance = "saldo-invertido";

    /// <summary>O máximo de achados por regra (acima disso, o relatório avisa que foi truncado).</summary>
    public const int MaxPerRule = 100;

    /// <summary>Quantos meses de histórico servem de termo de comparação aos valores atípicos.</summary>
    public const int HistoryMonths = 24;

    private const decimal MinimumAmount = 100m;
    private const int MinimumHistory = 8;

    public static IReadOnlyList<(string Rule, string Label, string Description)> Rules { get; } =
    [
        (Duplicate, "Possíveis lançamentos duplicados", "Linhas na mesma conta, no mesmo dia, com o mesmo valor e o mesmo terceiro, em lançamentos diferentes (ou sem terceiro, de 500 € ou mais)."),
        (RepeatedDocument, "Documento repetido", "O mesmo documento do mesmo terceiro em mais do que um lançamento."),
        (Outlier, "Valores fora do habitual", "Linhas de gastos e rendimentos muito acima do que a conta costuma ter (média mais quatro desvios-padrão dos últimos 24 meses, e pelo menos três vezes a média)."),
        (NoParty, "Clientes e fornecedores sem terceiro", "Linhas nas contas 21 e 22 sem terceiro: não entram nas contas correntes nem nas declarações com o NIF da contraparte."),
        (InvertedBalance, "Saldos com o sinal trocado", "Clientes com saldo credor e fornecedores com saldo devedor à data final do período."),
    ];

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<AnomalyReport?> ScanAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, bool includeDismissed, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("A data final não pode ser anterior à inicial.", nameof(to));
        }

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var findings = new List<AnomalyFinding>();
        var truncated = false;
        foreach (var batch in new[]
        {
            await DuplicatesAsync(companyId, from, to, cancellationToken).ConfigureAwait(false),
            await RepeatedDocumentsAsync(companyId, from, to, cancellationToken).ConfigureAwait(false),
            await OutliersAsync(companyId, from, to, cancellationToken).ConfigureAwait(false),
            await MissingPartiesAsync(companyId, from, to, cancellationToken).ConfigureAwait(false),
            await InvertedBalancesAsync(companyId, to, cancellationToken).ConfigureAwait(false),
        })
        {
            truncated |= batch.Count > MaxPerRule;
            findings.AddRange(batch.Take(MaxPerRule));
        }

        var dismissed = await context.AnomalyDismissals
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToDictionaryAsync(item => item.FindingKey, item => item.Reason, cancellationToken)
            .ConfigureAwait(false);
        var marked = findings
            .Select(finding => dismissed.TryGetValue(finding.Key, out var reason) ? finding with { Dismissed = true, DismissReason = reason } : finding)
            .Where(finding => includeDismissed || !finding.Dismissed)
            .OrderBy(finding => finding.Dismissed ? 1 : 0)
            .ThenBy(finding => finding.Severity switch { "high" => 0, "medium" => 1, _ => 2 })
            .ThenByDescending(finding => finding.Amount ?? 0m)
            .ToList();
        var summary = Rules
            .Select(rule => new AnomalyRuleSummary(rule.Rule, rule.Label, rule.Description, marked.Count(finding => finding.Rule == rule.Rule && !finding.Dismissed)))
            .ToList();
        return new AnomalyReport(from, to, marked, summary, truncated);
    }

    /// <summary>Dispensa um achado, com a razão. <c>false</c> sem acesso à empresa.</summary>
    public async Task<bool> DismissAsync(Guid userId, Guid companyId, string key, string? reason, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim();
        if (!await context.AnomalyDismissals.AnyAsync(item => item.CompanyId == companyId && item.FindingKey == normalized, cancellationToken).ConfigureAwait(false))
        {
            context.AnomalyDismissals.Add(AnomalyDismissal.Create(companyId, normalized, reason, userId));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Volta a mostrar um achado dispensado. <c>false</c> sem acesso ou se não estava dispensado.</summary>
    public async Task<bool> RestoreAsync(Guid userId, Guid companyId, string key, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var normalized = (key ?? string.Empty).Trim();
        var row = await context.AnomalyDismissals.FirstOrDefaultAsync(item => item.CompanyId == companyId && item.FindingKey == normalized, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        context.AnomalyDismissals.Remove(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private IQueryable<JournalEntryLine> Lines(Guid companyId) =>
        context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Opening &&
                line.JournalEntry.Source != EntrySource.Closing);

    private async Task<List<AnomalyFinding>> DuplicatesAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var groups = await Lines(companyId)
            .Where(line => line.JournalEntry.EntryDate >= from && line.JournalEntry.EntryDate <= to && line.Debit + line.Credit >= 1m &&
                (line.PartyId != null || line.Debit + line.Credit >= 500m))
            .GroupBy(line => new { line.AccountId, line.PartyId, line.JournalEntry.EntryDate, line.Debit, line.Credit })
            .Where(group => group.Select(line => line.JournalEntryId).Distinct().Count() > 1)
            .Select(group => new { group.Key.AccountId, group.Key.PartyId, group.Key.EntryDate, group.Key.Debit, group.Key.Credit, Count = group.Select(line => line.JournalEntryId).Distinct().Count() })
            .OrderByDescending(group => group.Debit + group.Credit)
            .Take(MaxPerRule + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = new List<AnomalyFinding>();
        foreach (var group in groups)
        {
            var references = await Lines(companyId)
                .Where(line => line.AccountId == group.AccountId && line.PartyId == group.PartyId && line.JournalEntry.EntryDate == group.EntryDate &&
                    line.Debit == group.Debit && line.Credit == group.Credit)
                .Select(line => line.JournalEntry.Reference)
                .Distinct()
                .OrderBy(reference => reference)
                .Take(6)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var account = await context.Accounts.AsNoTracking().Where(item => item.Id == group.AccountId).Select(item => item.Code).FirstAsync(cancellationToken).ConfigureAwait(false);
            var party = group.PartyId is { } partyId ? await context.Parties.AsNoTracking().Where(item => item.Id == partyId).Select(item => item.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) : null;
            var amount = group.Debit + group.Credit;
            result.Add(new AnomalyFinding(
                $"dup:{account}:{group.PartyId}:{group.EntryDate:yyyy-MM-dd}:{group.Debit}:{group.Credit}", Duplicate,
                amount >= 1000m ? "high" : "medium",
                $"{group.Count} linhas iguais na conta {account} em {group.EntryDate:dd/MM/yyyy}",
                $"Valor de {amount:N2} €{(party is null ? string.Empty : $", terceiro {party}")}, nos lançamentos {string.Join(", ", references)}. Confirme que não é o mesmo documento lançado duas vezes.",
                references, account, party, amount, group.EntryDate, false, null));
        }

        // O mesmo par de lançamentos repete-se em todas as linhas: fica um achado só, de preferência o que tem terceiro.
        return result
            .GroupBy(finding => string.Join('|', finding.EntryReferences))
            .Select(group => group.OrderByDescending(finding => finding.PartyName is not null).ThenByDescending(finding => finding.Amount).First())
            .ToList();
    }

    private async Task<List<AnomalyFinding>> RepeatedDocumentsAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var groups = await Lines(companyId)
            .Where(line => line.JournalEntry.EntryDate >= from && line.JournalEntry.EntryDate <= to && line.PartyId != null && line.JournalEntry.DocumentReference != string.Empty)
            .GroupBy(line => new { line.JournalEntry.DocumentReference, line.PartyId })
            .Where(group => group.Select(line => line.JournalEntryId).Distinct().Count() > 1)
            .Select(group => new
            {
                group.Key.DocumentReference,
                group.Key.PartyId,
                Count = group.Select(line => line.JournalEntryId).Distinct().Count(),
                Amount = group.Sum(line => line.Debit + line.Credit),
            })
            .OrderByDescending(group => group.Amount)
            .Take(MaxPerRule + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = new List<AnomalyFinding>();
        foreach (var group in groups)
        {
            var references = await Lines(companyId)
                .Where(line => line.PartyId == group.PartyId && line.JournalEntry.DocumentReference == group.DocumentReference)
                .Select(line => line.JournalEntry.Reference)
                .Distinct()
                .OrderBy(reference => reference)
                .Take(6)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var party = await context.Parties.AsNoTracking().Where(item => item.Id == group.PartyId).Select(item => item.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            result.Add(new AnomalyFinding(
                $"doc:{group.DocumentReference}:{group.PartyId}", RepeatedDocument, "medium",
                $"O documento {group.DocumentReference} aparece em {group.Count} lançamentos",
                $"Terceiro {party}, lançamentos {string.Join(", ", references)}. O mesmo documento só devia ser lançado uma vez.",
                references, null, party, group.Amount, null, false, null));
        }

        return result;
    }

    private async Task<List<AnomalyFinding>> OutliersAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var historyStart = from.AddMonths(-HistoryMonths);
        var history = await Lines(companyId)
            .Where(line => line.JournalEntry.EntryDate >= historyStart && line.JournalEntry.EntryDate < from &&
                (line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7")) && line.Debit + line.Credit > 0m)
            .GroupBy(line => line.AccountId)
            .Select(group => new { AccountId = group.Key, Count = group.Count(), Sum = group.Sum(line => line.Debit + line.Credit), SumSquares = group.Sum(line => (line.Debit + line.Credit) * (line.Debit + line.Credit)) })
            .Where(group => group.Count >= MinimumHistory)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (history.Count == 0)
        {
            return [];
        }

        var limits = history.ToDictionary(item => item.AccountId, item =>
        {
            var mean = (double)item.Sum / item.Count;
            var variance = Math.Max(0d, ((double)item.SumSquares / item.Count) - (mean * mean));
            return (Mean: mean, Std: Math.Sqrt(variance));
        });
        var accountIds = limits.Keys.ToList();
        var candidates = await Lines(companyId)
            .Where(line => line.JournalEntry.EntryDate >= from && line.JournalEntry.EntryDate <= to && accountIds.Contains(line.AccountId) && line.Debit + line.Credit >= MinimumAmount)
            .Select(line => new { line.AccountId, AccountCode = line.Account.Code, line.JournalEntry.Reference, line.JournalEntry.EntryDate, Amount = line.Debit + line.Credit, PartyName = line.Party == null ? null : line.Party.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = new List<AnomalyFinding>();
        foreach (var line in candidates)
        {
            var (mean, std) = limits[line.AccountId];
            var amount = (double)line.Amount;
            if (std <= 0d || amount <= mean + (4d * std) || amount < 3d * mean)
            {
                continue;
            }

            result.Add(new AnomalyFinding(
                $"out:{line.Reference}:{line.AccountCode}:{line.Amount}", Outlier,
                amount > mean + (8d * std) ? "high" : "medium",
                $"Valor de {line.Amount:N2} € na conta {line.AccountCode}, muito acima do habitual",
                $"Nos últimos {HistoryMonths} meses esta conta teve em média {mean:N2} € por linha (desvio-padrão {std:N2}). Lançamento {line.Reference} de {line.EntryDate:dd/MM/yyyy}{(line.PartyName is null ? string.Empty : $", {line.PartyName}")}.",
                [line.Reference], line.AccountCode, line.PartyName, line.Amount, line.EntryDate, false, null));
        }

        return [.. result.OrderByDescending(finding => finding.Amount)];
    }

    private async Task<List<AnomalyFinding>> MissingPartiesAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await Lines(companyId)
            .Where(line => line.JournalEntry.EntryDate >= from && line.JournalEntry.EntryDate <= to && line.PartyId == null &&
                (line.Account.Code.StartsWith("21") || line.Account.Code.StartsWith("22")) && line.Debit + line.Credit > 0m)
            .Select(line => new { AccountCode = line.Account.Code, line.JournalEntry.Reference, line.JournalEntry.EntryDate, Amount = line.Debit + line.Credit })
            .OrderByDescending(line => line.Amount)
            .Take(MaxPerRule + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => new AnomalyFinding(
            $"party:{row.Reference}:{row.AccountCode}:{row.Amount}", NoParty, "medium",
            $"Linha de {row.Amount:N2} € na conta {row.AccountCode} sem terceiro",
            $"Lançamento {row.Reference} de {row.EntryDate:dd/MM/yyyy}. Sem terceiro, a linha não entra na conta corrente do cliente ou do fornecedor.",
            [row.Reference], row.AccountCode, null, row.Amount, row.EntryDate, false, null)).ToList();
    }

    private async Task<List<AnomalyFinding>> InvertedBalancesAsync(Guid companyId, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await Lines(companyId)
            .Where(line => line.JournalEntry.EntryDate <= to && line.PartyId != null && (line.Account.Code.StartsWith("21") || line.Account.Code.StartsWith("22")))
            .GroupBy(line => new { PartyId = line.PartyId!.Value, Customer = line.Account.Code.StartsWith("21") })
            .Select(group => new { group.Key.PartyId, group.Key.Customer, Balance = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // Cliente (21): o saldo normal é devedor. Fornecedor (22): o saldo normal é credor.
        var inverted = rows.Where(row => row.Customer ? row.Balance < -1m : row.Balance > 1m).OrderByDescending(row => Math.Abs(row.Balance)).Take(MaxPerRule + 1).ToList();
        var ids = inverted.Select(row => row.PartyId).ToList();
        var names = await context.Parties.AsNoTracking().Where(party => ids.Contains(party.Id)).ToDictionaryAsync(party => party.Id, party => party.Name, cancellationToken).ConfigureAwait(false);
        return inverted.Select(row => new AnomalyFinding(
            $"saldo:{row.PartyId}:{(row.Customer ? "c" : "f")}", InvertedBalance, Math.Abs(row.Balance) >= 1000m ? "medium" : "low",
            row.Customer ? $"Cliente {names.GetValueOrDefault(row.PartyId)} com saldo credor de {Math.Abs(row.Balance):N2} €" : $"Fornecedor {names.GetValueOrDefault(row.PartyId)} com saldo devedor de {Math.Abs(row.Balance):N2} €",
            row.Customer
                ? "Um cliente com saldo a favor pode ser um adiantamento, uma nota de crédito por regularizar ou um recebimento lançado no terceiro errado."
                : "Um fornecedor com saldo a seu favor pode ser um pagamento a mais, um adiantamento ou uma fatura por lançar.",
            [], null, names.GetValueOrDefault(row.PartyId), Math.Abs(row.Balance), to, false, null)).ToList();
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(company => company.Id == companyId && company.IsActive &&
                company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive), cancellationToken);
}
