using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// O estado do interruptor dos relatórios de gestão com IA de uma empresa, mais
/// se o narrador tem chave (para o ecrã saber se a geração é sequer possível).
/// </summary>
public sealed record CompanyAiSettingsView(
    bool Enabled,
    bool AutoMonthly,
    bool NarratorConfigured,
    string Model,
    bool AnonymizeNames = true,
    int? ConversationRetentionDays = null,
    AiBudgetStatus? Budget = null);

/// <summary>Leitura e gravação do <see cref="CompanyAiSettings"/> de uma empresa.</summary>
public sealed class CompanyAiSettingsService(AccountingDbContext context, IManagementReportNarrator narrator, AiBudgetService? budget = null, AiRetentionService? retention = null)
{
    public async Task<CompanyAiSettingsView?> GetAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = await context.CompanyAiSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        return await ViewAsync(row, companyId, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="anonymizeNames"><c>null</c> mantém o que estava.</param>
    /// <param name="setRetention"><c>true</c> aplica <paramref name="retentionDays"/> (que pode ser <c>null</c>, «sem prazo»).</param>
    public async Task<CompanyAiSettingsView?> SaveAsync(
        Guid userId, Guid companyId, bool enabled, bool autoMonthly, CancellationToken cancellationToken = default,
        bool? anonymizeNames = null, int? retentionDays = null, bool setRetention = false)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = await context.CompanyAiSettings
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = CompanyAiSettings.Create(companyId);
            row.Update(enabled, autoMonthly);
            context.CompanyAiSettings.Add(row);
        }
        else
        {
            row.Update(enabled, autoMonthly);
        }

        if (anonymizeNames is not null || setRetention)
        {
            row.ConfigurePrivacy(anonymizeNames ?? row.AnonymizeNames, setRetention ? retentionDays : row.ConversationRetentionDays);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (setRetention && retention is not null)
        {
            // O novo prazo vale já: apagam-se logo as conversas que o passam.
            await retention.PurgeCompanyAsync(companyId, row.ConversationRetentionDays, cancellationToken).ConfigureAwait(false);
        }

        return await ViewAsync(row, companyId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CompanyAiSettingsView> ViewAsync(CompanyAiSettings? row, Guid companyId, CancellationToken cancellationToken) => new(
        row?.Enabled ?? false,
        row?.AutoMonthly ?? false,
        narrator.IsConfigured,
        narrator.Model,
        row?.AnonymizeNames ?? true,
        row?.ConversationRetentionDays,
        budget is null ? null : await budget.StatusAsync(companyId, cancellationToken).ConfigureAwait(false));

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
