using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Saft;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>O que a importação faz (ou faria) a um terceiro da origem.</summary>
public enum PartyImportState
{
    New = 0,
    Updated = 1,
    Unchanged = 2,
}

/// <summary>Um terceiro da origem, já fundido por país + contribuinte, e o seu destino.</summary>
public sealed record PartyImportItem(
    string CountryCode,
    string TaxRegistrationNumber,
    string Name,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerAccountCode,
    string? SupplierAccountCode,
    string State);

/// <summary>Quantos registos da origem ficaram de fora, por motivo.</summary>
public sealed record PartyImportSkipped(string Reason, int Count);

/// <summary>
/// O resultado de uma análise (<see cref="Applied"/> falso) ou de uma importação
/// de terceiros. <see cref="Items"/> traz no máximo
/// <see cref="PartyImportService.MaxItems"/> linhas, com os novos e os
/// atualizados primeiro; as contagens são sempre do conjunto inteiro.
/// </summary>
public sealed record PartyImportResult(
    bool Applied,
    int SourceCount,
    int NewCount,
    int UpdatedCount,
    int UnchangedCount,
    int SkippedCount,
    IReadOnlyList<PartyImportItem> Items,
    IReadOnlyList<PartyImportSkipped> Skipped);

/// <summary>
/// Cria os terceiros em falta a partir dos clientes e fornecedores de um SAF-T
/// ou dos emitentes das compras do e-Fatura. Nunca altera o nome nem desfaz uma
/// ligação de um terceiro existente: só lhe acrescenta a função e a subconta
/// que faltarem. O consumidor final, os registos sem contribuinte e os NIF
/// portugueses inválidos ficam de fora, contados por motivo.
/// </summary>
public sealed class PartyImportService(ISaftPartiesReader partiesReader, AccountingDbContext context)
{
    public const int MaxItems = 200;

    private const string FinalConsumerTaxId = "999999990";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PartyImportResult?> FromSaftAsync(
        Guid userId, Guid companyId, Stream content, bool apply, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var source = await partiesReader.ReadAsync(content, cancellationToken).ConfigureAwait(false);
        var candidates = source.Select(party => new Candidate(
            party.Country,
            party.TaxId,
            party.Name,
            party.Kind == SaftPartyKind.Customer,
            party.Kind == SaftPartyKind.Supplier,
            party.Kind == SaftPartyKind.Customer ? party.AccountId : null,
            party.Kind == SaftPartyKind.Supplier ? party.AccountId : null,
            party.AddressDetail,
            party.City,
            party.PostalCode));

        return await RunAsync(companyId, candidates, source.Count, apply, cancellationToken).ConfigureAwait(false);
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<PartyImportResult?> FromEFaturaAsync(
        Guid userId, Guid companyId, bool apply, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var suppliers = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(purchase => purchase.CompanyId == companyId)
            .GroupBy(purchase => purchase.SupplierTaxId)
            .Select(group => new { TaxId = group.Key, Name = group.Max(purchase => purchase.SupplierName) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var candidates = suppliers.Select(supplier => new Candidate(
            Party.DefaultCountryCode, supplier.TaxId, supplier.Name ?? string.Empty, false, true, null, null, null, null, null));

        return await RunAsync(companyId, candidates, suppliers.Count, apply, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartyImportResult> RunAsync(
        Guid companyId, IEnumerable<Candidate> candidates, int sourceCount, bool apply, CancellationToken cancellationToken)
    {
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        var merged = new Dictionary<(string Country, string TaxId), Candidate>();

        foreach (var candidate in candidates)
        {
            if (!TryNormalize(candidate, out var normalized, out var reason))
            {
                skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                continue;
            }

            var key = (normalized.Country!, normalized.TaxId);
            merged[key] = merged.TryGetValue(key, out var current)
                ? current with
                {
                    IsCustomer = current.IsCustomer || normalized.IsCustomer,
                    IsSupplier = current.IsSupplier || normalized.IsSupplier,
                    CustomerAccountCode = current.CustomerAccountCode ?? normalized.CustomerAccountCode,
                    SupplierAccountCode = current.SupplierAccountCode ?? normalized.SupplierAccountCode,
                    AddressDetail = current.AddressDetail ?? normalized.AddressDetail,
                    City = current.City ?? normalized.City,
                    PostalCode = current.PostalCode ?? normalized.PostalCode,
                }
                : normalized;
        }

        var existingQuery = context.Parties.Where(party => party.CompanyId == companyId);
        var existing = await (apply ? existingQuery : existingQuery.AsNoTracking())
            .ToDictionaryAsync(party => (party.CountryCode, party.TaxRegistrationNumber), cancellationToken)
            .ConfigureAwait(false);
        var postingAccounts = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.AllowsPosting)
            .Select(account => new { account.Id, account.Code })
            .ToDictionaryAsync(account => account.Code, account => account.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
        var accountCodes = postingAccounts.ToDictionary(pair => pair.Value, pair => pair.Key);

        var items = new List<(PartyImportState State, PartyImportItem Item)>(merged.Count);
        foreach (var candidate in merged.Values)
        {
            var customerAccountId = Resolve(postingAccounts, candidate.CustomerAccountCode);
            var supplierAccountId = Resolve(postingAccounts, candidate.SupplierAccountCode);
            PartyImportState state;

            if (existing.TryGetValue((candidate.Country!, candidate.TaxId), out var party))
            {
                var isCustomer = party.IsCustomer || candidate.IsCustomer;
                var isSupplier = party.IsSupplier || candidate.IsSupplier;
                var customerAccount = party.CustomerAccountId ?? customerAccountId;
                var supplierAccount = party.SupplierAccountId ?? supplierAccountId;
                // A morada só se preenche num terceiro que ainda não tem nenhuma.
                var fillAddress = party.AddressDetail is null && party.City is null && party.PostalCode is null &&
                    (candidate.AddressDetail ?? candidate.City ?? candidate.PostalCode) is not null;
                var changed = isCustomer != party.IsCustomer || isSupplier != party.IsSupplier ||
                    customerAccount != party.CustomerAccountId || supplierAccount != party.SupplierAccountId || fillAddress;
                state = changed ? PartyImportState.Updated : PartyImportState.Unchanged;
                if (changed && apply)
                {
                    party.SetRoles(isCustomer, isSupplier);
                    party.LinkAccounts(customerAccount, supplierAccount);
                    if (fillAddress)
                    {
                        party.SetAddress(candidate.AddressDetail, candidate.City, candidate.PostalCode);
                    }
                }

                items.Add((state, new PartyImportItem(
                    party.CountryCode, party.TaxRegistrationNumber, party.Name, isCustomer, isSupplier,
                    CodeOf(accountCodes, customerAccount), CodeOf(accountCodes, supplierAccount), state.ToString())));
                continue;
            }

            state = PartyImportState.New;
            if (apply)
            {
                var created = Party.Create(companyId, candidate.Country, candidate.TaxId, candidate.Name);
                created.SetRoles(candidate.IsCustomer, candidate.IsSupplier);
                created.LinkAccounts(customerAccountId, supplierAccountId);
                created.SetAddress(candidate.AddressDetail, candidate.City, candidate.PostalCode);
                context.Parties.Add(created);
            }

            items.Add((state, new PartyImportItem(
                candidate.Country!, candidate.TaxId, candidate.Name, candidate.IsCustomer, candidate.IsSupplier,
                CodeOf(accountCodes, customerAccountId), CodeOf(accountCodes, supplierAccountId), state.ToString())));
        }

        if (apply)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new PartyImportResult(
            apply,
            sourceCount,
            items.Count(item => item.State == PartyImportState.New),
            items.Count(item => item.State == PartyImportState.Updated),
            items.Count(item => item.State == PartyImportState.Unchanged),
            skipped.Values.Sum(),
            items
                .OrderBy(item => item.State)
                .ThenBy(item => item.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxItems)
                .Select(item => item.Item)
                .ToList(),
            skipped
                .OrderByDescending(pair => pair.Value)
                .Select(pair => new PartyImportSkipped(pair.Key, pair.Value))
                .ToList());
    }

    /// <summary>
    /// A chave (país, contribuinte) com que um cliente ou fornecedor de um SAF-T
    /// fica como terceiro, pelas mesmas regras da importação; <c>null</c> quando
    /// a importação o deixaria de fora (consumidor final, NIF inválido, …).
    /// </summary>
    public static (string Country, string TaxNumber)? KeyOf(SaftParty party)
    {
        ArgumentNullException.ThrowIfNull(party);
        var candidate = new Candidate(party.Country, party.TaxId, string.IsNullOrWhiteSpace(party.Name) ? "-" : party.Name, false, false, null, null, null, null, null);
        return TryNormalize(candidate, out var normalized, out _) ? (normalized.Country!, normalized.TaxId) : null;
    }

    private static bool TryNormalize(Candidate candidate, out Candidate normalized, out string reason)
    {
        normalized = candidate;
        reason = string.Empty;

        var taxId = new string((candidate.TaxId ?? string.Empty).Where(character => !char.IsWhiteSpace(character)).ToArray())
            .ToUpperInvariant();
        if (taxId.Length == 0)
        {
            reason = "Sem número de contribuinte";
            return false;
        }

        if (taxId == FinalConsumerTaxId)
        {
            reason = "Consumidor final (999999990)";
            return false;
        }

        if (taxId.Length > Party.TaxRegistrationNumberMaxLength)
        {
            reason = "Número de contribuinte demasiado longo";
            return false;
        }

        var name = (candidate.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            reason = "Sem nome";
            return false;
        }

        // O SAF-T admite «Desconhecido» no país; tudo o que não seja um código
        // de duas letras é tratado como Portugal e validado como tal.
        var country = candidate.Country?.Trim().ToUpperInvariant();
        if (country is not { Length: 2 } || !country.All(char.IsAsciiLetter))
        {
            country = Party.DefaultCountryCode;
        }

        if (country == Party.DefaultCountryCode && !PortugueseTaxNumber.IsValid(taxId))
        {
            reason = "NIF português inválido";
            return false;
        }

        normalized = candidate with
        {
            Country = country,
            TaxId = taxId,
            Name = name.Length > Party.NameMaxLength ? name[..Party.NameMaxLength] : name,
            AddressDetail = Clean(candidate.AddressDetail, AccountingAI.Domain.Organizations.PostalAddress.AddressDetailMaxLength),
            City = Clean(candidate.City, AccountingAI.Domain.Organizations.PostalAddress.CityMaxLength),
            PostalCode = Clean(candidate.PostalCode, AccountingAI.Domain.Organizations.PostalAddress.PostalCodeMaxLength),
        };
        return true;
    }

    /// <summary>Um campo de morada do ficheiro: «Desconhecido» e os vazios não contam; corta ao máximo.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("Desconhecido", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return text.Length > maxLength ? text[..maxLength] : text;
    }

    private static Guid? Resolve(Dictionary<string, Guid> postingAccounts, string? accountCode) =>
        !string.IsNullOrWhiteSpace(accountCode) && postingAccounts.TryGetValue(accountCode.Trim(), out var id) ? id : null;

    private static string? CodeOf(Dictionary<Guid, string> accountCodes, Guid? accountId) =>
        accountId is { } id && accountCodes.TryGetValue(id, out var code) ? code : null;

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);

    private sealed record Candidate(
        string? Country,
        string TaxId,
        string Name,
        bool IsCustomer,
        bool IsSupplier,
        string? CustomerAccountCode,
        string? SupplierAccountCode,
        string? AddressDetail,
        string? City,
        string? PostalCode);
}
