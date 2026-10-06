using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Corre as listagens do utilizador (F4D-01) sobre a origem «linhas de lançamentos». A
/// definição é só dados: o servidor traduz cada filtro, ordenação e agrupamento para
/// expressões LINQ tipadas, que o EF converte em SQL com parâmetros; nunca se junta texto
/// do utilizador a uma consulta. O filtro de empresa vem do chamador (a sessão), nunca da
/// definição. Os lançamentos anulados ficam de fora, salvo se a definição filtrar pelo estado.
/// <para>
/// Com agrupamento, cada pedido traz um nível: os grupos (com contagem e somas) ou, no último
/// nível, as linhas; os níveis abrem-se um a um, como no balancete em árvore.
/// </para>
/// </summary>
public sealed class ListingEngine(AccountingDbContext context)
{
    public const int MaxPageSize = 1000;

    /// <summary>O máximo de linhas numa exportação.</summary>
    public const int MaxExportRows = 100_000;

    private static readonly MethodInfo ILikeMethod = typeof(NpgsqlDbFunctionsExtensions)
        .GetMethod(nameof(NpgsqlDbFunctionsExtensions.ILike), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    /// <summary>A linha plana sobre que as listagens trabalham.</summary>
    private sealed class LineRow
    {
        public Guid Id { get; set; }

        public int Sequence { get; set; }

        public DateOnly Date { get; set; }

        public string Reference { get; set; } = string.Empty;

        public string JournalCode { get; set; } = string.Empty;

        public string DocumentReference { get; set; } = string.Empty;

        public JournalEntryStatus Status { get; set; }

        public string AccountCode { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public Guid? PartyId { get; set; }

        public string? PartyName { get; set; }

        public string? PartyTax { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? CostCenter { get; set; }

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }
    }

    private sealed class GroupAggregate
    {
        public string? Key { get; set; }

        public int Count { get; set; }

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }
    }

    private static Expression<Func<LineRow, T>> Select<T>(Expression<Func<LineRow, T>> expression) => expression;

    // O que cada campo é, em SQL: o valor mostrado, filtrado e ordenado.
    private static readonly Dictionary<string, LambdaExpression> Values = new(StringComparer.Ordinal)
    {
        ["data"] = Select(row => row.Date),
        ["lancamento"] = Select(row => row.Reference),
        ["diario"] = Select(row => row.JournalCode),
        ["documento"] = Select(row => row.DocumentReference),
        ["estado"] = Select(row => row.Status == JournalEntryStatus.Voided ? "Anulado" : row.Status == JournalEntryStatus.Reviewed ? "Revisto" : "Por rever"),
        ["conta"] = Select(row => row.AccountCode),
        ["nomeConta"] = Select(row => row.AccountName),
        ["classe"] = Select(row => row.AccountCode.Substring(0, 1)),
        ["terceiro"] = Select(row => row.PartyName),
        ["nif"] = Select(row => row.PartyTax),
        ["descricao"] = Select(row => row.Description),
        ["centroCusto"] = Select(row => row.CostCenter),
        ["ano"] = Select(row => row.Date.Year),
        ["mes"] = Select(row => row.Date.Month),
        ["debito"] = Select(row => row.Debit),
        ["credito"] = Select(row => row.Credit),
        ["saldo"] = Select(row => row.Debit - row.Credit),
    };

    // A chave de agrupamento é sempre texto; os inteiros passam por ToString.
    private static readonly Dictionary<string, Expression<Func<LineRow, string?>>> GroupKeys = new(StringComparer.Ordinal)
    {
        ["diario"] = row => row.JournalCode,
        ["estado"] = row => row.Status == JournalEntryStatus.Voided ? "Anulado" : row.Status == JournalEntryStatus.Reviewed ? "Revisto" : "Por rever",
        ["conta"] = row => row.AccountCode,
        ["nomeConta"] = row => row.AccountName,
        ["classe"] = row => row.AccountCode.Substring(0, 1),
        ["terceiro"] = row => row.PartyName,
        ["centroCusto"] = row => row.CostCenter,
        ["ano"] = row => row.Date.Year.ToString(),
        ["mes"] = row => row.Date.Month.ToString(),
    };

    private IQueryable<LineRow> Base(Guid companyId) =>
        context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId)
            .Select(line => new LineRow
            {
                Id = line.Id,
                Sequence = line.Sequence,
                Date = line.JournalEntry.EntryDate,
                Reference = line.JournalEntry.Reference,
                JournalCode = line.JournalEntry.Journal.Code,
                DocumentReference = line.JournalEntry.DocumentReference,
                Status = line.JournalEntry.Status,
                AccountCode = line.Account.Code,
                AccountName = line.Account.Name,
                PartyId = line.PartyId,
                PartyName = line.Party == null ? null : line.Party.Name,
                PartyTax = line.Party == null ? null : line.Party.TaxRegistrationNumber,
                Description = line.Description,
                CostCenter = line.CostCenter == null ? null : line.CostCenter.Name,
                Debit = line.Debit,
                Credit = line.Credit,
            });

    /// <summary>Corre a definição para a empresa (já verificado o acesso).</summary>
    /// <exception cref="ArgumentException">A definição ou o pedido não são válidos (mensagem em português).</exception>
    public async Task<ListingResult> RunAsync(
        Guid companyId, ListingDefinition definition, ListingRequest request, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);
        var problems = ListingCatalog.Validate(definition);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(definition));
        }

        var source = ListingCatalog.Source(definition.Source)!;
        var groupBy = definition.GroupBy ?? [];
        var path = request.GroupPath ?? [];
        if (path.Count > groupBy.Count)
        {
            throw new ArgumentException("O caminho dos grupos é mais fundo do que o agrupamento.", nameof(request));
        }

        var query = Filtered(companyId, definition, request, today);
        for (var level = 0; level < path.Count; level++)
        {
            query = query.Where(KeyEquals(GroupKeys[groupBy[level]], path[level]));
        }

        var columns = definition.Columns
            .Select(key => source.Field(key)!)
            .Select(field => new ListingColumn(field.Key, field.Label, field.Type.ToString().ToLowerInvariant(), field.Measure))
            .ToList();

        // Totais do que está aberto, em três agregados.
        var totalRows = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var totalDebit = await query.SumAsync(row => (decimal?)row.Debit, cancellationToken).ConfigureAwait(false) ?? 0m;
        var totalCredit = await query.SumAsync(row => (decimal?)row.Credit, cancellationToken).ConfigureAwait(false) ?? 0m;
        var totals = Sums(totalDebit, totalCredit);

        if (path.Count < groupBy.Count)
        {
            var key = GroupKeys[groupBy[path.Count]];
            var aggregates = await query
                .GroupBy(key)
                .Select(group => new GroupAggregate { Key = group.Key, Count = group.Count(), Debit = group.Sum(row => row.Debit), Credit = group.Sum(row => row.Credit) })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var numeric = groupBy[path.Count] is "ano" or "mes";
            var groups = aggregates
                .OrderBy(item => item.Key is null ? 1 : 0)
                .ThenBy(item => numeric && int.TryParse(item.Key, out var number) ? number : 0)
                .ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new ListingGroup(item.Key, item.Key ?? "(sem valor)", item.Count, Sums(item.Debit, item.Credit)))
                .ToList();
            return new ListingResult("groups", columns, groupBy, groups, [], totalRows, totals, []);
        }

        var take = Math.Clamp(request.Take, 1, MaxPageSize);
        var rows = await Sorted(query, definition.Sort)
            .Skip(Math.Max(0, request.Skip))
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ListingResult("rows", columns, groupBy, [], Project(rows, definition.Columns), totalRows, totals, []);
    }

    /// <summary>Todas as linhas da definição, sem agrupar (para exportar), até <see cref="MaxExportRows"/>.</summary>
    public async Task<(IReadOnlyList<ListingColumn> Columns, IReadOnlyList<ListingRow> Rows, bool Truncated)> ExportAsync(
        Guid companyId, ListingDefinition definition, IReadOnlyDictionary<string, string>? parameters, DateOnly today, CancellationToken cancellationToken = default)
    {
        var problems = ListingCatalog.Validate(definition);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(definition));
        }

        var source = ListingCatalog.Source(definition.Source)!;
        var query = Filtered(companyId, definition, new ListingRequest(parameters), today);
        // Com agrupamento, os campos de grupo vêm primeiro na ordenação, para cada grupo ficar seguido.
        var sort = (definition.GroupBy ?? []).Select(key => new ListingSort(key)).Concat(definition.Sort ?? []).ToList();
        var rows = await Sorted(query, sort).Take(MaxExportRows + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var truncated = rows.Count > MaxExportRows;
        var columns = definition.Columns
            .Select(key => source.Field(key)!)
            .Select(field => new ListingColumn(field.Key, field.Label, field.Type.ToString().ToLowerInvariant(), field.Measure))
            .ToList();
        return (columns, Project(rows.Take(MaxExportRows).ToList(), definition.Columns), truncated);
    }

    private IQueryable<LineRow> Filtered(Guid companyId, ListingDefinition definition, ListingRequest request, DateOnly today)
    {
        var query = Base(companyId);
        var parameters = ListingParameters.Effective(definition, request.Parameters);
        if (definition.Filter is not null)
        {
            var predicate = Group(definition.Filter, definition, parameters, today);
            if (predicate is not null)
            {
                query = query.Where(predicate);
            }
        }

        // Os lançamentos anulados ficam de fora, salvo se a listagem filtrar pelo estado.
        if (!MentionsStatus(definition.Filter))
        {
            query = query.Where(row => row.Status != JournalEntryStatus.Voided);
        }

        return query;
    }

    private static bool MentionsStatus(ListingFilterGroup? group) =>
        group is not null &&
        ((group.Conditions ?? []).Any(condition => condition.Field == "estado") || (group.Groups ?? []).Any(MentionsStatus));

    private static Expression<Func<LineRow, bool>>? Group(
        ListingFilterGroup group, ListingDefinition definition, IReadOnlyDictionary<string, string> parameters, DateOnly today)
    {
        var parts = new List<Expression<Func<LineRow, bool>>>();
        foreach (var condition in group.Conditions ?? [])
        {
            parts.Add(Condition(condition, definition, parameters, today));
        }

        foreach (var child in group.Groups ?? [])
        {
            if (Group(child, definition, parameters, today) is { } built)
            {
                parts.Add(built);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var parameter = Expression.Parameter(typeof(LineRow), "row");
        Expression? body = null;
        foreach (var part in parts)
        {
            var inner = new ParameterReplacer(part.Parameters[0], parameter).Visit(part.Body)!;
            body = body is null ? inner : group.Op == ListingFilterGroup.Or ? Expression.OrElse(body, inner) : Expression.AndAlso(body, inner);
        }

        return Expression.Lambda<Func<LineRow, bool>>(body!, parameter);
    }

    private static Expression<Func<LineRow, bool>> Condition(
        ListingCondition condition, ListingDefinition definition, IReadOnlyDictionary<string, string> parameters, DateOnly today)
    {
        var field = ListingCatalog.Source(definition.Source)!.Field(condition.Field)!;
        var selector = Values[condition.Field];
        var row = selector.Parameters[0];
        var value = selector.Body;

        string Resolve(string? text)
        {
            var raw = text ?? string.Empty;
            if (!raw.StartsWith('@'))
            {
                return raw;
            }

            return parameters.TryGetValue(raw[1..], out var given)
                ? given
                : throw new ArgumentException($"Falta o valor do parâmetro «{raw[1..]}».");
        }

        Expression Constant(string text) => field.Type switch
        {
            ListingFieldType.Date => Expression.Constant(
                ListingParameters.ResolveDate(text, today) ?? throw new ArgumentException($"«{text}» não é uma data."), typeof(DateOnly)),
            ListingFieldType.Integer => Expression.Constant(
                int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw new ArgumentException($"«{text}» não é um número inteiro.")),
            ListingFieldType.Money => Expression.Constant(
                ListingCatalog.TryDecimal(text, out var amount) ? amount : throw new ArgumentException($"«{text}» não é um número.")),
            _ => Expression.Constant(text),
        };

        Expression body;
        if (field.Type == ListingFieldType.Text)
        {
            Expression Like(string pattern) => Expression.Call(
                ILikeMethod, Expression.Constant(EF.Functions), value, Expression.Constant(pattern), Expression.Constant("\\"));
            var notNull = Expression.NotEqual(value, Expression.Constant(null, typeof(string)));
            var isNull = Expression.Equal(value, Expression.Constant(null, typeof(string)));
            body = condition.Operator switch
            {
                ListingOperators.Contains => Expression.AndAlso(notNull, Like("%" + Escape(Resolve(condition.Value)) + "%")),
                ListingOperators.NotContains => Expression.OrElse(isNull, Expression.Not(Like("%" + Escape(Resolve(condition.Value)) + "%"))),
                ListingOperators.Equal => Expression.AndAlso(notNull, Like(Escape(Resolve(condition.Value)))),
                ListingOperators.NotEqual => Expression.OrElse(isNull, Expression.Not(Like(Escape(Resolve(condition.Value))))),
                ListingOperators.StartsWith => Expression.AndAlso(notNull, Like(Escape(Resolve(condition.Value)) + "%")),
                ListingOperators.Empty => Expression.OrElse(isNull, Expression.Equal(value, Expression.Constant(string.Empty))),
                ListingOperators.NotEmpty => Expression.AndAlso(notNull, Expression.NotEqual(value, Expression.Constant(string.Empty))),
                _ => throw new ArgumentException($"O operador «{condition.Operator}» não serve a texto."),
            };
        }
        else
        {
            var first = Constant(Resolve(condition.Value));
            body = condition.Operator switch
            {
                ListingOperators.Equal => Expression.Equal(value, first),
                ListingOperators.NotEqual => Expression.NotEqual(value, first),
                ListingOperators.Greater => Expression.GreaterThan(value, first),
                ListingOperators.GreaterOrEqual => Expression.GreaterThanOrEqual(value, first),
                ListingOperators.Less => Expression.LessThan(value, first),
                ListingOperators.LessOrEqual => Expression.LessThanOrEqual(value, first),
                ListingOperators.Between => Expression.AndAlso(
                    Expression.GreaterThanOrEqual(value, first), Expression.LessThanOrEqual(value, Constant(Resolve(condition.Value2)))),
                _ => throw new ArgumentException($"O operador «{condition.Operator}» não serve a este campo."),
            };
        }

        return Expression.Lambda<Func<LineRow, bool>>(body, row);
    }

    /// <summary>Escapa o que tem sentido num padrão de <c>ILIKE</c> (<c>\</c>, <c>%</c> e <c>_</c>).</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static Expression<Func<LineRow, bool>> KeyEquals(Expression<Func<LineRow, string?>> key, string? value)
    {
        var body = Expression.Equal(key.Body, Expression.Constant(value, typeof(string)));
        return Expression.Lambda<Func<LineRow, bool>>(body, key.Parameters[0]);
    }

    private static IQueryable<LineRow> Sorted(IQueryable<LineRow> query, IReadOnlyList<ListingSort>? sort)
    {
        var first = true;
        foreach (var item in sort ?? [])
        {
            query = Order(query, Values[item.Field], item.Descending, first);
            first = false;
        }

        if (first)
        {
            query = Order(query, Select(row => row.Date), descending: false, first: true);
            query = Order(query, Select(row => row.Reference), descending: false, first: false);
            query = Order(query, Select(row => row.Sequence), descending: false, first: false);
        }

        // O desempate por identificador mantém a paginação estável.
        return Order(query, Select(row => row.Id), descending: false, first: false);
    }

    private static IQueryable<LineRow> Order(IQueryable<LineRow> query, LambdaExpression selector, bool descending, bool first)
    {
        var name = first ? (descending ? "OrderByDescending" : "OrderBy") : (descending ? "ThenByDescending" : "ThenBy");
        var method = typeof(Queryable).GetMethods()
            .First(candidate => candidate.Name == name && candidate.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(LineRow), selector.ReturnType);
        return (IQueryable<LineRow>)method.Invoke(null, [query, selector])!;
    }

    private static List<ListingRow> Project(IReadOnlyList<LineRow> rows, IReadOnlyList<string> columns)
    {
        var readers = columns.Select(key =>
        {
            var selector = Values[key];
            var boxed = Expression.Lambda<Func<LineRow, object?>>(Expression.Convert(selector.Body, typeof(object)), selector.Parameters[0]).Compile();
            return (Func<LineRow, object?>)(row => boxed(row) switch
            {
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                var other => other,
            });
        }).ToList();
        return rows.Select(row => new ListingRow([.. readers.Select(read => read(row))], new ListingRowLink(row.Reference, row.PartyId))).ToList();
    }

    private static Dictionary<string, decimal> Sums(decimal debit, decimal credit) => new(StringComparer.Ordinal)
    {
        ["debito"] = debit,
        ["credito"] = credit,
        ["saldo"] = debit - credit,
    };

    /// <summary>Troca o parâmetro de uma expressão por outro, para juntar condições numa só.</summary>
    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
