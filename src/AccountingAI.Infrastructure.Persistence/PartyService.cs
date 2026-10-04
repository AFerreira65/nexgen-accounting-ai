using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Saft;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Um terceiro com os códigos das subcontas a que está ligado.</summary>
public sealed record PartyRow(
    Guid Id,
    string CountryCode,
    string TaxRegistrationNumber,
    string Name,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerAccountCode,
    string? SupplierAccountCode,
    bool IsActive,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

/// <summary>Uma página de terceiros e o total do conjunto filtrado.</summary>
public sealed record PartyPage(int Total, IReadOnlyList<PartyRow> Items);

/// <summary>O filtro de função da lista de terceiros.</summary>
public enum PartyRoleFilter
{
    Todos = 0,
    Clientes = 1,
    Fornecedores = 2,
}

/// <summary>Os dados editáveis de um terceiro (o país e o NIF só se definem na criação).</summary>
public sealed record PartyInput(
    string Name,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerAccountCode,
    string? SupplierAccountCode,
    bool IsActive = true,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

/// <summary>
/// Mantém os terceiros (<see cref="Party"/>) de uma empresa: lista paginada com
/// pesquisa, criação e edição. Um NIF português é validado pelo dígito de
/// controlo; o par país + NIF é único por empresa. As regras de validação
/// falhadas saem como <see cref="ArgumentException"/> ou
/// <see cref="InvalidOperationException"/> com a mensagem em pt-PT.
/// </summary>
public sealed class PartyService(AccountingDbContext context)
{
    public const int MaxPageSize = 200;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PartyPage?> ListAsync(
        Guid userId, Guid companyId, string? search, PartyRoleFilter role, bool includeInactive,
        int page, int pageSize, CancellationToken cancellationToken = default, string? accountCode = null)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var query = context.Parties.AsNoTracking().Where(party => party.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(accountCode))
        {
            // Os terceiros ligados a uma subconta: serve o editor de lançamentos
            // para propor o terceiro ao escolher a conta.
            var code = accountCode.Trim().ToUpperInvariant();
            query = query.Where(party =>
                context.Accounts.Any(account => account.CompanyId == companyId && account.Code == code &&
                    (account.Id == party.CustomerAccountId || account.Id == party.SupplierAccountId)));
        }

        if (!includeInactive)
        {
            query = query.Where(party => party.IsActive);
        }

        query = role switch
        {
            PartyRoleFilter.Clientes => query.Where(party => party.IsCustomer),
            PartyRoleFilter.Fornecedores => query.Where(party => party.IsSupplier),
            _ => query,
        };

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(party => party.Name.Contains(term) || party.TaxRegistrationNumber.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var items = await Project(query
                .OrderBy(party => party.Name)
                .ThenBy(party => party.TaxRegistrationNumber)
                .Skip((Math.Max(page, 1) - 1) * size)
                .Take(size))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PartyPage(total, items);
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PartyRow?> CreateAsync(
        Guid userId, Guid companyId, string? countryCode, string taxRegistrationNumber, PartyInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var party = Party.Create(companyId, countryCode, taxRegistrationNumber, input.Name);
        if (party.CountryCode == Party.DefaultCountryCode && !PortugueseTaxNumber.IsValid(party.TaxRegistrationNumber))
        {
            throw new ArgumentException("O NIF português não é válido (nove dígitos com dígito de controlo).");
        }

        var duplicate = await context.Parties
            .AnyAsync(
                item => item.CompanyId == companyId &&
                    item.CountryCode == party.CountryCode &&
                    item.TaxRegistrationNumber == party.TaxRegistrationNumber,
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new InvalidOperationException("Já existe um terceiro com esse país e número de contribuinte na empresa.");
        }

        await ApplyAsync(party, input, cancellationToken).ConfigureAwait(false);
        context.Parties.Add(party);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await FindRowAsync(companyId, party.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <returns><c>null</c> sem acesso à empresa ou terceiro desconhecido.</returns>
    public async Task<PartyRow?> UpdateAsync(
        Guid userId, Guid companyId, Guid partyId, PartyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var party = await context.Parties
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == partyId, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return null;
        }

        party.Rename(input.Name);
        party.SetActive(input.IsActive);
        await ApplyAsync(party, input, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await FindRowAsync(companyId, party.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAsync(Party party, PartyInput input, CancellationToken cancellationToken)
    {
        party.SetRoles(input.IsCustomer, input.IsSupplier);
        party.SetAddress(input.AddressDetail, input.City, input.PostalCode);
        party.LinkAccounts(
            await ResolveAccountAsync(party.CompanyId, input.CustomerAccountCode, "de cliente", cancellationToken).ConfigureAwait(false),
            await ResolveAccountAsync(party.CompanyId, input.SupplierAccountCode, "de fornecedor", cancellationToken).ConfigureAwait(false));
    }

    private async Task<Guid?> ResolveAccountAsync(
        Guid companyId, string? accountCode, string role, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            return null;
        }

        var normalized = accountCode.Trim().ToUpperInvariant();
        var account = await context.Accounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Code == normalized)
            .Select(item => new { item.Id, item.AllowsPosting })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            throw new InvalidOperationException($"A conta {role} {normalized} não existe na empresa.");
        }

        if (!account.AllowsPosting)
        {
            throw new InvalidOperationException($"A conta {role} {normalized} é de integração; escolha uma conta de movimento.");
        }

        return account.Id;
    }

    private Task<PartyRow?> FindRowAsync(Guid companyId, Guid partyId, CancellationToken cancellationToken) =>
        Project(context.Parties.AsNoTracking().Where(party => party.CompanyId == companyId && party.Id == partyId))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<PartyRow> Project(IQueryable<Party> query) =>
        query.Select(party => new PartyRow(
            party.Id,
            party.CountryCode,
            party.TaxRegistrationNumber,
            party.Name,
            party.IsCustomer,
            party.IsSupplier,
            context.Accounts.Where(account => account.Id == party.CustomerAccountId).Select(account => account.Code).FirstOrDefault(),
            context.Accounts.Where(account => account.Id == party.SupplierAccountId).Select(account => account.Code).FirstOrDefault(),
            party.IsActive,
            party.AddressDetail,
            party.City,
            party.PostalCode));

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
