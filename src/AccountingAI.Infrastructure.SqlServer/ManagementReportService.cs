using System.Text.Json;
using AccountingAI.Application.ManagementReports;
using AccountingAI.Domain.Reporting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>Um relatório de gestão guardado, para o histórico e a consulta.</summary>
public sealed record ManagementReportView(
    Guid Id,
    string Kind,
    string? Question,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Model,
    int PromptTokens,
    int OutputTokens,
    DateTimeOffset? CreatedAtUtc,
    ManagementReportResult? Result,
    JsonElement? Dossier);

/// <summary>
/// O resultado de um pedido de geração: o relatório, ou a indicação de que a
/// empresa não existe / não tem acesso (<see cref="NotFound"/>), ou de que a
/// funcionalidade está indisponível (<see cref="Disabled"/> — interruptor
/// desligado ou sem chave).
/// </summary>
public sealed record ManagementReportGeneration(
    ManagementReportView? View,
    bool NotFound,
    bool Disabled,
    string? DisabledReason);

/// <summary>
/// Orquestra a geração: verifica o interruptor e a chave, monta o dossiê
/// (<see cref="IManagementReportDataService"/>), chama o narrador
/// (<see cref="IManagementReportNarrator"/>), persiste o
/// <see cref="ManagementReport"/> e devolve. Guarda também o histórico.
/// </summary>
public sealed class ManagementReportService(
    AccountingDbContext context,
    IManagementReportDataService data,
    IManagementReportNarrator narrator)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ManagementReportGeneration> GenerateAsync(
        Guid userId, Guid companyId, ManagementReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return new ManagementReportGeneration(null, NotFound: true, Disabled: false, null);
        }

        var settings = await context.CompanyAiSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is not { Enabled: true })
        {
            return new ManagementReportGeneration(null, false, Disabled: true,
                "Os relatórios de gestão com IA estão desligados para esta empresa.");
        }

        if (!narrator.IsConfigured)
        {
            return new ManagementReportGeneration(null, false, Disabled: true,
                "Não há chave de API de IA configurada no servidor.");
        }

        var dossier = await data.BuildDossierAsync(userId, companyId, request, cancellationToken).ConfigureAwait(false);
        if (dossier is null)
        {
            return new ManagementReportGeneration(null, NotFound: true, false, null);
        }

        var narration = await narrator.GenerateAsync(request, dossier, cancellationToken).ConfigureAwait(false);

        var kind = request.Kind is { } known ? ManagementReportCatalog.KeyOf(known) : "pergunta";
        var entity = ManagementReport.Create(
            companyId,
            kind,
            request.Question,
            request.From,
            request.To,
            narration.Model,
            JsonSerializer.Serialize(dossier, Json),
            JsonSerializer.Serialize(narration.Result, Json),
            narration.PromptTokens,
            narration.OutputTokens);

        context.ManagementReports.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var createdAt = await context.ManagementReports
            .AsNoTracking()
            .Where(item => item.Id == entity.Id)
            .Select(item => EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var view = new ManagementReportView(
            entity.Id, entity.Kind, entity.Question, entity.PeriodFrom, entity.PeriodTo,
            entity.Model, entity.PromptTokens, entity.OutputTokens, createdAt,
            narration.Result, null);
        return new ManagementReportGeneration(view, false, false, null);
    }

    public async Task<IReadOnlyList<ManagementReportView>?> ListAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await context.ManagementReports
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new
            {
                item.Id,
                item.Kind,
                item.Question,
                item.PeriodFrom,
                item.PeriodTo,
                item.Model,
                item.PromptTokens,
                item.OutputTokens,
                item.ResultJson,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"),
            })
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new ManagementReportView(
                row.Id, row.Kind, row.Question, row.PeriodFrom, row.PeriodTo,
                row.Model, row.PromptTokens, row.OutputTokens, row.CreatedAtUtc,
                Deserialize<ManagementReportResult>(row.ResultJson), null))
            .ToList();
    }

    public async Task<ManagementReportView?> GetAsync(
        Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = await context.ManagementReports
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Kind,
                item.Question,
                item.PeriodFrom,
                item.PeriodTo,
                item.Model,
                item.PromptTokens,
                item.OutputTokens,
                item.ResultJson,
                item.DossierJson,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new ManagementReportView(
            row.Id, row.Kind, row.Question, row.PeriodFrom, row.PeriodTo,
            row.Model, row.PromptTokens, row.OutputTokens, row.CreatedAtUtc,
            Deserialize<ManagementReportResult>(row.ResultJson),
            DossierElement(row.DossierJson));
    }

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static JsonElement? DossierElement(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
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
