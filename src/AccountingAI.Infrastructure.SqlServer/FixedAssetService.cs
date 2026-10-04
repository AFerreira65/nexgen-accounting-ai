using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record FixedAssetInput(
    string? Code,
    string? Description,
    DateOnly AcquisitionDate,
    DateOnly StartOfUseDate,
    decimal AcquisitionValue,
    decimal ResidualValue,
    decimal AnnualRate,
    bool ByMonths,
    string? AssetAccountCode,
    string? AccumulatedAccountCode,
    string? ExpenseAccountCode,
    DateOnly? DisposalDate,
    bool IsActive = true);

public sealed record FixedAssetRow(
    string Code,
    string Description,
    DateOnly AcquisitionDate,
    DateOnly StartOfUseDate,
    decimal AcquisitionValue,
    decimal ResidualValue,
    decimal AnnualRate,
    bool ByMonths,
    string? AssetAccountCode,
    string? AccumulatedAccountCode,
    string? ExpenseAccountCode,
    DateOnly? DisposalDate,
    bool IsActive);

/// <summary>Um ativo no mapa de depreciações de um exercício.</summary>
public sealed record DepreciationScheduleRow(
    string Code,
    string Description,
    DateOnly StartOfUseDate,
    DateOnly? DisposalDate,
    decimal AcquisitionValue,
    decimal AnnualRate,
    decimal AccumulatedAtStart,
    decimal Depreciation,
    decimal AccumulatedAtEnd,
    decimal NetValue,
    string? ExpenseAccountCode);

/// <param name="LedgerDepreciation">Os gastos de depreciação lançados no exercício (contas 64, sem o apuramento).</param>
/// <param name="Difference">Calculado menos lançado: zero quando a contabilidade tem as depreciações do mapa.</param>
public sealed record DepreciationSchedule(
    int Year,
    IReadOnlyList<DepreciationScheduleRow> Rows,
    decimal TotalAcquisitionValue,
    decimal TotalDepreciation,
    decimal TotalAccumulatedAtEnd,
    decimal TotalNetValue,
    decimal LedgerDepreciation,
    decimal Difference);

/// <summary>
/// Mantém o cadastro de ativos fixos (<see cref="FixedAsset"/>) e calcula o
/// mapa de depreciações de um exercício, por quotas constantes. O mapa é um
/// cálculo — não lança nada — e compara o total com os gastos de depreciação
/// que estão na contabilidade.
/// </summary>
public sealed class FixedAssetService(AccountingDbContext context)
{
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IReadOnlyList<FixedAssetRow>?> ListAsync(
        Guid userId, Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await context.FixedAssets
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && (includeInactive || item.IsActive))
            .OrderBy(item => item.Code)
            .Select(item => new FixedAssetRow(
                item.Code, item.Description, item.AcquisitionDate, item.StartOfUseDate, item.AcquisitionValue, item.ResidualValue,
                item.AnnualRate, item.ByMonths, item.AssetAccount!.Code, item.AccumulatedAccount!.Code, item.ExpenseAccount!.Code,
                item.DisposalDate, item.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<FixedAssetRow?> CreateAsync(
        Guid userId, Guid companyId, FixedAssetInput input, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var asset = FixedAsset.Create(companyId, input.Code ?? string.Empty);
        var duplicate = await context.FixedAssets
            .AnyAsync(item => item.CompanyId == companyId && item.Code == asset.Code, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new InvalidOperationException("Já existe um ativo com esse código na empresa.");
        }

        var codes = await ApplyAsync(asset, companyId, input, cancellationToken).ConfigureAwait(false);
        context.FixedAssets.Add(asset);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RowOf(asset, codes);
    }

    /// <returns><c>null</c> sem acesso à empresa ou ativo desconhecido.</returns>
    public async Task<FixedAssetRow?> UpdateAsync(
        Guid userId, Guid companyId, string code, FixedAssetInput input, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        var asset = await context.FixedAssets
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (asset is null)
        {
            return null;
        }

        var codes = await ApplyAsync(asset, companyId, input, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RowOf(asset, codes);
    }

    /// <summary>O mapa de depreciações do exercício: os ativos ativos que já tinham entrado em funcionamento.</summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<DepreciationSchedule?> ScheduleAsync(
        Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var assets = await context.FixedAssets
            .AsNoTracking()
            .Include(item => item.ExpenseAccount)
            .Where(item => item.CompanyId == companyId && item.IsActive && item.StartOfUseDate.Year <= year)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = assets
            // Um ativo alienado em exercícios anteriores já não está no mapa.
            .Where(asset => asset.DisposalDate is not { } disposal || disposal.Year >= year)
            .Select(asset =>
            {
                var atStart = asset.AccumulatedAtEndOf(year - 1);
                var atEnd = asset.AccumulatedAtEndOf(year);
                return new DepreciationScheduleRow(
                    asset.Code, asset.Description, asset.StartOfUseDate, asset.DisposalDate, asset.AcquisitionValue, asset.AnnualRate,
                    atStart, atEnd - atStart, atEnd, asset.AcquisitionValue - atEnd, asset.ExpenseAccount?.Code);
            })
            .ToList();

        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var ledger = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.Account.Code.StartsWith("64") &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.JournalEntry.Source != EntrySource.Closing &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")))
            .SumAsync(line => line.Debit - line.Credit, cancellationToken)
            .ConfigureAwait(false);

        var total = rows.Sum(row => row.Depreciation);
        return new DepreciationSchedule(
            year, rows, rows.Sum(row => row.AcquisitionValue), total, rows.Sum(row => row.AccumulatedAtEnd), rows.Sum(row => row.NetValue),
            ledger, total - ledger);
    }

    private async Task<(string? Asset, string? Accumulated, string? Expense)> ApplyAsync(
        FixedAsset asset, Guid companyId, FixedAssetInput input, CancellationToken cancellationToken)
    {
        asset.Update(
            input.Description ?? string.Empty, input.AcquisitionDate, input.StartOfUseDate, input.AcquisitionValue,
            input.ResidualValue, input.AnnualRate, input.ByMonths, input.DisposalDate);
        asset.SetActive(input.IsActive);

        var assetAccount = await AccountAsync(companyId, input.AssetAccountCode, "do ativo", cancellationToken).ConfigureAwait(false);
        var accumulated = await AccountAsync(companyId, input.AccumulatedAccountCode, "das depreciações acumuladas", cancellationToken).ConfigureAwait(false);
        var expense = await AccountAsync(companyId, input.ExpenseAccountCode, "do gasto de depreciação", cancellationToken).ConfigureAwait(false);
        asset.LinkAccounts(assetAccount?.Id, accumulated?.Id, expense?.Id);
        return (assetAccount?.Code, accumulated?.Code, expense?.Code);
    }

    private async Task<(Guid Id, string Code)?> AccountAsync(Guid companyId, string? code, string role, CancellationToken cancellationToken)
    {
        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        var account = await context.Accounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Code == normalized)
            .Select(item => new { item.Id, item.Code, item.AllowsPosting })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            throw new InvalidOperationException($"A conta {role} ({normalized}) não existe no plano de contas.");
        }

        if (!account.AllowsPosting)
        {
            throw new InvalidOperationException($"A conta {role} ({normalized}) não é de movimento.");
        }

        return (account.Id, account.Code);
    }

    private static FixedAssetRow RowOf(FixedAsset asset, (string? Asset, string? Accumulated, string? Expense) codes) => new(
        asset.Code, asset.Description, asset.AcquisitionDate, asset.StartOfUseDate, asset.AcquisitionValue, asset.ResidualValue,
        asset.AnnualRate, asset.ByMonths, codes.Asset, codes.Accumulated, codes.Expense, asset.DisposalDate, asset.IsActive);

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
