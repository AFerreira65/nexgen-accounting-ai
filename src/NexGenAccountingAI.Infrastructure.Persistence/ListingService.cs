using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma listagem guardada, com a definição já desserializada.</summary>
/// <param name="Mine">O utilizador é o dono (pode mudar o alcance e apagar).</param>
public sealed record SavedListingView(
    Guid Id, string Name, string? Description, string Scope, bool IsFavorite, bool Mine, ListingDefinition Definition);

/// <summary>Resultado de uma escrita: o registo (se passou), os erros de validação, ou 404.</summary>
public sealed record SavedListingWriteResult(SavedListingView? Saved, IReadOnlyList<string> Errors, bool NotFound)
{
    public static SavedListingWriteResult Missing => new(null, [], NotFound: true);

    public static SavedListingWriteResult Invalid(IReadOnlyList<string> errors) => new(null, errors, NotFound: false);

    public static SavedListingWriteResult Ok(SavedListingView view) => new(view, [], NotFound: false);
}

/// <summary>
/// As listagens do utilizador (F4D-01): guardar (privadas, da empresa ou do gabinete), correr
/// e exportar. Quem corre uma listagem vê só o que os seus direitos sobre a empresa permitem:
/// a definição nunca traz dados nem o filtro de empresa, que vem sempre da sessão.
/// </summary>
public sealed class ListingService(AccountingDbContext context, ListingEngine engine, TimeProvider time)
{
    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>As listagens que o utilizador vê nesta empresa: as suas, as da empresa e as do gabinete. <c>null</c> sem acesso.</summary>
    public async Task<IReadOnlyList<SavedListingView>?> ListAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var firmId = await FirmOfAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (firmId is null)
        {
            return null;
        }

        var rows = await context.SavedListings
            .AsNoTracking()
            .Where(item =>
                (item.Scope == ListingScope.Private && item.CompanyId == companyId && item.OwnerUserId == userId) ||
                (item.Scope == ListingScope.Company && item.CompanyId == companyId) ||
                (item.Scope == ListingScope.Firm && item.FirmId == firmId))
            .OrderByDescending(item => item.IsFavorite && item.OwnerUserId == userId)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => View(row, userId)).ToList();
    }

    public async Task<SavedListingWriteResult> CreateAsync(
        Guid userId, Guid companyId, string scope, string name, string? description, ListingDefinition definition, CancellationToken cancellationToken = default)
    {
        var firmId = await FirmOfAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (firmId is null)
        {
            return SavedListingWriteResult.Missing;
        }

        var errors = Validate(scope, name, definition, out var parsedScope);
        if (errors.Count == 0 && await NameTakenAsync(companyId, userId, parsedScope, name, null, cancellationToken).ConfigureAwait(false))
        {
            errors = ["Já tem uma listagem com esse nome e esse alcance."];
        }

        if (errors.Count > 0)
        {
            return SavedListingWriteResult.Invalid(errors);
        }

        var entity = SavedListing.Create(companyId, firmId.Value, userId, parsedScope, name, description, ListingJson.Serialize(definition));
        context.SavedListings.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SavedListingWriteResult.Ok(View(entity, userId));
    }

    public async Task<SavedListingWriteResult> UpdateAsync(
        Guid userId, Guid companyId, Guid id, string scope, string name, string? description, ListingDefinition definition, CancellationToken cancellationToken = default)
    {
        var entity = await FindEditableAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return SavedListingWriteResult.Missing;
        }

        var errors = Validate(scope, name, definition, out var parsedScope);
        if (errors.Count == 0 && parsedScope != entity.Scope && entity.OwnerUserId != userId)
        {
            errors = ["Só o dono pode mudar quem vê a listagem."];
        }

        if (errors.Count == 0 && await NameTakenAsync(entity.CompanyId, entity.OwnerUserId, parsedScope, name, id, cancellationToken).ConfigureAwait(false))
        {
            errors = ["Já existe uma listagem com esse nome e esse alcance."];
        }

        if (errors.Count > 0)
        {
            return SavedListingWriteResult.Invalid(errors);
        }

        entity.Update(parsedScope, name, description, ListingJson.Serialize(definition));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SavedListingWriteResult.Ok(View(entity, userId));
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindEditableAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        context.SavedListings.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Marca ou desmarca como favorita (só do dono). <c>false</c> se não existe ou não é do utilizador.</summary>
    public async Task<bool> SetFavoriteAsync(Guid userId, Guid companyId, Guid id, bool favorite, CancellationToken cancellationToken = default)
    {
        var entity = await FindEditableAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false);
        if (entity is null || entity.OwnerUserId != userId)
        {
            return false;
        }

        entity.SetFavorite(favorite);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Corre uma definição (guardada ou ainda não). <c>null</c> sem acesso à empresa.</summary>
    /// <exception cref="ArgumentException">A definição ou o pedido não são válidos (mensagem em português).</exception>
    public async Task<ListingResult?> RunAsync(
        Guid userId, Guid companyId, ListingDefinition definition, ListingRequest request, CancellationToken cancellationToken = default)
    {
        if (await FirmOfAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return await engine.RunAsync(companyId, definition, request, Today, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Todas as linhas da definição para exportar. <c>null</c> sem acesso à empresa.</summary>
    public async Task<(IReadOnlyList<ListingColumn> Columns, IReadOnlyList<ListingRow> Rows, bool Truncated)?> ExportAsync(
        Guid userId, Guid companyId, ListingDefinition definition, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default)
    {
        if (await FirmOfAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return await engine.ExportAsync(companyId, definition, parameters, Today, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Uma tabela cruzada sobre a definição. <c>null</c> sem acesso à empresa.</summary>
    /// <exception cref="ArgumentException">A definição ou o pedido não são válidos (mensagem em português).</exception>
    public async Task<ListingPivot?> PivotAsync(
        Guid userId, Guid companyId, ListingDefinition definition, IReadOnlyDictionary<string, string>? parameters,
        string rowField, string columnField, string measure, CancellationToken cancellationToken = default)
    {
        if (await FirmOfAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return await engine.PivotAsync(companyId, definition, parameters, rowField, columnField, measure, Today, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> Validate(string scope, string name, ListingDefinition? definition, out ListingScope parsedScope)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Indique o nome da listagem.");
        }

        if (!Enum.TryParse(scope, ignoreCase: true, out parsedScope) || !Enum.IsDefined(parsedScope))
        {
            errors.Add("O alcance tem de ser «private», «company» ou «firm».");
        }

        errors.AddRange(ListingCatalog.Validate(definition));
        return errors;
    }

    private static SavedListingView View(SavedListing row, Guid userId) => new(
        row.Id, row.Name, row.Description, row.Scope.ToString().ToLowerInvariant(), row.IsFavorite && row.OwnerUserId == userId, row.OwnerUserId == userId,
        ListingJson.Deserialize(row.DefinitionJson) ?? new ListingDefinition(ListingCatalog.JournalLines, []));

    private Task<bool> NameTakenAsync(Guid companyId, Guid ownerId, ListingScope scope, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        return context.SavedListings.AnyAsync(
            item => item.CompanyId == companyId && item.OwnerUserId == ownerId && item.Scope == scope && item.Name == trimmed && (exceptId == null || item.Id != exceptId),
            cancellationToken);
    }

    /// <summary>A listagem que o utilizador pode alterar: a sua, ou uma da empresa. <c>null</c> se não existe ou não pode.</summary>
    private async Task<SavedListing?> FindEditableAsync(Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var firmId = await FirmOfAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (firmId is null)
        {
            return null;
        }

        var entity = await context.SavedListings.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        var visible = entity.Scope switch
        {
            ListingScope.Private => entity.CompanyId == companyId && entity.OwnerUserId == userId,
            ListingScope.Company => entity.CompanyId == companyId,
            _ => entity.FirmId == firmId && entity.OwnerUserId == userId,
        };
        return visible ? entity : null;
    }

    /// <summary>O gabinete da empresa, ou <c>null</c> se o utilizador não tem acesso a ela.</summary>
    private async Task<Guid?> FirmOfAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId && company.IsActive &&
                company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .Select(company => (Guid?)company.AccountingFirmId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
