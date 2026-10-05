using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um mapa guardado, com a definição já desserializada.</summary>
public sealed record FinancialMapView(Guid Id, string Name, string? Description, MapDefinition Definition, DateTimeOffset? CreatedAtUtc);

/// <summary>Resultado de uma escrita: o registo (se passou), os erros de validação, ou 404.</summary>
public sealed record FinancialMapWriteResult(FinancialMapView? Saved, IReadOnlyList<string> Errors, bool NotFound)
{
    public static FinancialMapWriteResult Missing => new(null, [], NotFound: true);
    public static FinancialMapWriteResult Invalid(IReadOnlyList<string> errors) => new(null, errors, NotFound: false);
    public static FinancialMapWriteResult Ok(FinancialMapView view) => new(view, [], NotFound: false);
}

/// <summary>
/// Os mapas desenhados pelo utilizador (F4B-03): guardar, correr e explicar. Correr um
/// mapa é ler os valores das contas de que ele precisa (<see cref="MapDataProvider"/>)
/// e entregá-los ao motor; o resultado traz os avisos de cobertura — contas com valores
/// que nenhuma linha apanha, ou que duas linhas apanham.
/// </summary>
public sealed class FinancialMapService(AccountingDbContext context, MapDataProvider data)
{
    public async Task<IReadOnlyList<FinancialMapView>?> ListAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await context.FinancialMaps
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name, item.Description, item.DefinitionJson, CreatedAtUtc = EF.Property<DateTimeOffset?>(item, "CreatedAtUtc") })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(row => new FinancialMapView(row.Id, row.Name, row.Description, MapJson.Deserialize(row.DefinitionJson) ?? new MapDefinition([], []), row.CreatedAtUtc))
            .ToList();
    }

    public async Task<FinancialMapView?> GetAsync(Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list?.FirstOrDefault(item => item.Id == id);
    }

    public async Task<FinancialMapWriteResult> CreateAsync(
        Guid userId, Guid companyId, string name, string? description, MapDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return FinancialMapWriteResult.Missing;
        }

        var errors = await ValidateForWriteAsync(companyId, null, name, definition, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return FinancialMapWriteResult.Invalid(errors);
        }

        var entity = FinancialMap.Create(companyId, name, description, MapJson.Serialize(definition));
        context.FinancialMaps.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FinancialMapWriteResult.Ok(new FinancialMapView(entity.Id, entity.Name, entity.Description, definition, null));
    }

    public async Task<FinancialMapWriteResult> UpdateAsync(
        Guid userId, Guid companyId, Guid id, string name, string? description, MapDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return FinancialMapWriteResult.Missing;
        }

        var entity = await context.FinancialMaps
            .FirstOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return FinancialMapWriteResult.Missing;
        }

        var errors = await ValidateForWriteAsync(companyId, id, name, definition, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return FinancialMapWriteResult.Invalid(errors);
        }

        entity.Update(name, description, MapJson.Serialize(definition));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FinancialMapWriteResult.Ok(new FinancialMapView(entity.Id, entity.Name, entity.Description, definition, null));
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var entity = await context.FinancialMaps
            .FirstOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        context.FinancialMaps.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Corre uma definição (guardada ou ainda não). <c>null</c> sem acesso à empresa.</summary>
    /// <exception cref="ArgumentException">A definição ou o pedido não são válidos (mensagem em português).</exception>
    public async Task<MapResult?> RunAsync(
        Guid userId, Guid companyId, MapDefinition definition, MapRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var problems = MapEngine.Validate(definition);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(definition));
        }

        var accounts = await LoadAsync(companyId, definition, request, cancellationToken).ConfigureAwait(false);
        var result = MapEngine.Run(definition, request, accounts);
        var (uncovered, repeated) = MapEngine.Coverage(definition, request, accounts);
        var warnings = new List<string>(result.Warnings);
        if (uncovered.Count > 0)
        {
            warnings.Add($"{uncovered.Count} conta(s) com valores não entram em nenhuma linha: {Sample(uncovered)}.");
        }

        if (repeated.Count > 0)
        {
            warnings.Add($"{repeated.Count} conta(s) entram em mais de uma linha: {Sample(repeated)}.");
        }

        return result with { Warnings = warnings };
    }

    /// <summary>As contas por trás de uma linha de contas. <c>null</c> sem acesso à empresa.</summary>
    public async Task<IReadOnlyList<MapContribution>?> ExplainAsync(
        Guid userId, Guid companyId, MapDefinition definition, MapRequest request, string rowCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var accounts = await LoadAsync(companyId, definition, request, cancellationToken).ConfigureAwait(false);
        return MapEngine.Explain(definition, request, accounts, rowCode);
    }

    private Task<IReadOnlyList<MapAccount>> LoadAsync(Guid companyId, MapDefinition definition, MapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Year is < 1990 or > 2100)
        {
            throw new ArgumentException("O exercício não é válido.", nameof(request));
        }

        if (request.FromMonth is < 1 or > 12 || request.ToMonth is < 1 or > 12 || request.ToMonth < request.FromMonth)
        {
            throw new ArgumentException("O período tem de ser de um mês a outro do mesmo exercício.", nameof(request));
        }

        return data.LoadAsync(companyId, [.. MapEngine.YearsNeeded(definition, request)], cancellationToken);
    }

    private static string Sample(IReadOnlyList<string> codes) =>
        string.Join(", ", codes.Take(8)) + (codes.Count > 8 ? ", …" : string.Empty);

    private async Task<IReadOnlyList<string>> ValidateForWriteAsync(
        Guid companyId, Guid? id, string name, MapDefinition definition, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("O nome do mapa é obrigatório.");
        }
        else if (await context.FinancialMaps.AnyAsync(item => item.CompanyId == companyId && item.Id != id && item.Name == name.Trim(), cancellationToken).ConfigureAwait(false))
        {
            errors.Add($"Já existe um mapa com o nome «{name.Trim()}».");
        }

        errors.AddRange(MapEngine.Validate(definition ?? new MapDefinition([], [])));
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
