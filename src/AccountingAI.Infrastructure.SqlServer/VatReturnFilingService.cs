using System.Text.Json;
using AccountingAI.Application.Vat;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>A vista de um apuramento de IVA de um período: o cálculo mais o estado de gravação.</summary>
public sealed record VatReturnFilingView(
    bool Saved,
    DateTimeOffset? SavedAtUtc,
    decimal PriorPeriodCredit,
    decimal SuggestedPriorCredit,
    bool PriorFromPreviousFiling,
    VatReturn Return);

/// <summary>Uma linha do histórico de apuramentos.</summary>
public sealed record VatReturnFilingSummary(
    Guid Id,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal NetPayable,
    decimal CreditToCarryForward,
    DateTimeOffset? SavedAtUtc);

/// <summary>
/// Guarda e lê os apuramentos periódicos de IVA de uma empresa. O cálculo vem do
/// <see cref="IVatReturnService"/>; aqui trata-se do excesso a reportar como
/// input explícito, do encadeamento entre períodos, e da persistência.
/// </summary>
public sealed class VatReturnFilingService(AccountingDbContext context, IVatReturnService calculator)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<VatReturnFilingView?> GetAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var existing = await context.VatReturnFilings
            .AsNoTracking()
            .Where(filing => filing.CompanyId == companyId && filing.PeriodFrom == from && filing.PeriodTo == to)
            .Select(filing => new
            {
                filing.PriorPeriodCredit,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(filing, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(filing, "ModifiedAtUtc"),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var previousCarryForward = await PreviousCarryForwardAsync(companyId, from, cancellationToken).ConfigureAwait(false);

        decimal? priorOverride = existing?.PriorPeriodCredit ?? previousCarryForward;
        var computed = await calculator
            .BuildAsync(userId, companyId, from, to, priorOverride, cancellationToken)
            .ConfigureAwait(false);
        if (computed is null)
        {
            return null;
        }

        var suggested = previousCarryForward ?? computed.PriorPeriodCredit;
        return new VatReturnFilingView(
            existing is not null,
            existing?.ModifiedAtUtc ?? existing?.CreatedAtUtc,
            computed.PriorPeriodCredit,
            suggested,
            previousCarryForward is not null,
            computed);
    }

    public async Task<VatReturnFilingView?> SaveAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, decimal priorPeriodCredit,
        CancellationToken cancellationToken = default)
    {
        var computed = await calculator
            .BuildAsync(userId, companyId, from, to, priorPeriodCredit, cancellationToken)
            .ConfigureAwait(false);
        if (computed is null)
        {
            return null;
        }

        var snapshot = JsonSerializer.Serialize(computed, Json);
        var row = await context.VatReturnFilings
            .SingleOrDefaultAsync(
                filing => filing.CompanyId == companyId && filing.PeriodFrom == from && filing.PeriodTo == to,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            context.VatReturnFilings.Add(VatReturnFiling.Create(companyId, from, to, priorPeriodCredit, snapshot));
        }
        else
        {
            row.Update(priorPeriodCredit, snapshot);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VatReturnFilingSummary>?> ListAsync(
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

        var rows = await context.VatReturnFilings
            .AsNoTracking()
            .Where(filing => filing.CompanyId == companyId)
            .Select(filing => new
            {
                filing.Id,
                filing.PeriodFrom,
                filing.PeriodTo,
                filing.SnapshotJson,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(filing, "CreatedAtUtc"),
                ModifiedAtUtc = EF.Property<DateTimeOffset?>(filing, "ModifiedAtUtc"),
            })
            .OrderByDescending(filing => filing.PeriodFrom)
            .Take(24)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row =>
            {
                var snapshot = Deserialize(row.SnapshotJson);
                return new VatReturnFilingSummary(
                    row.Id, row.PeriodFrom, row.PeriodTo,
                    snapshot?.NetPayable ?? 0m,
                    snapshot?.CreditToCarryForward ?? 0m,
                    row.ModifiedAtUtc ?? row.CreatedAtUtc);
            })
            .ToList();
    }

    private async Task<decimal?> PreviousCarryForwardAsync(Guid companyId, DateOnly from, CancellationToken cancellationToken)
    {
        var previousEnd = from.AddDays(-1);
        var snapshot = await context.VatReturnFilings
            .AsNoTracking()
            .Where(filing => filing.CompanyId == companyId && filing.PeriodTo == previousEnd)
            .Select(filing => filing.SnapshotJson)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return snapshot is null ? null : Deserialize(snapshot)?.CreditToCarryForward ?? 0m;
    }

    private static VatReturn? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<VatReturn>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
