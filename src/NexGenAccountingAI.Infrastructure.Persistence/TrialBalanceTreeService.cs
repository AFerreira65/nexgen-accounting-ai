using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O que se pede ao balancete em árvore.</summary>
/// <param name="ColumnSet">Um dos <see cref="MapColumnSets"/> (por omissão o balancete clássico).</param>
/// <param name="Parent">Só as contas diretamente por baixo desta.</param>
/// <param name="Depth">Todas as contas até este nível (1 = classes). Sem <paramref name="Parent"/> nem pesquisa, por omissão 1.</param>
/// <param name="Search">Contas cujo código começa pelo texto ou cujo nome o contém; a resposta deixa de ser uma árvore.</param>
/// <param name="Everything">Para exportar: sem o limite de linhas do ecrã.</param>
public sealed record TrialBalanceTreeQuery(
    int Year,
    int FromMonth,
    int ToMonth,
    bool IncludeClosing = true,
    string? ColumnSet = null,
    string? Parent = null,
    int? Depth = null,
    string? Search = null,
    bool Everything = false);

public sealed record TrialBalanceTreeView(
    IReadOnlyList<MapColumn> Columns,
    IReadOnlyList<TrialBalanceNode> Rows,
    IReadOnlyList<decimal?> Totals,
    decimal TotalDebit,
    decimal TotalCredit,
    int AccountCount,
    int MaxDepth,
    bool Truncated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// O balancete em árvore de uma empresa (F4B-02), com as colunas à escolha. Calcula a
/// árvore toda no servidor e devolve só a parte pedida — as classes, um ramo, até um
/// nível, ou o resultado de uma pesquisa — para o ecrã nunca receber dezenas de milhares
/// de linhas de uma vez.
/// </summary>
public sealed class TrialBalanceTreeService(AccountingDbContext context, MapDataProvider data)
{
    /// <summary>O máximo de linhas devolvidas ao ecrã de cada vez.</summary>
    public const int RowLimit = 3000;

    /// <summary>Não verifica o acesso à empresa: é de quem chama.</summary>
    /// <exception cref="ArgumentException">O período ou o conjunto de colunas não são válidos.</exception>
    public async Task<TrialBalanceTreeView> RunAsync(
        Guid companyId, TrialBalanceTreeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Year is < 1990 or > 2100)
        {
            throw new ArgumentException("O exercício não é válido.", nameof(query));
        }

        if (query.FromMonth is < 1 or > 12 || query.ToMonth is < 1 or > 12 || query.ToMonth < query.FromMonth)
        {
            throw new ArgumentException("O período tem de ser de um mês a outro do mesmo exercício.", nameof(query));
        }

        var set = MapColumnSets.Find(string.IsNullOrWhiteSpace(query.ColumnSet) ? MapColumnSets.Classic : query.ColumnSet)
            ?? throw new ArgumentException($"Não existe o conjunto de colunas «{query.ColumnSet}».", nameof(query));
        var request = new MapRequest(query.Year, query.FromMonth, query.ToMonth, query.IncludeClosing);
        var years = MapEngine.YearsNeeded(new MapDefinition([], set.Columns), request);

        var accounts = await data.LoadAsync(companyId, [.. years], cancellationToken).ConfigureAwait(false);
        var chart = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .Select(account => new { account.Code, account.Name })
            .ToDictionaryAsync(account => account.Code, account => account.Name, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var tree = TrialBalanceTree.Build(accounts, chart, set.Columns, request);

        IEnumerable<TrialBalanceNode> rows = tree.Nodes;
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            rows = rows.Where(node =>
                node.Code.StartsWith(search, StringComparison.OrdinalIgnoreCase) ||
                node.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(query.Parent))
        {
            var parent = query.Parent.Trim();
            rows = query.Depth is { } below
                ? rows.Where(node => node.Code.Length > parent.Length && node.Code.StartsWith(parent, StringComparison.Ordinal) && node.Depth <= below)
                : rows.Where(node => node.ParentCode == parent);
        }
        else if (!query.Everything || query.Depth is not null)
        {
            var depth = Math.Max(1, query.Depth ?? 1);
            rows = rows.Where(node => node.Depth <= depth);
        }

        var selected = rows.ToList();
        var truncated = !query.Everything && selected.Count > RowLimit;
        if (truncated)
        {
            selected = selected.GetRange(0, RowLimit);
        }

        var warnings = new List<string>();
        if (await LacksOpeningAsync(companyId, query.Year, cancellationToken).ConfigureAwait(false))
        {
            warnings.Add($"O exercício de {query.Year} ainda não tem lançamento de abertura: os saldos iniciais das contas de balanço são os acumulados dos exercícios anteriores, e o resultado desses exercícios não está transitado.");
        }

        return new TrialBalanceTreeView(
            tree.Columns,
            selected,
            tree.Totals,
            tree.TotalDebit,
            tree.TotalCredit,
            tree.Nodes.Count(node => node.IsPosting),
            tree.Nodes.Count == 0 ? 0 : tree.Nodes.Max(node => node.Depth),
            truncated,
            warnings);
    }

    /// <summary>O exercício não tem abertura lançada, mas há movimentos de exercícios anteriores.</summary>
    private async Task<bool> LacksOpeningAsync(Guid companyId, int year, CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var entries = context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId && entry.Status != JournalEntryStatus.Voided);
        var hasOpening = await entries
            .AnyAsync(entry => entry.Source == EntrySource.Opening && entry.EntryDate >= start && entry.EntryDate <= end, cancellationToken)
            .ConfigureAwait(false);
        return !hasOpening && await entries.AnyAsync(entry => entry.EntryDate < start, cancellationToken).ConfigureAwait(false);
    }
}
