using AccountingAI.Application.ManagementReports;
using AccountingAI.Domain.Reporting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// O estado do interruptor dos relatórios de gestão com IA de uma empresa, mais
/// se o narrador tem chave (para o ecrã saber se a geração é sequer possível).
/// </summary>
public sealed record CompanyAiSettingsView(
    bool Enabled,
    bool AutoMonthly,
    bool NarratorConfigured,
    string Model);

/// <summary>Leitura e gravação do <see cref="CompanyAiSettings"/> de uma empresa.</summary>
public sealed class CompanyAiSettingsService(AccountingDbContext context, IManagementReportNarrator narrator)
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

        return View(row);
    }

    public async Task<CompanyAiSettingsView?> SaveAsync(
        Guid userId, Guid companyId, bool enabled, bool autoMonthly, CancellationToken cancellationToken = default)
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

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return View(row);
    }

    private CompanyAiSettingsView View(CompanyAiSettings? row) => new(
        row?.Enabled ?? false,
        row?.AutoMonthly ?? false,
        narrator.IsConfigured,
        narrator.Model);

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
