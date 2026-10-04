using System.Text.Json;
using AccountingAI.Application.Irc;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>A vista de uma estimativa de IRC guardada de um período: o estado de gravação e o cálculo atual.</summary>
public sealed record IrcEstimateFilingView(
    bool Saved,
    DateTimeOffset? SavedAtUtc,
    decimal? TaxRate,
    IReadOnlyList<IrcAdjustment> Adjustments,
    IrcEstimate? Estimate);

/// <summary>Uma linha do histórico de estimativas.</summary>
public sealed record IrcEstimateFilingSummary(
    Guid Id,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal TaxRate,
    decimal EstimatedTax,
    DateTimeOffset? SavedAtUtc);

/// <summary>
/// Guarda e lê as estimativas de IRC de uma empresa. O cálculo vem do
/// <see cref="IrcEstimateService"/>; a leitura recalcula sempre contra o razão
/// atual, usando a taxa e as correções guardadas — para refletir lançamentos
/// posteriores à gravação sem perder o que o utilizador indicou.
/// </summary>
public sealed class IrcEstimateFilingService(AccountingDbContext context, IrcEstimateService calculator)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IrcEstimateFilingView?> GetAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var existing = await context.IrcEstimateFilings
            .AsNoTracking()
            .Where(filing => filing.CompanyId == companyId && filing.PeriodFrom == from && filing.PeriodTo == to)
            .Select(filing => new
            {
                filing.TaxRate,
                filing.AdjustmentsJson,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(filing, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(filing, "ModifiedAtUtc"),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var hasAccess = await context.Companies
                .AsNoTracking()
                .AnyAsync(
                    company => company.Id == companyId &&
                        company.IsActive &&
                        company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                    cancellationToken)
                .ConfigureAwait(false);
            return hasAccess ? new IrcEstimateFilingView(false, null, null, [], null) : null;
        }

        var adjustments = DeserializeAdjustments(existing.AdjustmentsJson);
        var estimate = await calculator
            .BuildAsync(userId, companyId, from, to, existing.TaxRate, adjustments, cancellationToken)
            .ConfigureAwait(false);
        if (estimate is null)
        {
            return null;
        }

        return new IrcEstimateFilingView(
            true, existing.ModifiedAtUtc ?? existing.CreatedAtUtc, existing.TaxRate, adjustments, estimate);
    }

    public async Task<IrcEstimateFilingView?> SaveAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to,
        decimal taxRate, IReadOnlyList<IrcAdjustment> adjustments, CancellationToken cancellationToken = default)
    {
        var estimate = await calculator
            .BuildAsync(userId, companyId, from, to, taxRate, adjustments, cancellationToken)
            .ConfigureAwait(false);
        if (estimate is null)
        {
            return null;
        }

        var adjustmentsJson = JsonSerializer.Serialize(adjustments, Json);
        var snapshot = JsonSerializer.Serialize(estimate, Json);
        var row = await context.IrcEstimateFilings
            .SingleOrDefaultAsync(
                filing => filing.CompanyId == companyId && filing.PeriodFrom == from && filing.PeriodTo == to,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            context.IrcEstimateFilings.Add(IrcEstimateFiling.Create(companyId, from, to, taxRate, adjustmentsJson, snapshot));
        }
        else
        {
            row.Update(taxRate, adjustmentsJson, snapshot);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IrcEstimateFilingSummary>?> ListAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
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

        var rows = await context.IrcEstimateFilings
            .AsNoTracking()
            .Where(filing => filing.CompanyId == companyId)
            .Select(filing => new
            {
                filing.Id,
                filing.PeriodFrom,
                filing.PeriodTo,
                filing.TaxRate,
                filing.SnapshotJson,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(filing, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(filing, "ModifiedAtUtc"),
            })
            .OrderByDescending(filing => filing.PeriodFrom)
            .Take(24)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new IrcEstimateFilingSummary(
                row.Id, row.PeriodFrom, row.PeriodTo, row.TaxRate,
                DeserializeEstimate(row.SnapshotJson)?.EstimatedTax ?? 0m,
                row.ModifiedAtUtc ?? row.CreatedAtUtc))
            .ToList();
    }

    private static IReadOnlyList<IrcAdjustment> DeserializeAdjustments(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<IrcAdjustment>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IrcEstimate? DeserializeEstimate(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IrcEstimate>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
