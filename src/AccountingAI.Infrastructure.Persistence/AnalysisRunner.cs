using System.Globalization;
using AccountingAI.Application.Reporting;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>
/// Corre análises. Para os <c>Movimentos</c>, o SQL faz o filtro de período e a
/// projeção das linhas; para os <c>SaldosDeConta</c> reaproveita o balancete
/// (<see cref="AccountLedgerService.TrialBalanceAsync"/>). Em ambos, a memória
/// aplica os filtros da definição (com paragem no limite de
/// <see cref="AnalysisResult.RowLimit"/>), a ordenação, os agrupamentos com
/// subtotais em todos os níveis e o total geral. Só lê; exclui os anulados.
/// </summary>
public sealed class AnalysisRunner(AccountingDbContext context, AccountLedgerService ledger) : IAnalysisRunner
{
    public async Task<AnalysisResult?> RunAsync(
        Guid userId,
        Guid companyId,
        AnalysisDefinition definition,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = AnalysisFieldCatalog.Validate(definition);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(definition));
        }

        var hasAccess = await context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(m => m.UserId == userId && m.IsActive),
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var (rows, truncated) = definition.Source switch
        {
            AnalysisSource.Movimentos => await CollectMovementsAsync(companyId, definition, from, to, cancellationToken).ConfigureAwait(false),
            AnalysisSource.SaldosDeConta => CollectAccountBalances(await ledger.TrialBalanceAsync(companyId, from, to, cancellationToken).ConfigureAwait(false), definition),
            _ => throw new ArgumentException($"Origem ainda não suportada pelo runner: {definition.Source}.", nameof(definition)),
        };

        var sorted = Sort(rows, definition);
        var columns = definition.Columns
            .Select(key => AnalysisFieldCatalog.Field(definition.Source, key)!)
            .Select(field => new AnalysisColumn(field.Key, field.Label, field.Type.ToString()))
            .ToList();

        var groups = BuildGroups(sorted, definition);
        var grandTotal = Aggregate(sorted, definition.Aggregates);

        return new AnalysisResult(columns, sorted, groups, grandTotal, sorted.Count, truncated);
    }

    private async Task<(List<IReadOnlyDictionary<string, object?>> Rows, bool Truncated)> CollectMovementsAsync(
        Guid companyId, AnalysisDefinition definition, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.JournalEntry.Status != JournalEntryStatus.Voided);

        if (from is { } lower)
        {
            query = query.Where(line => line.JournalEntry.EntryDate >= lower);
        }

        if (to is { } upper)
        {
            query = query.Where(line => line.JournalEntry.EntryDate <= upper);
        }

        var projected = query
            .OrderBy(line => line.JournalEntry.EntryDate)
            .ThenBy(line => line.JournalEntry.Reference)
            .ThenBy(line => line.Sequence)
            .Select(line => new Flat
            {
                EntryDate = line.JournalEntry.EntryDate,
                JournalCode = line.JournalEntry.Journal.Code,
                JournalName = line.JournalEntry.Journal.Name,
                Reference = line.JournalEntry.Reference,
                DocumentType = line.JournalEntry.DocumentType != null ? line.JournalEntry.DocumentType.Code : null,
                DocumentReference = line.JournalEntry.DocumentReference,
                Description = line.JournalEntry.Description,
                Status = line.JournalEntry.Status,
                Source = line.JournalEntry.Source,
                AccountCode = line.Account.Code,
                AccountName = line.Account.Name,
                LineDescription = line.Description,
                Debit = line.Debit,
                Credit = line.Credit,
            });

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var truncated = false;
        await foreach (var flat in projected.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var values = ToValues(flat);
            if (!definition.Filters.All(filter => Matches(values, filter)))
            {
                continue;
            }

            if (rows.Count >= AnalysisResult.RowLimit)
            {
                truncated = true;
                break;
            }

            rows.Add(values);
        }

        return (rows, truncated);
    }

    private static (List<IReadOnlyDictionary<string, object?>> Rows, bool Truncated) CollectAccountBalances(
        TrialBalance balance, AnalysisDefinition definition)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var truncated = false;
        foreach (var row in balance.Rows)
        {
            var values = new Dictionary<string, object?>
            {
                ["accountCode"] = row.AccountCode,
                ["accountName"] = row.AccountName,
                ["class"] = row.AccountCode.Length > 0 ? row.AccountCode[..1] : string.Empty,
                ["openingBalance"] = decimal.Round(row.OpeningBalance, 2),
                ["debit"] = decimal.Round(row.Debit, 2),
                ["credit"] = decimal.Round(row.Credit, 2),
                ["balance"] = decimal.Round(row.Balance, 2),
            };

            if (!definition.Filters.All(filter => Matches(values, filter)))
            {
                continue;
            }

            if (rows.Count >= AnalysisResult.RowLimit)
            {
                truncated = true;
                break;
            }

            rows.Add(values);
        }

        return (rows, truncated);
    }

    private static IReadOnlyDictionary<string, object?> ToValues(Flat flat) => new Dictionary<string, object?>
    {
        ["entryDate"] = flat.EntryDate,
        ["year"] = flat.EntryDate.Year,
        ["month"] = flat.EntryDate.Month,
        ["journalCode"] = flat.JournalCode,
        ["journalName"] = flat.JournalName,
        ["reference"] = flat.Reference,
        ["documentType"] = flat.DocumentType ?? string.Empty,
        ["documentReference"] = flat.DocumentReference,
        ["description"] = flat.Description,
        ["status"] = StatusLabel(flat.Status),
        ["source"] = SourceLabel(flat.Source),
        ["accountCode"] = flat.AccountCode,
        ["accountName"] = flat.AccountName,
        ["lineDescription"] = flat.LineDescription,
        ["debit"] = flat.Debit,
        ["credit"] = flat.Credit,
        ["amount"] = decimal.Round(flat.Debit - flat.Credit, 2),
    };

    private static string StatusLabel(JournalEntryStatus status) => status switch
    {
        JournalEntryStatus.Reviewed => "Conferido",
        JournalEntryStatus.Voided => "Anulado",
        _ => "Por conferir",
    };

    private static string SourceLabel(EntrySource source) => source switch
    {
        EntrySource.SaftImport => "SAF-T",
        EntrySource.Opening => "Abertura",
        EntrySource.Closing => "Apuramento",
        _ => "Manual",
    };

    private static bool Matches(IReadOnlyDictionary<string, object?> values, AnalysisFilter filter)
    {
        var actual = values.GetValueOrDefault(filter.Field);
        var op = filter.Operator.ToLowerInvariant();

        if (actual is decimal number)
        {
            if (!decimal.TryParse(filter.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var target))
            {
                return false;
            }

            return op switch
            {
                "eq" => number == target,
                "neq" => number != target,
                "gt" => number > target,
                "gte" => number >= target,
                "lt" => number < target,
                "lte" => number <= target,
                _ => false,
            };
        }

        if (actual is int integer)
        {
            if (!int.TryParse(filter.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
            {
                return false;
            }

            return op switch
            {
                "eq" => integer == target,
                "neq" => integer != target,
                "gt" => integer > target,
                "gte" => integer >= target,
                "lt" => integer < target,
                "lte" => integer <= target,
                _ => false,
            };
        }

        if (actual is DateOnly date)
        {
            if (!DateOnly.TryParse(filter.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var target))
            {
                return false;
            }

            return op switch
            {
                "eq" => date == target,
                "gte" => date >= target,
                "lte" => date <= target,
                _ => false,
            };
        }

        var text = actual?.ToString() ?? string.Empty;
        return op switch
        {
            "eq" => string.Equals(text, filter.Value, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(text, filter.Value, StringComparison.OrdinalIgnoreCase),
            "contains" => text.Contains(filter.Value, StringComparison.OrdinalIgnoreCase),
            "startswith" => text.StartsWith(filter.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static List<IReadOnlyDictionary<string, object?>> Sort(
        List<IReadOnlyDictionary<string, object?>> rows, AnalysisDefinition definition)
    {
        // Os campos de agrupamento ordenam primeiro (para os subtotais), depois a ordenação da definição.
        var keys = definition.GroupBy
            .Select(field => (Field: field, Descending: false))
            .Concat(definition.Sort.Select(sort => (sort.Field, sort.Descending)))
            .ToList();
        if (keys.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<IReadOnlyDictionary<string, object?>>? ordered = null;
        foreach (var (field, descending) in keys)
        {
            static object? Key(IReadOnlyDictionary<string, object?> row, string f) => row.GetValueOrDefault(f);
            if (ordered is null)
            {
                ordered = descending
                    ? rows.OrderByDescending(row => Key(row, field), RowComparer.Instance)
                    : rows.OrderBy(row => Key(row, field), RowComparer.Instance);
            }
            else
            {
                ordered = descending
                    ? ordered.ThenByDescending(row => Key(row, field), RowComparer.Instance)
                    : ordered.ThenBy(row => Key(row, field), RowComparer.Instance);
            }
        }

        return ordered!.ToList();
    }

    private static IReadOnlyList<AnalysisGroup> BuildGroups(
        List<IReadOnlyDictionary<string, object?>> rows, AnalysisDefinition definition)
    {
        if (definition.GroupBy.Count == 0 || rows.Count == 0)
        {
            return [];
        }

        var groups = new List<AnalysisGroup>();
        for (var level = 1; level <= definition.GroupBy.Count; level++)
        {
            var fields = definition.GroupBy.Take(level).ToList();
            IReadOnlyList<object?>? currentKey = null;
            var bucket = new List<IReadOnlyDictionary<string, object?>>();

            void Flush()
            {
                if (currentKey is null)
                {
                    return;
                }

                var keyParts = fields
                    .Zip(currentKey, (field, value) => new AnalysisGroupKey(
                        field, AnalysisFieldCatalog.Field(definition.Source, field)!.Label, value))
                    .ToList();
                groups.Add(new AnalysisGroup(keyParts, Aggregate(bucket, definition.Aggregates), bucket.Count));
            }

            foreach (var row in rows)
            {
                var key = fields.Select(field => row.GetValueOrDefault(field)).ToList();
                if (currentKey is null || !key.SequenceEqual(currentKey))
                {
                    Flush();
                    currentKey = key;
                    bucket = [];
                }

                bucket.Add(row);
            }

            Flush();
        }

        return groups;
    }

    private static IReadOnlyDictionary<string, decimal> Aggregate(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<AnalysisAggregate> aggregates)
    {
        var result = new Dictionary<string, decimal>();
        foreach (var aggregate in aggregates)
        {
            var key = $"{aggregate.Field}:{aggregate.Function.ToString().ToLowerInvariant()}";
            var numbers = rows
                .Select(row => row.GetValueOrDefault(aggregate.Field))
                .Select(value => value switch
                {
                    decimal d => d,
                    int i => i,
                    _ => (decimal?)null,
                })
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .ToList();

            result[key] = aggregate.Function switch
            {
                AnalysisAggregateFunction.Count => rows.Count,
                AnalysisAggregateFunction.Sum => decimal.Round(numbers.Sum(), 2),
                AnalysisAggregateFunction.Average => numbers.Count == 0 ? 0m : decimal.Round(numbers.Average(), 2),
                AnalysisAggregateFunction.Min => numbers.Count == 0 ? 0m : numbers.Min(),
                AnalysisAggregateFunction.Max => numbers.Count == 0 ? 0m : numbers.Max(),
                _ => 0m,
            };
        }

        return result;
    }

    private sealed class Flat
    {
        public DateOnly EntryDate { get; init; }
        public string JournalCode { get; init; } = string.Empty;
        public string JournalName { get; init; } = string.Empty;
        public string Reference { get; init; } = string.Empty;
        public string? DocumentType { get; init; }
        public string DocumentReference { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public JournalEntryStatus Status { get; init; }
        public EntrySource Source { get; init; }
        public string AccountCode { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public string LineDescription { get; init; } = string.Empty;
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
    }

    private sealed class RowComparer : IComparer<object?>
    {
        public static readonly RowComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x is IComparable comparable && x.GetType() == y.GetType())
            {
                return comparable.CompareTo(y);
            }

            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
