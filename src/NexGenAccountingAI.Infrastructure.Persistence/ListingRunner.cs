using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using NexGenAccountingAI.Application.Listings;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma soma que as listagens fazem nos subtotais e no total geral.</summary>
internal sealed record ListingSum<TRow>(string Key, Expression<Func<TRow, decimal>> Selector);

/// <summary>
/// O que uma origem de dados diz ao motor (F4D-02): a consulta de partida (sempre limitada à
/// empresa), o que cada campo é em SQL, as chaves de agrupamento, as somas e a ligação ao registo.
/// </summary>
internal sealed class ListingBinding<TRow>
{
    public required Func<Guid, IQueryable<TRow>> Base { get; init; }

    /// <summary>O valor de cada campo: <c>Expression&lt;Func&lt;TRow, T&gt;&gt;</c> com o tipo do campo.</summary>
    public required IReadOnlyDictionary<string, LambdaExpression> Values { get; init; }

    /// <summary>A chave de agrupamento de cada campo agrupável, sempre em texto.</summary>
    public required IReadOnlyDictionary<string, Expression<Func<TRow, string?>>> GroupKeys { get; init; }

    /// <summary>As somas calculadas na base de dados (no máximo <see cref="ListingRunner{TRow}.MaxSums"/>).</summary>
    public required IReadOnlyList<ListingSum<TRow>> Sums { get; init; }

    /// <summary>Medidas derivadas das somas (por exemplo, o saldo é o débito menos o crédito).</summary>
    public Action<Dictionary<string, decimal>>? Derive { get; init; }

    public required IReadOnlyList<(LambdaExpression Selector, bool Descending)> DefaultSort { get; init; }

    /// <summary>O desempate final da ordenação, que mantém a paginação estável.</summary>
    public required LambdaExpression TieBreak { get; init; }

    public required Func<TRow, ListingRowLink> Link { get; init; }

    /// <summary>Linhas escondidas, salvo se a definição filtrar pelo campo indicado (por exemplo, os anulados).</summary>
    public (string Field, Expression<Func<TRow, bool>> Visible)? HiddenUnlessFiltered { get; init; }
}

/// <summary>O que o motor sabe fazer com uma origem, sem conhecer o tipo da sua linha.</summary>
internal interface IListingRunner
{
    Task<ListingResult> RunAsync(Guid companyId, ListingDefinition definition, ListingRequest request, DateOnly today, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ListingColumn> Columns, IReadOnlyList<ListingRow> Rows, bool Truncated)> ExportAsync(
        Guid companyId, ListingDefinition definition, IReadOnlyDictionary<string, string>? parameters, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// O núcleo do motor de listagens (F4D-01/02): traduz a definição (só dados) em expressões LINQ
/// tipadas sobre a linha da origem, que o EF converte em SQL com parâmetros. Nunca se junta texto
/// do utilizador a uma consulta. Com agrupamento, cada pedido traz um nível: os grupos (com contagem
/// e somas) ou, no último, as linhas paginadas.
/// </summary>
internal sealed class ListingRunner<TRow>(ListingBinding<TRow> binding) : IListingRunner
{
    public const int MaxPageSize = 1000;

    /// <summary>O máximo de linhas numa exportação.</summary>
    public const int MaxExportRows = 100_000;

    /// <summary>O máximo de somas distintas que uma origem calcula na base de dados.</summary>
    public const int MaxSums = 4;

    private static readonly MethodInfo ILikeMethod = typeof(NpgsqlDbFunctionsExtensions)
        .GetMethod(nameof(NpgsqlDbFunctionsExtensions.ILike), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    private static readonly MethodInfo EnumerableCount = typeof(Enumerable).GetMethods()
        .First(method => method.Name == nameof(Enumerable.Count) && method.GetParameters().Length == 1).MakeGenericMethod(typeof(TRow));

    private static readonly MethodInfo EnumerableSum = typeof(Enumerable).GetMethods()
        .First(method => method.Name == nameof(Enumerable.Sum) && method.IsGenericMethod && method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType == typeof(Func<,>).MakeGenericType(method.GetGenericArguments()[0], typeof(decimal)))
        .MakeGenericMethod(typeof(TRow));

    /// <summary>Uma linha de agregados: a chave, quantas linhas e até quatro somas.</summary>
    private sealed class Aggregate
    {
        public string? Key { get; set; }

        public int Count { get; set; }

        public decimal S1 { get; set; }

        public decimal S2 { get; set; }

        public decimal S3 { get; set; }

        public decimal S4 { get; set; }

        public decimal Sum(int index) => index switch { 0 => S1, 1 => S2, 2 => S3, _ => S4 };
    }

    public async Task<ListingResult> RunAsync(
        Guid companyId, ListingDefinition definition, ListingRequest request, DateOnly today, CancellationToken cancellationToken)
    {
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
            query = query.Where(KeyEquals(binding.GroupKeys[groupBy[level]], path[level]));
        }

        var columns = Columns(source, definition.Columns);

        // O total do que está aberto: uma agregação com chave constante.
        var totalRow = (await Aggregates(query, row => string.Empty).ToListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        var totals = Measures(totalRow);
        var totalRows = totalRow?.Count ?? 0;

        if (path.Count < groupBy.Count)
        {
            var aggregates = await Aggregates(query, binding.GroupKeys[groupBy[path.Count]]).ToListAsync(cancellationToken).ConfigureAwait(false);
            var numeric = source.Field(groupBy[path.Count]) is { Type: ListingFieldType.Integer };
            var groups = aggregates
                .OrderBy(item => item.Key is null ? 1 : 0)
                .ThenBy(item => numeric && int.TryParse(item.Key, out var number) ? number : 0)
                .ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new ListingGroup(item.Key, item.Key ?? "(sem valor)", item.Count, Measures(item)))
                .ToList();
            return new ListingResult("groups", columns, groupBy, groups, [], totalRows, totals, []);
        }

        var rows = await Sorted(query, definition.Sort)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, MaxPageSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ListingResult("rows", columns, groupBy, [], Project(rows, definition.Columns), totalRows, totals, []);
    }

    public async Task<(IReadOnlyList<ListingColumn> Columns, IReadOnlyList<ListingRow> Rows, bool Truncated)> ExportAsync(
        Guid companyId, ListingDefinition definition, IReadOnlyDictionary<string, string>? parameters, DateOnly today, CancellationToken cancellationToken)
    {
        var query = Filtered(companyId, definition, new ListingRequest(parameters), today);
        // Com agrupamento, os campos de grupo vêm primeiro na ordenação, para cada grupo ficar seguido.
        var sort = (definition.GroupBy ?? []).Select(key => new ListingSort(key)).Concat(definition.Sort ?? []).ToList();
        var rows = await Sorted(query, sort).Take(MaxExportRows + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var columns = Columns(ListingCatalog.Source(definition.Source)!, definition.Columns);
        return (columns, Project(rows.Take(MaxExportRows).ToList(), definition.Columns), rows.Count > MaxExportRows);
    }

    private static List<ListingColumn> Columns(ListingSource source, IReadOnlyList<string> keys) =>
        keys.Select(key => source.Field(key)!)
            .Select(field => new ListingColumn(field.Key, field.Label, field.Type.ToString().ToLowerInvariant(), field.Measure))
            .ToList();

    /// <summary>Agrupa pela chave e soma: a contagem e as somas da origem, numa só consulta.</summary>
    private IQueryable<Aggregate> Aggregates(IQueryable<TRow> query, Expression<Func<TRow, string?>> key)
    {
        var group = Expression.Parameter(typeof(IGrouping<string?, TRow>), "group");
        var members = new List<MemberBinding>
        {
            Expression.Bind(typeof(Aggregate).GetProperty(nameof(Aggregate.Key))!, Expression.Property(group, nameof(IGrouping<string?, TRow>.Key))),
            Expression.Bind(typeof(Aggregate).GetProperty(nameof(Aggregate.Count))!, Expression.Call(EnumerableCount, group)),
        };
        for (var index = 0; index < binding.Sums.Count; index++)
        {
            members.Add(Expression.Bind(typeof(Aggregate).GetProperty($"S{index + 1}")!, Expression.Call(EnumerableSum, group, binding.Sums[index].Selector)));
        }

        var select = Expression.Lambda<Func<IGrouping<string?, TRow>, Aggregate>>(Expression.MemberInit(Expression.New(typeof(Aggregate)), members), group);
        return query.GroupBy(key).Select(select);
    }

    private Dictionary<string, decimal> Measures(Aggregate? aggregate)
    {
        var sums = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var index = 0; index < binding.Sums.Count; index++)
        {
            sums[binding.Sums[index].Key] = aggregate?.Sum(index) ?? 0m;
        }

        binding.Derive?.Invoke(sums);
        return sums;
    }

    private IQueryable<TRow> Filtered(Guid companyId, ListingDefinition definition, ListingRequest request, DateOnly today)
    {
        var query = binding.Base(companyId);
        var parameters = ListingParameters.Effective(definition, request.Parameters);
        if (definition.Filter is not null && Group(definition.Filter, definition, parameters, today) is { } predicate)
        {
            query = query.Where(predicate);
        }

        if (binding.HiddenUnlessFiltered is { } hidden && !Mentions(definition.Filter, hidden.Field))
        {
            query = query.Where(hidden.Visible);
        }

        return query;
    }

    private static bool Mentions(ListingFilterGroup? group, string field) =>
        group is not null && ((group.Conditions ?? []).Any(condition => condition.Field == field) || (group.Groups ?? []).Any(child => Mentions(child, field)));

    private Expression<Func<TRow, bool>>? Group(
        ListingFilterGroup group, ListingDefinition definition, IReadOnlyDictionary<string, string> parameters, DateOnly today)
    {
        var parts = (group.Conditions ?? []).Select(condition => Condition(condition, definition, parameters, today)).ToList();
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

        var parameter = Expression.Parameter(typeof(TRow), "row");
        Expression? body = null;
        foreach (var part in parts)
        {
            var inner = new ParameterReplacer(part.Parameters[0], parameter).Visit(part.Body)!;
            body = body is null ? inner : group.Op == ListingFilterGroup.Or ? Expression.OrElse(body, inner) : Expression.AndAlso(body, inner);
        }

        return Expression.Lambda<Func<TRow, bool>>(body!, parameter);
    }

    private Expression<Func<TRow, bool>> Condition(
        ListingCondition condition, ListingDefinition definition, IReadOnlyDictionary<string, string> parameters, DateOnly today)
    {
        var field = ListingCatalog.Source(definition.Source)!.Field(condition.Field)!;
        var selector = binding.Values[condition.Field];
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

        // A constante tem o tipo do campo; se o campo admite nulos, a comparação é entre tipos com nulos.
        Expression Constant(string text)
        {
            object parsed = field.Type switch
            {
                ListingFieldType.Date => ListingParameters.ResolveDate(text, today) ?? throw new ArgumentException($"«{text}» não é uma data."),
                ListingFieldType.Integer => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw new ArgumentException($"«{text}» não é um número inteiro."),
                ListingFieldType.Money => ListingCatalog.TryDecimal(text, out var amount) ? amount : throw new ArgumentException($"«{text}» não é um número."),
                _ => text,
            };
            var constant = Expression.Constant(parsed);
            return value.Type == constant.Type ? constant : Expression.Convert(constant, value.Type);
        }

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

        return Expression.Lambda<Func<TRow, bool>>(body, row);
    }

    /// <summary>Escapa o que tem sentido num padrão de <c>ILIKE</c> (<c>\</c>, <c>%</c> e <c>_</c>).</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static Expression<Func<TRow, bool>> KeyEquals(Expression<Func<TRow, string?>> key, string? value) =>
        Expression.Lambda<Func<TRow, bool>>(Expression.Equal(key.Body, Expression.Constant(value, typeof(string))), key.Parameters[0]);

    private IQueryable<TRow> Sorted(IQueryable<TRow> query, IReadOnlyList<ListingSort>? sort)
    {
        var first = true;
        foreach (var item in sort ?? [])
        {
            query = Order(query, binding.Values[item.Field], item.Descending, first);
            first = false;
        }

        if (first)
        {
            foreach (var (selector, descending) in binding.DefaultSort)
            {
                query = Order(query, selector, descending, first);
                first = false;
            }
        }

        return first ? Order(query, binding.TieBreak, false, true) : Order(query, binding.TieBreak, false, false);
    }

    private static IQueryable<TRow> Order(IQueryable<TRow> query, LambdaExpression selector, bool descending, bool first)
    {
        var name = first ? (descending ? "OrderByDescending" : "OrderBy") : (descending ? "ThenByDescending" : "ThenBy");
        var method = typeof(Queryable).GetMethods()
            .First(candidate => candidate.Name == name && candidate.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TRow), selector.ReturnType);
        return (IQueryable<TRow>)method.Invoke(null, [query, selector])!;
    }

    private List<ListingRow> Project(IReadOnlyList<TRow> rows, IReadOnlyList<string> columns)
    {
        var readers = columns.Select(key =>
        {
            var selector = binding.Values[key];
            var boxed = Expression.Lambda<Func<TRow, object?>>(Expression.Convert(selector.Body, typeof(object)), selector.Parameters[0]).Compile();
            return (Func<TRow, object?>)(row => boxed(row) switch
            {
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                var other => other,
            });
        }).ToList();
        return rows.Select(row => new ListingRow([.. readers.Select(read => read(row))], binding.Link(row))).ToList();
    }

    /// <summary>Troca o parâmetro de uma expressão por outro, para juntar condições numa só.</summary>
    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
