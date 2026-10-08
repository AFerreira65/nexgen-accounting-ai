using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O que uma empresa gastou e ainda pode gastar em IA este mês (USD).</summary>
/// <param name="AllowanceUsd">A franquia mensal em vigor; <c>null</c> = ilimitado.</param>
/// <param name="AvailableUsd">O que resta (franquia do mês + crédito extra); <c>null</c> = ilimitado.</param>
public sealed record AiBudgetStatus(
    Guid CompanyId,
    decimal? AllowanceUsd,
    bool AllowanceIsDefault,
    decimal SpentThisMonthUsd,
    decimal ExtraCreditUsd,
    decimal? AvailableUsd,
    int CallsThisMonth)
{
    public bool Unlimited => AllowanceUsd is null;

    public bool Exhausted => AvailableUsd is { } available && available <= 0m;
}

public sealed record AiBudgetCompanyRow(Guid CompanyId, string CompanyName, string FirmName, AiBudgetStatus Status);

/// <summary>
/// O orçamento e o registo de consumo da IA por empresa (USD). Cada uso (conversa, relatório, explicação…) regista os tokens e o
/// custo; antes de cada pedido verifica-se se a empresa ainda tem saldo — a franquia do mês e, depois dela, o crédito extra que o
/// produtor carregou. O custo de um pedido que passa o saldo é registado na mesma (pode ultrapassá-lo por esse pedido).
/// </summary>
public sealed class AiBudgetService(AccountingDbContext context, TimeProvider time)
{
    public const string ExhaustedMessage =
        "O limite de IA desta empresa para este mês foi atingido. Peça mais crédito ao fornecedor do serviço ou espere pelo próximo mês.";

    private DateTimeOffset MonthStart()
    {
        // O mês é o da hora local, mas a base de dados só aceita instantes em UTC.
        var local = time.GetLocalNow();
        var firstDay = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(firstDay, time.LocalTimeZone.GetUtcOffset(firstDay)).ToUniversalTime();
    }

    public async Task<AiBudgetStatus> StatusAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var defaultAllowance = await context.AiGeneralSettings.AsNoTracking().Select(row => row.DefaultMonthlyAllowanceUsd).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var budget = await context.CompanyAiBudgets.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        var start = MonthStart();
        var usage = await context.AiUsageRecords.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CreatedAtUtc >= start)
            .GroupBy(_ => 1)
            .Select(group => new { Cost = group.Sum(item => item.CostUsd), Calls = group.Count() })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return Build(companyId, budget, defaultAllowance, usage?.Cost ?? 0m, usage?.Calls ?? 0);
    }

    /// <summary>A mensagem a mostrar se a empresa já não tem saldo, ou <c>null</c> se pode usar a IA.</summary>
    public async Task<string?> RefusalAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        (await StatusAsync(companyId, cancellationToken).ConfigureAwait(false)).Exhausted ? ExhaustedMessage : null;

    /// <summary>Regista um uso e tira do crédito extra o que a franquia do mês não cobriu.</summary>
    public async Task<AiUsageRecord> RecordAsync(
        Guid companyId, Guid? userId, AiFeature feature, string model, int inputTokens, int outputTokens, CancellationToken cancellationToken = default)
    {
        var overrides = await context.AiGeneralSettings.AsNoTracking().Select(row => row.PricesJson).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var cost = AiPricing.CostUsd(model, inputTokens, outputTokens, overrides);
        var before = await StatusAsync(companyId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var record = new AiUsageRecord(Guid.NewGuid(), companyId, userId, feature, model, inputTokens, outputTokens, cost, now);
        context.AiUsageRecords.Add(record);

        if (before.AllowanceUsd is { } allowance && cost > 0)
        {
            var monthlyLeft = Math.Max(0m, allowance - before.SpentThisMonthUsd);
            var fromExtra = cost - Math.Min(cost, monthlyLeft);
            if (fromExtra > 0)
            {
                var budget = await context.CompanyAiBudgets.SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
                budget?.SpendExtra(fromExtra, now);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<AiBudgetStatus> SetAllowanceAsync(Guid companyId, decimal? monthlyAllowanceUsd, CancellationToken cancellationToken = default)
    {
        var budget = await EnsureAsync(companyId, cancellationToken).ConfigureAwait(false);
        budget.SetAllowance(monthlyAllowanceUsd, time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await StatusAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiBudgetStatus> AddCreditAsync(Guid companyId, decimal amountUsd, string? note, Guid? createdBy, CancellationToken cancellationToken = default)
    {
        var budget = await EnsureAsync(companyId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        budget.AddCredit(amountUsd, now);
        context.AiCreditTopUps.Add(new AiCreditTopUp(Guid.NewGuid(), companyId, amountUsd, note, createdBy, now));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await StatusAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Todas as empresas com o seu consumo e saldo, para o produtor.</summary>
    public async Task<IReadOnlyList<AiBudgetCompanyRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var defaultAllowance = await context.AiGeneralSettings.AsNoTracking().Select(row => row.DefaultMonthlyAllowanceUsd).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var start = MonthStart();
        var companies = await context.Companies.AsNoTracking().Where(company => company.IsActive)
            .Select(company => new { company.Id, company.LegalName, Firm = context.AccountingFirms.Where(firm => firm.Id == company.AccountingFirmId).Select(firm => firm.Name).FirstOrDefault() })
            .OrderBy(company => company.LegalName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var budgets = await context.CompanyAiBudgets.AsNoTracking().ToDictionaryAsync(item => item.CompanyId, cancellationToken).ConfigureAwait(false);
        var usage = await context.AiUsageRecords.AsNoTracking()
            .Where(item => item.CreatedAtUtc >= start)
            .GroupBy(item => item.CompanyId)
            .Select(group => new { group.Key, Cost = group.Sum(item => item.CostUsd), Calls = group.Count() })
            .ToDictionaryAsync(item => item.Key, cancellationToken)
            .ConfigureAwait(false);
        return companies.Select(company => new AiBudgetCompanyRow(
            company.Id, company.LegalName, company.Firm ?? string.Empty,
            Build(company.Id, budgets.GetValueOrDefault(company.Id), defaultAllowance, usage.TryGetValue(company.Id, out var used) ? used.Cost : 0m, usage.TryGetValue(company.Id, out var calls) ? calls.Calls : 0))).ToList();
    }

    private async Task<CompanyAiBudget> EnsureAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await context.Companies.AnyAsync(company => company.Id == companyId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("A empresa não existe.");
        }

        var budget = await context.CompanyAiBudgets.SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (budget is null)
        {
            budget = new CompanyAiBudget(companyId);
            context.CompanyAiBudgets.Add(budget);
        }

        return budget;
    }

    private static AiBudgetStatus Build(Guid companyId, CompanyAiBudget? budget, decimal? defaultAllowance, decimal spent, int calls)
    {
        var allowance = budget?.MonthlyAllowanceUsd ?? defaultAllowance;
        var extra = budget?.ExtraCreditUsd ?? 0m;
        decimal? available = allowance is null ? null : Math.Max(0m, allowance.Value - spent) + extra;
        return new AiBudgetStatus(companyId, allowance, budget?.MonthlyAllowanceUsd is null, spent, extra, available, calls);
    }
}
