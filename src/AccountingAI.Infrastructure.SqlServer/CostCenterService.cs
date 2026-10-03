using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record CostCenterRow(string Code, string Name, bool IsActive);

/// <summary>O movimento de uma conta num centro de custo, num período.</summary>
public sealed record AnalyticAccountRow(string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <summary>
/// Um centro de custo no mapa analítico: os gastos (classe 6, débito menos
/// crédito), os rendimentos (classe 7, crédito menos débito), o resultado, e
/// as contas que o compõem. <see cref="Code"/> é <c>null</c> na linha «sem
/// centro de custo».
/// </summary>
public sealed record AnalyticCenterRow(
    string? Code,
    string Name,
    decimal Expenses,
    decimal Income,
    decimal Result,
    IReadOnlyList<AnalyticAccountRow> Accounts);

public sealed record AnalyticReport(
    DateOnly? From,
    DateOnly? To,
    decimal TotalExpenses,
    decimal TotalIncome,
    decimal TotalResult,
    IReadOnlyList<AnalyticCenterRow> Centers);

/// <summary>
/// Mantém os centros de custo (<see cref="CostCenter"/>) de uma empresa e
/// produz o mapa analítico: o movimento do período por centro de custo e por
/// conta, sem os lançamentos anulados. Só as contas de gastos e rendimentos
/// (classes 6 e 7) entram no mapa — incluindo as linhas sem centro, para o
/// total bater com a demonstração de resultados.
/// </summary>
public sealed class CostCenterService(AccountingDbContext context)
{
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IReadOnlyList<CostCenterRow>?> ListAsync(
        Guid userId, Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await context.CostCenters
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && (includeInactive || item.IsActive))
            .OrderBy(item => item.Code)
            .Select(item => new CostCenterRow(item.Code, item.Name, item.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<CostCenterRow?> CreateAsync(
        Guid userId, Guid companyId, string code, string name, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var costCenter = CostCenter.Create(companyId, code, name);
        var duplicate = await context.CostCenters
            .AnyAsync(item => item.CompanyId == companyId && item.Code == costCenter.Code, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new InvalidOperationException("Já existe um centro de custo com esse código na empresa.");
        }

        context.CostCenters.Add(costCenter);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CostCenterRow(costCenter.Code, costCenter.Name, costCenter.IsActive);
    }

    /// <returns><c>null</c> sem acesso à empresa ou centro de custo desconhecido.</returns>
    public async Task<CostCenterRow?> UpdateAsync(
        Guid userId, Guid companyId, string code, string name, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        var costCenter = await context.CostCenters
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (costCenter is null)
        {
            return null;
        }

        costCenter.Rename(name);
        costCenter.SetActive(isActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CostCenterRow(costCenter.Code, costCenter.Name, costCenter.IsActive);
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<AnalyticReport?> ReportAsync(
        Guid userId, Guid companyId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var lines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                (line.Account.Code.StartsWith("6") || line.Account.Code.StartsWith("7")));
        if (from is { } start)
        {
            lines = lines.Where(line => line.JournalEntry.EntryDate >= start);
        }

        if (to is { } end)
        {
            lines = lines.Where(line => line.JournalEntry.EntryDate <= end);
        }

        var grouped = await lines
            .GroupBy(line => new { line.CostCenterId, line.Account.Code, line.Account.Name })
            .Select(group => new
            {
                group.Key.CostCenterId,
                AccountCode = group.Key.Code,
                AccountName = group.Key.Name,
                Debit = group.Sum(line => line.Debit),
                Credit = group.Sum(line => line.Credit),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var centers = await context.CostCenters
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToDictionaryAsync(item => item.Id, cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .GroupBy(item => item.CostCenterId)
            .Select(group =>
            {
                var center = group.Key is { } id ? centers.GetValueOrDefault(id) : null;
                var accounts = group
                    .OrderBy(item => item.AccountCode, StringComparer.Ordinal)
                    .Select(item => new AnalyticAccountRow(item.AccountCode, item.AccountName, item.Debit, item.Credit))
                    .ToList();
                var expenses = accounts.Where(item => item.AccountCode.StartsWith('6')).Sum(item => item.Debit - item.Credit);
                var income = accounts.Where(item => item.AccountCode.StartsWith('7')).Sum(item => item.Credit - item.Debit);
                return new AnalyticCenterRow(
                    center?.Code, center?.Name ?? "Sem centro de custo", expenses, income, income - expenses, accounts);
            })
            // Os centros por código; a linha «sem centro» no fim.
            .OrderBy(row => row.Code is null)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ToList();

        return new AnalyticReport(
            from, to,
            rows.Sum(row => row.Expenses),
            rows.Sum(row => row.Income),
            rows.Sum(row => row.Result),
            rows);
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
