using System.Text.Json;
using AccountingAI.Application.Reporting;
using AccountingAI.Domain.Reporting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>O modelo de impressão de um relatório, já com a identificação da empresa e do gabinete.</summary>
public sealed record ReportTemplateView(
    string ReportKey,
    IReadOnlyList<string> HeaderLines,
    bool ShowFirmName,
    bool ShowCompanyIdentity,
    string Orientation,
    string CompanyName,
    string CompanyTaxId,
    string FirmName);

/// <summary>
/// Lê e grava a configuração de impressão de um relatório fixo numa empresa
/// (<c>reporting.ReportTemplate</c>). Um relatório sem modelo guardado devolve o
/// modelo por omissão — a folha impressa tem sempre um cabeçalho.
/// </summary>
public sealed class ReportTemplateService(AccountingDbContext context)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxHeaderLines = 6;
    private const int MaxLineLength = 160;

    public async Task<ReportTemplateView?> GetAsync(
        Guid userId, Guid companyId, string reportKey, CancellationToken cancellationToken = default)
    {
        if (!ReportTemplateKeys.IsKnown(reportKey))
        {
            return null;
        }

        var company = await CompanyIfAccessibleAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var stored = await context.ReportTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CompanyId == companyId && item.ReportKey == reportKey.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

        return stored is null
            ? new ReportTemplateView(reportKey.Trim().ToLowerInvariant(), [], true, true, nameof(ReportOrientation.Portrait),
                company.Name, company.TaxId, company.FirmName)
            : ToView(stored, company);
    }

    public async Task<ReportTemplateView?> SaveAsync(
        Guid userId,
        Guid companyId,
        string reportKey,
        IReadOnlyList<string> headerLines,
        bool showFirmName,
        bool showCompanyIdentity,
        string? orientation,
        CancellationToken cancellationToken = default)
    {
        if (!ReportTemplateKeys.IsKnown(reportKey))
        {
            return null;
        }

        var company = await CompanyIfAccessibleAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var key = reportKey.Trim().ToLowerInvariant();
        var lines = headerLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim().Length <= MaxLineLength ? line.Trim() : line.Trim()[..MaxLineLength])
            .Take(MaxHeaderLines)
            .ToList();
        var parsedOrientation = string.Equals(orientation, nameof(ReportOrientation.Landscape), StringComparison.OrdinalIgnoreCase)
            ? ReportOrientation.Landscape
            : ReportOrientation.Portrait;

        var entity = await context.ReportTemplates
            .FirstOrDefaultAsync(item => item.CompanyId == companyId && item.ReportKey == key, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            entity = ReportTemplate.Create(companyId, key);
            context.ReportTemplates.Add(entity);
        }

        entity.Update(JsonSerializer.Serialize(lines, Json), showFirmName, showCompanyIdentity, parsedOrientation);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(entity, company);
    }

    private static ReportTemplateView ToView(ReportTemplate template, CompanyIdentity company) => new(
        template.ReportKey,
        JsonSerializer.Deserialize<List<string>>(template.HeaderLinesJson, Json) ?? [],
        template.ShowFirmName,
        template.ShowCompanyIdentity,
        template.Orientation.ToString(),
        company.Name,
        company.TaxId,
        company.FirmName);

    private async Task<CompanyIdentity?> CompanyIfAccessibleAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId &&
                company.IsActive &&
                company.UserMemberships.Any(m => m.UserId == userId && m.IsActive))
            .Select(company => new CompanyIdentity(
                company.LegalName,
                company.TaxRegistrationNumber,
                company.AccountingFirm.Name))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private sealed record CompanyIdentity(string Name, string TaxId, string FirmName);
}
