using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

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
    string? ExpenseAccountCode,
    string? AccumulatedAccountCode = null);

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

/// <summary>Os dados de uma alienação (ou de um abate, com preço zero).</summary>
public sealed record AssetDisposalInput(DateOnly Date, decimal SalePrice, string? CounterpartAccountCode, string JournalCode, string? DocumentReference);

/// <summary>A conta de uma linha do lançamento da alienação, com o valor a débito ou a crédito.</summary>
public sealed record AssetDisposalLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <summary>
/// A alienação ou o abate de um ativo numa data: o valor de aquisição, as
/// depreciações acumuladas até ao mês anterior, o valor líquido, o preço e o
/// resultado (positivo, um ganho), com as linhas do lançamento proposto.
/// </summary>
public sealed record AssetDisposalPreview(
    string AssetCode,
    string Description,
    DateOnly Date,
    decimal AcquisitionValue,
    decimal Accumulated,
    decimal NetBookValue,
    decimal SalePrice,
    decimal Result,
    string Kind,
    IReadOnlyList<AssetDisposalLine> Lines,
    string? Warning);

public sealed record AssetDisposalResult(AssetDisposalPreview Preview, PostedMovement Movement);

/// <summary>
/// Mantém o cadastro de ativos fixos (<see cref="FixedAsset"/>) e calcula o
/// mapa de depreciações de um exercício, por quotas constantes. O mapa é um
/// cálculo — não lança nada — e compara o total com os gastos de depreciação
/// que estão na contabilidade. O lançamento das depreciações é um passo à
/// parte, pedido pelo utilizador, e fica por conferir.
/// </summary>
public sealed class FixedAssetService(AccountingDbContext context, JournalPostingService posting)
{
    /// <summary>A referência de documento do lançamento gerado: é por ela que se sabe que o exercício já foi lançado.</summary>
    public static string DepreciationDocumentReference(int year) => $"Depreciações {year}";

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
            .Include(item => item.AccumulatedAccount)
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
                    atStart, atEnd - atStart, atEnd, asset.AcquisitionValue - atEnd, asset.ExpenseAccount?.Code, asset.AccumulatedAccount?.Code);
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

    /// <summary>
    /// Gera o lançamento das depreciações do exercício, a 31 de dezembro: o
    /// gasto a débito e as depreciações acumuladas a crédito, somados por
    /// conta. Fica por conferir. Recusa se o exercício já foi lançado por
    /// aqui, se algum ativo não tem as duas contas, ou — sem
    /// <paramref name="allowExisting"/> — se já há gastos de depreciação na
    /// contabilidade (para não duplicar o que veio de outro lado).
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PostedMovement?> PostDepreciationAsync(
        Guid userId, Guid companyId, int year, string journalCode, bool allowExisting, CancellationToken cancellationToken = default)
    {
        var schedule = await ScheduleAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            return null;
        }

        var documentReference = DepreciationDocumentReference(year);
        var existing = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.DocumentReference == documentReference)
            .Select(entry => entry.Reference)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException($"As depreciações de {year} já foram lançadas ({existing}). Anule esse lançamento para as gerar de novo.");
        }

        var rows = schedule.Rows.Where(row => row.Depreciation > 0m).ToList();
        if (rows.Count == 0)
        {
            throw new InvalidOperationException($"Não há depreciações a lançar em {year}.");
        }

        var incomplete = rows.Where(row => row.ExpenseAccountCode is null || row.AccumulatedAccountCode is null).Select(row => row.Code).ToList();
        if (incomplete.Count > 0)
        {
            throw new InvalidOperationException(
                $"Falta a conta do gasto ou a das depreciações acumuladas em: {string.Join(", ", incomplete)}. Complete o cadastro antes de lançar.");
        }

        if (schedule.LedgerDepreciation != 0m && !allowExisting)
        {
            throw new InvalidOperationException(
                $"Já há {schedule.LedgerDepreciation:N2} de gastos de depreciação lançados em {year} (contas 64). Confirme que não vai duplicar antes de gerar o lançamento.");
        }

        var description = $"Depreciações do exercício de {year}";
        var lines = rows
            .GroupBy(row => row.ExpenseAccountCode!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new JournalEntryLineInput(group.Key, description, group.Sum(row => row.Depreciation), 0m))
            .Concat(rows
                .GroupBy(row => row.AccumulatedAccountCode!, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new JournalEntryLineInput(group.Key, description, 0m, group.Sum(row => row.Depreciation))))
            .ToList();

        return await posting
            .PostAsync(companyId, journalCode, new DateOnly(year, 12, 31), documentReference, description, reviewed: false, lines, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Calcula a alienação (ou o abate) de um ativo numa data, sem lançar nada:
    /// as depreciações acumuladas até ao mês anterior pela regra do mapa, o valor
    /// líquido e o ganho ou a perda contra o preço. As linhas: as depreciações
    /// acumuladas a débito, o ativo a crédito pelo valor de aquisição, a
    /// contrapartida do preço a débito, e a diferença em 7871 (ganho), 6871
    /// (perda numa venda) ou 6873 (abate, sem preço) — as contas da Taxonomia S
    /// (Portaria n.º 302/2016) para «Alienações» e «Abates».
    /// </summary>
    public async Task<AssetDisposalPreview?> PreviewDisposalAsync(
        Guid userId, Guid companyId, string assetCode, AssetDisposalInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var code = FixedAsset.NormalizeCode(assetCode);
        var asset = await context.FixedAssets
            .AsNoTracking()
            .Include(item => item.AssetAccount)
            .Include(item => item.AccumulatedAccount)
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == code, cancellationToken)
            .ConfigureAwait(false);
        if (asset is null)
        {
            return null;
        }

        return await BuildDisposalAsync(companyId, asset, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lança a alienação (ou o abate) — por conferir — e regista no ativo a data e
    /// a referência do lançamento. Recusa um ativo que já tem uma alienação
    /// lançada (por anular) e um ativo sem as contas do ativo e das depreciações.
    /// </summary>
    public async Task<AssetDisposalResult?> PostDisposalAsync(
        Guid userId, Guid companyId, string assetCode, AssetDisposalInput input, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewDisposalAsync(userId, companyId, assetCode, input, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return null;
        }

        var code = FixedAsset.NormalizeCode(assetCode);
        var asset = await context.FixedAssets
            .AsNoTracking()
            .SingleAsync(item => item.CompanyId == companyId && item.Code == code, cancellationToken)
            .ConfigureAwait(false);
        if (asset.DisposalEntryReference is { } previous &&
            await context.JournalEntries.AsNoTracking().AnyAsync(
                entry => entry.CompanyId == companyId && entry.Reference == previous && entry.Status != JournalEntryStatus.Voided, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"A saída do ativo {asset.Code} já foi lançada ({previous}). Anule esse lançamento para a lançar de novo.");
        }

        var description = $"{(preview.Kind == "abate" ? "Abate" : "Alienação")} do ativo {asset.Code} — {asset.Description}";
        var movement = await posting.PostAsync(
            companyId,
            input.JournalCode,
            input.Date,
            string.IsNullOrWhiteSpace(input.DocumentReference) ? $"Saída do ativo {asset.Code}" : input.DocumentReference.Trim(),
            description.Length <= 500 ? description : description[..500],
            reviewed: false,
            preview.Lines.Select(line => new JournalEntryLineInput(line.AccountCode, description.Length <= 300 ? description : description[..300], line.Debit, line.Credit)).ToList(),
            cancellationToken,
            notes: string.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-PT"),
                $"Valor de aquisição {preview.AcquisitionValue:N2}; depreciações acumuladas {preview.Accumulated:N2}; valor líquido {preview.NetBookValue:N2}; preço {preview.SalePrice:N2}."))
            .ConfigureAwait(false);

        context.ChangeTracker.Clear();
        var tracked = await context.FixedAssets
            .SingleAsync(item => item.CompanyId == companyId && item.Code == code, cancellationToken)
            .ConfigureAwait(false);
        tracked.RecordDisposal(input.Date, movement.Reference);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AssetDisposalResult(preview, movement);
    }

    private async Task<AssetDisposalPreview> BuildDisposalAsync(Guid companyId, FixedAsset asset, AssetDisposalInput input, CancellationToken cancellationToken)
    {
        if (asset.AssetAccount is null || asset.AccumulatedAccount is null)
        {
            throw new InvalidOperationException($"Falta a conta do ativo ou a das depreciações acumuladas em {asset.Code}. Complete o cadastro antes de lançar a saída.");
        }

        if (input.Date < asset.StartOfUseDate)
        {
            throw new ArgumentException("A alienação não pode ser anterior à entrada em funcionamento.", nameof(input));
        }

        var price = decimal.Round(input.SalePrice, 2, MidpointRounding.AwayFromZero);
        if (price < 0m)
        {
            throw new ArgumentException("O preço de venda não pode ser negativo.", nameof(input));
        }

        var (accumulated, netBookValue) = asset.ValuesAtDisposal(input.Date);
        var result = price - netBookValue;
        var kind = price == 0m ? "abate" : "alienação";

        var lines = new List<AssetDisposalLine>();
        if (accumulated > 0m)
        {
            lines.Add(new AssetDisposalLine(asset.AccumulatedAccount.Code, asset.AccumulatedAccount.Name, accumulated, 0m));
        }

        lines.Add(new AssetDisposalLine(asset.AssetAccount.Code, asset.AssetAccount.Name, 0m, asset.AcquisitionValue));
        if (price > 0m)
        {
            var counterpart = (input.CounterpartAccountCode ?? string.Empty).Trim();
            var account = counterpart.Length == 0
                ? null
                : await context.Accounts.AsNoTracking()
                    .Where(item => item.CompanyId == companyId && item.Code == counterpart && item.AllowsPosting)
                    .Select(item => new { item.Code, item.Name })
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (account is null)
            {
                throw new ArgumentException("Indique a conta de movimento que recebe o preço (o comprador, o banco).", nameof(input));
            }

            lines.Add(new AssetDisposalLine(account.Code, account.Name, price, 0m));
        }

        if (result != 0m)
        {
            var prefix = result > 0m ? "7871" : price == 0m ? "6873" : "6871";
            var account = await context.Accounts.AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.AllowsPosting && item.Code.StartsWith(prefix))
                .OrderBy(item => item.Code)
                .Select(item => new { item.Code, item.Name })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"O plano de contas não tem uma conta de movimento em {prefix}: crie-a para lançar o {(result > 0m ? "ganho" : "gasto")}.");
            lines.Add(result > 0m
                ? new AssetDisposalLine(account.Code, account.Name, 0m, result)
                : new AssetDisposalLine(account.Code, account.Name, -result, 0m));
        }

        string? warning = null;
        var ledgerAccumulated = await context.JournalEntryLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == asset.AccumulatedAccountId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided && line.JournalEntry.EntryDate <= input.Date)
            .SumAsync(line => (decimal?)(line.Credit - line.Debit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;
        if (ledgerAccumulated < accumulated)
        {
            warning = string.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-PT"),
                $"A conta {asset.AccumulatedAccount.Code} tem {ledgerAccumulated:N2} de depreciações acumuladas lançadas até à data, menos do que as {accumulated:N2} calculadas: lance primeiro as depreciações do exercício da alienação.");
        }

        return new AssetDisposalPreview(
            asset.Code, asset.Description, input.Date, asset.AcquisitionValue, accumulated, netBookValue, price, result, kind, lines, warning);
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
