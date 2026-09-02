using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record DocumentNumberSeriesState(
    string Series,
    int Year,
    int HighestIssuedNumber,
    int NextNumber,
    bool HasCounter,
    string Origin,
    string Name,
    string? OriginRoute);

/// <summary>
/// Consulta e avança, de forma controlada, os contadores de numeração de uma
/// empresa. Não permite recuar nem ficar abaixo de uma referência já emitida.
/// </summary>
public sealed class DocumentNumberSeriesService(AccountingDbContext context)
{
    public async Task<IReadOnlyList<DocumentNumberSeriesState>> ListAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken = default)
    {
        Validate(companyId, year);

        var documentTypes = await context.AccountingDocumentTypes
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Code, item.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var journals = await context.Journals
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Code, item.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var counters = await context.DocumentNumberSeries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Year == year)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var references = await context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Reference.Contains($"-{year}-"))
            .Select(item => item.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var highestBySeries = references
            .Select(reference => ParseReference(reference))
            .Where(item => item is not null && item.Value.Year == year)
            .Select(item => item!.Value)
            .GroupBy(item => item.Series, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Number), StringComparer.Ordinal);

        var definitions = new Dictionary<string, SeriesDefinition>(StringComparer.Ordinal);
        foreach (var item in documentTypes)
        {
            definitions[item.Code] = new SeriesDefinition("Tipo de documento", item.Name, "/documentos");
        }

        foreach (var item in journals)
        {
            if (definitions.TryGetValue(item.Code, out var existing))
            {
                definitions[item.Code] = new SeriesDefinition(
                    "Tipo de documento e diário", $"{existing.Name} / {item.Name}", "/documentos");
            }
            else
            {
                definitions[item.Code] = new SeriesDefinition("Diário", item.Name, "/diarios");
            }
        }

        foreach (var counter in counters)
        {
            definitions.TryAdd(counter.Series, new SeriesDefinition("Histórico", "Série com contador persistido", null));
        }

        foreach (var series in highestBySeries.Keys)
        {
            definitions.TryAdd(series, new SeriesDefinition("Histórico", "Série detetada nos movimentos", null));
        }

        var counterBySeries = counters.ToDictionary(item => item.Series, StringComparer.Ordinal);
        return definitions
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item =>
            {
                counterBySeries.TryGetValue(item.Key, out var counter);
                var highest = highestBySeries.GetValueOrDefault(item.Key);
                return new DocumentNumberSeriesState(
                    item.Key,
                    year,
                    highest,
                    counter?.NextNumber ?? highest + 1,
                    counter is not null,
                    item.Value.Origin,
                    item.Value.Name,
                    item.Value.Route);
            })
            .ToList();
    }

    public async Task<DocumentNumberSeriesState> AdvanceAsync(
        Guid companyId,
        string series,
        int year,
        int nextNumber,
        CancellationToken cancellationToken = default)
    {
        Validate(companyId, year);
        var normalizedSeries = NormalizeSeries(series);
        if (nextNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nextNumber), "O próximo número tem de ser positivo.");
        }

        var configured = await context.AccountingDocumentTypes
            .AsNoTracking()
            .AnyAsync(item => item.CompanyId == companyId && item.Code == normalizedSeries, cancellationToken)
            .ConfigureAwait(false) || await context.Journals
            .AsNoTracking()
            .AnyAsync(item => item.CompanyId == companyId && item.Code == normalizedSeries, cancellationToken)
            .ConfigureAwait(false);
        if (!configured)
        {
            throw new InvalidOperationException("A série não pertence aos tipos de documento ou diários da empresa.");
        }

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await SqlServerApplicationLock.AcquireAsync(
                context,
                $"AccountingAI:NumberSeries:{companyId:N}:{normalizedSeries}:{year}",
                cancellationToken).ConfigureAwait(false);

            context.ChangeTracker.Clear();
            var counter = await context.DocumentNumberSeries
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.Series == normalizedSeries && item.Year == year,
                    cancellationToken)
                .ConfigureAwait(false);
            var highest = await HighestExistingNumberAsync(companyId, normalizedSeries, year, cancellationToken).ConfigureAwait(false);
            var minimum = Math.Max(highest + 1, counter?.NextNumber ?? 1);
            if (nextNumber < minimum)
            {
                throw new InvalidOperationException($"O próximo número não pode ser inferior a {minimum}.");
            }

            if (counter is null)
            {
                counter = new DocumentNumberSeries(Guid.NewGuid(), companyId, normalizedSeries, year, nextNumber);
                context.DocumentNumberSeries.Add(counter);
            }
            else
            {
                counter.AdvanceTo(nextNumber);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (await ListAsync(companyId, year, cancellationToken).ConfigureAwait(false))
            .Single(item => item.Series == normalizedSeries);
    }

    private async Task<int> HighestExistingNumberAsync(
        Guid companyId,
        string series,
        int year,
        CancellationToken cancellationToken)
    {
        var referenceBase = $"{series}-{year}-";
        var references = await context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Reference.StartsWith(referenceBase))
            .Select(item => item.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return references
            .Select(reference => int.TryParse(reference[referenceBase.Length..], out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max();
    }

    private static (string Series, int Year, int Number)? ParseReference(string reference)
    {
        var numberSeparator = reference.LastIndexOf('-');
        if (numberSeparator <= 0 || !int.TryParse(reference[(numberSeparator + 1)..], out var number)) return null;
        var yearSeparator = reference.LastIndexOf('-', numberSeparator - 1);
        if (yearSeparator <= 0 || !int.TryParse(reference[(yearSeparator + 1)..numberSeparator], out var year)) return null;
        return (reference[..yearSeparator], year, number);
    }

    private static void Validate(Guid companyId, int year)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        if (year is < 2000 or > 2200) throw new ArgumentOutOfRangeException(nameof(year), "O exercício tem de estar entre 2000 e 2200.");
    }

    private static string NormalizeSeries(string series)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(series);
        var normalized = series.Trim().ToUpperInvariant();
        if (normalized.Length > 24) throw new ArgumentException("A série não pode exceder 24 caracteres.", nameof(series));
        return normalized;
    }

    private sealed record SeriesDefinition(string Origin, string Name, string? Route);
}
