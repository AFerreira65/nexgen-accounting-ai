using AccountingAI.Application.Reporting;
using AccountingAI.Domain.Reporting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma análise guardada, com a definição já desserializada.</summary>
public sealed record SavedAnalysisView(
    Guid Id, string Name, string? Description, AnalysisDefinition Definition, DateTimeOffset? CreatedAtUtc);

/// <summary>Resultado de uma escrita: o registo (se passou), os erros de validação, ou 404.</summary>
public sealed record SavedAnalysisWriteResult(SavedAnalysisView? Saved, IReadOnlyList<string> Errors, bool NotFound)
{
    public static SavedAnalysisWriteResult Missing => new(null, [], NotFound: true);
    public static SavedAnalysisWriteResult Invalid(IReadOnlyList<string> errors) => new(null, errors, NotFound: false);
    public static SavedAnalysisWriteResult Ok(SavedAnalysisView view) => new(view, [], NotFound: false);
}

/// <summary>
/// CRUD das análises guardadas de uma empresa. Valida a definição contra o
/// <see cref="AnalysisFieldCatalog"/> ao gravar. Só a definição é guardada — os
/// resultados são calculados pelo runner (F2-07B).
/// </summary>
public sealed class SavedAnalysisService(AccountingDbContext context)
{
    public async Task<IReadOnlyList<SavedAnalysisView>?> ListAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await context.SavedAnalyses
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.DefinitionJson,
                CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc"),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new SavedAnalysisView(
                row.Id, row.Name, row.Description,
                AnalysisJson.Deserialize(row.DefinitionJson) ?? AnalysisDefinition.Empty(AnalysisSource.Movimentos),
                row.CreatedAtUtc))
            .ToList();
    }

    public async Task<SavedAnalysisView?> GetAsync(
        Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list?.FirstOrDefault(item => item.Id == id);
    }

    public async Task<SavedAnalysisWriteResult> CreateAsync(
        Guid userId, Guid companyId, string name, string? description, AnalysisDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return SavedAnalysisWriteResult.Missing;
        }

        var errors = ValidateForWrite(name, definition);
        var nameTaken = await context.SavedAnalyses
            .AnyAsync(item => item.CompanyId == companyId && item.Name == name.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (nameTaken)
        {
            errors = [.. errors, $"Já existe uma análise com o nome «{name.Trim()}»."];
        }

        if (errors.Count > 0)
        {
            return SavedAnalysisWriteResult.Invalid(errors);
        }

        var entity = SavedAnalysis.Create(companyId, name, description, AnalysisJson.Serialize(definition));
        context.SavedAnalyses.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SavedAnalysisWriteResult.Ok(new SavedAnalysisView(entity.Id, entity.Name, entity.Description, definition, null));
    }

    public async Task<SavedAnalysisWriteResult> UpdateAsync(
        Guid userId, Guid companyId, Guid id, string name, string? description, AnalysisDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return SavedAnalysisWriteResult.Missing;
        }

        var entity = await context.SavedAnalyses
            .FirstOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return SavedAnalysisWriteResult.Missing;
        }

        var errors = ValidateForWrite(name, definition);
        var nameTaken = await context.SavedAnalyses
            .AnyAsync(item => item.CompanyId == companyId && item.Id != id && item.Name == name.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (nameTaken)
        {
            errors = [.. errors, $"Já existe uma análise com o nome «{name.Trim()}»."];
        }

        if (errors.Count > 0)
        {
            return SavedAnalysisWriteResult.Invalid(errors);
        }

        entity.Update(name, description, AnalysisJson.Serialize(definition));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SavedAnalysisWriteResult.Ok(new SavedAnalysisView(entity.Id, entity.Name, entity.Description, definition, null));
    }

    public async Task<bool> DeleteAsync(
        Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var entity = await context.SavedAnalyses
            .FirstOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        context.SavedAnalyses.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static IReadOnlyList<string> ValidateForWrite(string name, AnalysisDefinition definition)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("O nome da análise é obrigatório.");
        }

        errors.AddRange(AnalysisFieldCatalog.Validate(definition));
        return errors;
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
