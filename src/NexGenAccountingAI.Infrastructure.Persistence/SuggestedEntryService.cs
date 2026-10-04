using NexGenAccountingAI.Application.CrossCheck;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>A regra de lançamento de um fornecedor, com os nomes para mostrar.</summary>
public sealed record SupplierEntryRuleRow(
    string SupplierTaxId,
    string? SupplierName,
    string EntryTemplateCode,
    string EntryTemplateName,
    string? Description,
    bool IsActive);

public sealed record SupplierEntryRuleInput(string EntryTemplateCode, string? Description, bool IsActive);

/// <summary>Uma linha de um lançamento sugerido.</summary>
public sealed record SuggestedEntryLine(
    string AccountCode,
    string AccountName,
    string Description,
    decimal Debit,
    decimal Credit,
    string? VatCode,
    string? PartyTaxNumber);

/// <summary>
/// Uma fatura do e-Fatura ainda sem lançamento, com o lançamento proposto pela
/// regra do fornecedor. Sem regra (ou com um problema), <see cref="Lines"/> fica
/// vazio e <see cref="Problem"/> diz porquê.
/// </summary>
public sealed record SuggestedEntry(
    Guid PurchaseId,
    string SupplierTaxId,
    string SupplierName,
    string InvoiceNo,
    DateOnly InvoiceDate,
    string InvoiceType,
    bool IsCreditNote,
    decimal TaxBase,
    decimal TaxAmount,
    decimal GrossTotal,
    string? EntryTemplateCode,
    string? JournalCode,
    string? DocumentTypeCode,
    string Description,
    IReadOnlyList<SuggestedEntryLine> Lines,
    string? Problem,
    string? Warning)
{
    public bool Ready => Problem is null && Lines.Count > 0;
}

public sealed record SuggestedEntriesReport(
    DateOnly From,
    DateOnly To,
    int PurchaseCount,
    int BookedCount,
    int CancelledCount,
    IReadOnlyList<SuggestedEntry> Items)
{
    public int ReadyCount => Items.Count(item => item.Ready);

    public int WithoutRuleCount => Items.Count(item => item.EntryTemplateCode is null);
}

/// <summary>O resultado de finalizar uma sugestão: a referência do lançamento criado, ou o erro.</summary>
public sealed record SuggestedEntryOutcome(Guid DocumentId, string? Reference, string? Error);

/// <summary>
/// Lançamentos sugeridos a partir das compras do e-Fatura (F3-18A). Cada fatura
/// ainda sem lançamento recebe uma proposta pelo lançamento-tipo da regra do
/// fornecedor; finalizar cria o lançamento «por conferir» pelo
/// <see cref="JournalPostingService"/>, com origem <see cref="EntrySource.EFatura"/>,
/// e guarda a referência na fatura. Uma fatura conta como lançada quando tem um
/// lançamento ligado que não está anulado ou quando o cruzamento lhe encontra um
/// lançamento de compra (<see cref="CrossCheckBuilder.BookedInvoiceIds"/>).
/// </summary>
public sealed class SuggestedEntryService(AccountingDbContext context, JournalPostingService posting)
{
    public async Task<IReadOnlyList<SupplierEntryRuleRow>?> ListRulesAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rules = await context.SupplierEntryRules
            .AsNoTracking()
            .Where(rule => rule.CompanyId == companyId)
            .OrderBy(rule => rule.SupplierTaxId)
            .Select(rule => new
            {
                rule.SupplierTaxId,
                TemplateCode = rule.EntryTemplate!.Code,
                TemplateName = rule.EntryTemplate.Name,
                rule.Description,
                rule.IsActive
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var names = await SupplierNamesAsync(companyId, rules.Select(rule => rule.SupplierTaxId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        return rules
            .Select(rule => new SupplierEntryRuleRow(
                rule.SupplierTaxId,
                names.GetValueOrDefault(rule.SupplierTaxId),
                rule.TemplateCode,
                rule.TemplateName,
                rule.Description,
                rule.IsActive))
            .ToList();
    }

    /// <summary>Cria ou altera a regra do fornecedor com este NIF.</summary>
    public async Task<SupplierEntryRuleRow?> SaveRuleAsync(
        Guid userId, Guid companyId, string supplierTaxId, SupplierEntryRuleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var taxId = SupplierEntryRule.NormalizeTaxId(supplierTaxId);
        var templateCode = (input.EntryTemplateCode ?? string.Empty).Trim().ToUpperInvariant();
        var template = await context.EntryTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == templateCode, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException($"O lançamento-tipo '{input.EntryTemplateCode}' não existe.", nameof(input));

        var rule = await context.SupplierEntryRules
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.SupplierTaxId == taxId, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            rule = SupplierEntryRule.Create(companyId, taxId);
            context.SupplierEntryRules.Add(rule);
        }

        rule.Update(template.Id, input.Description, input.IsActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var names = await SupplierNamesAsync(companyId, [taxId], cancellationToken).ConfigureAwait(false);
        return new SupplierEntryRuleRow(
            taxId, names.GetValueOrDefault(taxId), template.Code, template.Name, rule.Description, rule.IsActive);
    }

    /// <summary>Apaga a regra; devolve <c>false</c> quando não havia regra para o NIF.</summary>
    public async Task<bool?> DeleteRuleAsync(
        Guid userId, Guid companyId, string supplierTaxId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var taxId = SupplierEntryRule.NormalizeTaxId(supplierTaxId);
        var rule = await context.SupplierEntryRules
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.SupplierTaxId == taxId, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        context.SupplierEntryRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>As faturas do período ainda sem lançamento, cada uma com a sugestão.</summary>
    public async Task<SuggestedEntriesReport?> ListAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("A data final não pode ser anterior à inicial.", nameof(to));
        }

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var purchases = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.InvoiceDate >= from && item.InvoiceDate <= to)
            .OrderBy(item => item.InvoiceDate)
            .ThenBy(item => item.SupplierName)
            .ThenBy(item => item.InvoiceNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var (pending, booked, cancelled) = await SplitAsync(companyId, purchases, from, to, cancellationToken).ConfigureAwait(false);
        var items = await SuggestAsync(companyId, pending, cancellationToken).ConfigureAwait(false);
        return new SuggestedEntriesReport(from, to, purchases.Count, booked, cancelled, items);
    }

    /// <summary>
    /// Cria os lançamentos das sugestões indicadas, um a um: uma sugestão que já
    /// não está pendente ou que falha (período bloqueado, conta sem movimento)
    /// fica com o erro e não impede as outras.
    /// </summary>
    public async Task<IReadOnlyList<SuggestedEntryOutcome>?> FinalizeAsync(
        Guid userId, Guid companyId, IReadOnlyCollection<Guid> purchaseIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(purchaseIds);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var ids = purchaseIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new ArgumentException("Indique pelo menos uma sugestão.", nameof(purchaseIds));
        }

        var purchases = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && ids.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var outcomes = new List<SuggestedEntryOutcome>();
        foreach (var missing in ids.Where(id => purchases.All(purchase => purchase.Id != id)))
        {
            outcomes.Add(new SuggestedEntryOutcome(missing, null, "A fatura não existe."));
        }

        if (purchases.Count == 0)
        {
            return outcomes;
        }

        var from = purchases.Min(item => item.InvoiceDate);
        var to = purchases.Max(item => item.InvoiceDate);
        var (pending, _, _) = await SplitAsync(companyId, purchases, from, to, cancellationToken).ConfigureAwait(false);
        var suggestions = (await SuggestAsync(companyId, pending, cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => item.PurchaseId);

        foreach (var purchase in purchases)
        {
            if (!suggestions.TryGetValue(purchase.Id, out var suggestion))
            {
                outcomes.Add(new SuggestedEntryOutcome(purchase.Id, null, "A fatura já tem lançamento ou está anulada."));
                continue;
            }

            if (!suggestion.Ready)
            {
                outcomes.Add(new SuggestedEntryOutcome(purchase.Id, null, suggestion.Problem ?? "A sugestão não tem linhas."));
                continue;
            }

            try
            {
                var posted = await posting.PostAsync(
                    companyId,
                    suggestion.JournalCode!,
                    suggestion.InvoiceDate,
                    suggestion.InvoiceNo,
                    suggestion.Description,
                    reviewed: false,
                    suggestion.Lines
                        .Select(line => new JournalEntryLineInput(
                            line.AccountCode, line.Description, line.Debit, line.Credit,
                            line.PartyTaxNumber, line.PartyTaxNumber is null ? null : "PT", line.VatCode))
                        .ToList(),
                    cancellationToken,
                    suggestion.DocumentTypeCode,
                    notes: $"Sugerido do e-Fatura: {suggestion.SupplierTaxId} {suggestion.SupplierName}, {suggestion.InvoiceType} {suggestion.InvoiceNo}.",
                    source: EntrySource.EFatura,
                    documentDate: suggestion.InvoiceDate).ConfigureAwait(false);

                context.ChangeTracker.Clear();
                var tracked = await context.EFaturaPurchases
                    .SingleAsync(item => item.Id == purchase.Id, cancellationToken)
                    .ConfigureAwait(false);
                tracked.LinkToEntry(posted.Reference);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                outcomes.Add(new SuggestedEntryOutcome(purchase.Id, posted.Reference, null));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                context.ChangeTracker.Clear();
                outcomes.Add(new SuggestedEntryOutcome(purchase.Id, null, exception.Message));
            }
        }

        return outcomes;
    }

    /// <summary>Separa as faturas por lançar das já lançadas e das anuladas no Portal.</summary>
    private async Task<(List<EFaturaPurchase> Pending, int Booked, int Cancelled)> SplitAsync(
        Guid companyId, List<EFaturaPurchase> purchases, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var linkedReferences = purchases
            .Where(item => item.JournalEntryReference is not null)
            .Select(item => item.JournalEntryReference!)
            .Distinct()
            .ToList();
        var liveReferences = (await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                linkedReferences.Contains(entry.Reference) &&
                entry.Status != JournalEntryStatus.Voided)
            .Select(entry => entry.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        var entries = await CrossCheckService.LoadPurchaseEntriesAsync(
            context,
            companyId,
            from.AddDays(-CrossCheckBuilder.DateToleranceDays),
            to.AddDays(CrossCheckBuilder.DateToleranceDays),
            cancellationToken).ConfigureAwait(false);
        var matched = CrossCheckBuilder.BookedInvoiceIds(
            purchases
                .Select(item => new CrossCheckInvoiceInput(
                    item.Id, item.SupplierTaxId, item.SupplierName, item.InvoiceNo, item.Atcud,
                    item.InvoiceDate, item.TaxBase, item.TaxAmount, item.GrossTotal))
                .ToList(),
            entries);

        var pending = new List<EFaturaPurchase>();
        int booked = 0, cancelled = 0;
        foreach (var purchase in purchases)
        {
            if (purchase.IsCancelled)
            {
                cancelled++;
            }
            else if ((purchase.JournalEntryReference is { } reference && liveReferences.Contains(reference)) ||
                matched.Contains(purchase.Id))
            {
                booked++;
            }
            else
            {
                pending.Add(purchase);
            }
        }

        return (pending, booked, cancelled);
    }

    private async Task<List<SuggestedEntry>> SuggestAsync(
        Guid companyId, List<EFaturaPurchase> purchases, CancellationToken cancellationToken)
    {
        if (purchases.Count == 0)
        {
            return [];
        }

        var taxIds = purchases.Select(item => item.SupplierTaxId).Distinct().ToList();
        var rules = await context.SupplierEntryRules
            .AsNoTracking()
            .Where(rule => rule.CompanyId == companyId && rule.IsActive && taxIds.Contains(rule.SupplierTaxId))
            .ToDictionaryAsync(rule => rule.SupplierTaxId, cancellationToken)
            .ConfigureAwait(false);
        var templateIds = rules.Values.Select(rule => rule.EntryTemplateId).Distinct().ToList();
        var templates = await context.EntryTemplates
            .AsNoTracking()
            .Include(item => item.Lines)
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken)
            .ConfigureAwait(false);
        var parties = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && party.CountryCode == "PT" && taxIds.Contains(party.TaxRegistrationNumber))
            .Select(party => new { party.TaxRegistrationNumber, party.SupplierAccountId, party.IsActive })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var partiesByTaxId = parties
            .GroupBy(party => party.TaxRegistrationNumber)
            .ToDictionary(group => group.Key, group => group.First());

        var journalCodes = await context.Journals.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken).ConfigureAwait(false);
        var documentTypeCodes = await context.AccountingDocumentTypes.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Id, item.Code, item.Name })
            .ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        var vatCodes = await context.VatCodes.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new { item.Id, item.Code, item.Percentage })
            .ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        var percentages = vatCodes.Values.ToDictionary(item => item.Id, item => item.Percentage);

        var items = new List<SuggestedEntry>();
        foreach (var purchase in purchases)
        {
            var fallbackDescription = $"{(purchase.IsCreditNote ? "Nota de crédito" : "Fatura")} {purchase.InvoiceNo} — {purchase.SupplierName}".Trim();
            SuggestedEntry Without(string problem, string? templateCode = null) => new(
                purchase.Id, purchase.SupplierTaxId, purchase.SupplierName, purchase.InvoiceNo, purchase.InvoiceDate,
                purchase.InvoiceType, purchase.IsCreditNote, purchase.TaxBase, purchase.TaxAmount, purchase.GrossTotal,
                templateCode, null, null, fallbackDescription, [], problem, null);

            if (!rules.TryGetValue(purchase.SupplierTaxId, out var rule))
            {
                items.Add(Without("O fornecedor não tem regra de lançamento."));
                continue;
            }

            var template = templates[rule.EntryTemplateId];
            if (!template.IsActive)
            {
                items.Add(Without($"O lançamento-tipo {template.Code} está inativo.", template.Code));
                continue;
            }

            IReadOnlyList<EntryTemplateAmount> amounts;
            try
            {
                amounts = template.ComputeForDocument(purchase.TaxBase, purchase.TaxAmount, purchase.IsCreditNote, percentages);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                items.Add(Without(exception.Message, template.Code));
                continue;
            }

            // O fornecedor vai na contrapartida; se tiver conta própria (221…), usa-se essa.
            var party = partiesByTaxId.GetValueOrDefault(purchase.SupplierTaxId);
            string? warning = party is null
                ? "O fornecedor não tem ficha de terceiro: a contrapartida fica sem terceiro."
                : !party.IsActive ? "A ficha de terceiro do fornecedor está inativa." : null;
            var partyTaxId = party is { IsActive: true } ? purchase.SupplierTaxId : null;
            var description = rule.Description ?? template.Description ?? fallbackDescription;

            var lines = amounts
                .Select(amount =>
                {
                    var isBalance = amount.Line.Kind == EntryTemplateLineKind.Balance;
                    var accountId = isBalance && party is { IsActive: true, SupplierAccountId: { } own }
                        ? own
                        : amount.Line.AccountId;
                    var account = accounts[accountId];
                    return new SuggestedEntryLine(
                        account.Code,
                        account.Name,
                        amount.Line.Description ?? description,
                        amount.Debit,
                        amount.Credit,
                        amount.Line.VatCodeId is { } vatCodeId ? vatCodes[vatCodeId].Code : null,
                        isBalance ? partyTaxId : null);
                })
                .ToList();

            items.Add(new SuggestedEntry(
                purchase.Id, purchase.SupplierTaxId, purchase.SupplierName, purchase.InvoiceNo, purchase.InvoiceDate,
                purchase.InvoiceType, purchase.IsCreditNote, purchase.TaxBase, purchase.TaxAmount, purchase.GrossTotal,
                template.Code,
                journalCodes[template.JournalId],
                template.DocumentTypeId is { } documentTypeId ? documentTypeCodes[documentTypeId] : null,
                description,
                lines,
                null,
                warning));
        }

        return items;
    }

    /// <summary>O nome de cada fornecedor: o da ficha de terceiro ou, sem ficha, o da fatura mais recente.</summary>
    private async Task<Dictionary<string, string>> SupplierNamesAsync(
        Guid companyId, IReadOnlyCollection<string> taxIds, CancellationToken cancellationToken)
    {
        var names = (await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && taxIds.Contains(party.TaxRegistrationNumber))
            .Select(party => new { party.TaxRegistrationNumber, party.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(party => party.TaxRegistrationNumber)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        var missing = taxIds.Where(taxId => !names.ContainsKey(taxId)).ToList();
        if (missing.Count > 0)
        {
            var fromInvoices = await context.EFaturaPurchases
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && missing.Contains(item.SupplierTaxId))
                .OrderByDescending(item => item.InvoiceDate)
                .Select(item => new { item.SupplierTaxId, item.SupplierName })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in fromInvoices)
            {
                names.TryAdd(row.SupplierTaxId, row.SupplierName);
            }
        }

        return names;
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
