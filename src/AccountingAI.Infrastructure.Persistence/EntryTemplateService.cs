using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha de um lançamento-tipo, com as enumerações pelo nome.</summary>
public sealed record EntryTemplateLineRow(
    string Kind,
    string Side,
    string AccountCode,
    string AccountName,
    string? VatCode,
    string? Description);

public sealed record EntryTemplateRow(
    string Code,
    string Name,
    string JournalCode,
    string? DocumentTypeCode,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntryTemplateLineRow> Lines);

public sealed record EntryTemplateLineInput(
    EntryTemplateLineKind Kind,
    EntryTemplateSide Side,
    string AccountCode,
    string? VatCode,
    string? Description);

public sealed record EntryTemplateInput(
    string Name,
    string JournalCode,
    string? DocumentTypeCode,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntryTemplateLineInput> Lines);

/// <summary>Uma linha já calculada, pronta para o editor de lançamentos.</summary>
public sealed record AppliedTemplateLine(
    string Kind,
    string AccountCode,
    string Description,
    decimal Debit,
    decimal Credit,
    string? VatCode);

public sealed record AppliedTemplate(
    string Code,
    string JournalCode,
    string? DocumentTypeCode,
    string? Description,
    IReadOnlyList<AppliedTemplateLine> Lines);

/// <summary>
/// Mantém os lançamentos-tipo (<see cref="EntryTemplate"/>) de uma empresa e
/// calcula as linhas de um lançamento a partir de um deles. Aplicar um
/// lançamento-tipo só calcula — não lança nada; o lançamento continua a passar
/// pelo <see cref="JournalPostingService"/>, com as suas regras.
/// </summary>
public sealed class EntryTemplateService(AccountingDbContext context)
{
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IReadOnlyList<EntryTemplateRow>?> ListAsync(
        Guid userId, Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var templates = await context.EntryTemplates
            .AsNoTracking()
            .Include(item => item.Lines)
            .Where(item => item.CompanyId == companyId && (includeInactive || item.IsActive))
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var lookups = await LoadLookupsAsync(companyId, cancellationToken).ConfigureAwait(false);
        return templates.Select(template => ToRow(template, lookups)).ToList();
    }

    /// <summary>Cria ou substitui o lançamento-tipo com esse código.</summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<EntryTemplateRow?> SaveAsync(
        Guid userId, Guid companyId, string code, EntryTemplateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalizedCode = EntryTemplate.NormalizeCode(code);
        var lookups = await LoadLookupsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var journalCode = Normalize(input.JournalCode);
        if (!lookups.JournalsByCode.TryGetValue(journalCode, out var journalId))
        {
            throw new ArgumentException($"O diário {journalCode} não existe na empresa.");
        }

        Guid? documentTypeId = null;
        if (!string.IsNullOrWhiteSpace(input.DocumentTypeCode))
        {
            var documentTypeCode = Normalize(input.DocumentTypeCode);
            documentTypeId = lookups.DocumentTypesByCode.TryGetValue(documentTypeCode, out var found)
                ? found
                : throw new ArgumentException($"O tipo de documento {documentTypeCode} não existe na empresa.");
        }

        var lines = (input.Lines ?? []).Select(line =>
        {
            var accountCode = Normalize(line.AccountCode);
            if (!lookups.PostingAccountsByCode.TryGetValue(accountCode, out var accountId))
            {
                throw new ArgumentException($"A conta {accountCode} não existe na empresa ou não é de movimento.");
            }

            Guid? vatCodeId = null;
            if (!string.IsNullOrWhiteSpace(line.VatCode))
            {
                var vatCode = Normalize(line.VatCode);
                vatCodeId = lookups.VatCodesByCode.TryGetValue(vatCode, out var vat)
                    ? vat
                    : throw new ArgumentException($"O código de IVA {vatCode} não existe na empresa.");
            }

            return new EntryTemplateLine(line.Kind, line.Side, accountId, vatCodeId, line.Description);
        }).ToList();

        var template = await context.EntryTemplates
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalizedCode, cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
        {
            template = EntryTemplate.Create(companyId, normalizedCode);
            context.EntryTemplates.Add(template);
        }

        template.Update(input.Name, journalId, documentTypeId, input.Description, input.IsActive);
        template.ReplaceLines(lines);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRow(template, lookups);
    }

    /// <summary>Calcula as linhas de um lançamento a partir do lançamento-tipo; não lança.</summary>
    /// <returns><c>null</c> sem acesso à empresa ou lançamento-tipo desconhecido.</returns>
    public async Task<AppliedTemplate?> ApplyAsync(
        Guid userId, Guid companyId, string code, decimal amount, bool amountIncludesVat,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalizedCode = Normalize(code);
        var template = await context.EntryTemplates
            .AsNoTracking()
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalizedCode, cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
        {
            return null;
        }

        if (!template.IsActive)
        {
            throw new InvalidOperationException("O lançamento-tipo está inativo.");
        }

        var lookups = await LoadLookupsAsync(companyId, cancellationToken).ConfigureAwait(false);
        var computed = template.Compute(amount, amountIncludesVat, lookups.VatPercentages);

        return new AppliedTemplate(
            template.Code,
            lookups.JournalCodes[template.JournalId],
            template.DocumentTypeId is { } documentTypeId ? lookups.DocumentTypeCodes[documentTypeId] : null,
            template.Description,
            computed
                .Select(item => new AppliedTemplateLine(
                    item.Line.Kind.ToString(),
                    lookups.Accounts[item.Line.AccountId].Code,
                    item.Line.Description ?? template.Description ?? template.Name,
                    item.Debit,
                    item.Credit,
                    item.Line.VatCodeId is { } vatCodeId ? lookups.VatCodes[vatCodeId] : null))
                .ToList());
    }

    private static EntryTemplateRow ToRow(EntryTemplate template, Lookups lookups) => new(
        template.Code,
        template.Name,
        lookups.JournalCodes[template.JournalId],
        template.DocumentTypeId is { } documentTypeId ? lookups.DocumentTypeCodes[documentTypeId] : null,
        template.Description,
        template.IsActive,
        template.Lines
            .OrderBy(line => line.Sequence)
            .Select(line => new EntryTemplateLineRow(
                line.Kind.ToString(),
                line.Side.ToString(),
                lookups.Accounts[line.AccountId].Code,
                lookups.Accounts[line.AccountId].Name,
                line.VatCodeId is { } vatCodeId ? lookups.VatCodes[vatCodeId] : null,
                line.Description))
            .ToList());

    private async Task<Lookups> LoadLookupsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var journals = await context.Journals.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Id, item.Code })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var documentTypes = await context.AccountingDocumentTypes.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Id, item.Code })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Id, item.Code, item.Name, item.AllowsPosting })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var vatCodes = await context.VatCodes.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Id, item.Code, item.Percentage })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new Lookups(
            journals.ToDictionary(item => item.Code, item => item.Id, StringComparer.Ordinal),
            journals.ToDictionary(item => item.Id, item => item.Code),
            documentTypes.ToDictionary(item => item.Code, item => item.Id, StringComparer.Ordinal),
            documentTypes.ToDictionary(item => item.Id, item => item.Code),
            accounts.Where(item => item.AllowsPosting).ToDictionary(item => item.Code, item => item.Id, StringComparer.Ordinal),
            accounts.ToDictionary(item => item.Id, item => (item.Code, item.Name)),
            vatCodes.ToDictionary(item => item.Code, item => item.Id, StringComparer.Ordinal),
            vatCodes.ToDictionary(item => item.Id, item => item.Code),
            vatCodes.ToDictionary(item => item.Id, item => item.Percentage));
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);

    private sealed record Lookups(
        Dictionary<string, Guid> JournalsByCode,
        Dictionary<Guid, string> JournalCodes,
        Dictionary<string, Guid> DocumentTypesByCode,
        Dictionary<Guid, string> DocumentTypeCodes,
        Dictionary<string, Guid> PostingAccountsByCode,
        Dictionary<Guid, (string Code, string Name)> Accounts,
        Dictionary<string, Guid> VatCodesByCode,
        Dictionary<Guid, string> VatCodes,
        Dictionary<Guid, decimal> VatPercentages);
}
