using System.Globalization;
using NexGenAccountingAI.Application.CrossCheck;
using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Billing;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O resultado de importar (ou pré-visualizar) os documentos de venda de um SAF-T de faturação.</summary>
public sealed record SalesDocumentsImportResult(
    Guid? BatchId,
    int Read,
    int Imported,
    int AlreadyExisted,
    int Voided,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<string> Warnings);

public sealed record SalesEntryRuleRow(decimal TaxPercentage, string EntryTemplateCode, string EntryTemplateName, bool IsActive);

/// <summary>Um documento de venda ainda sem lançamento, com o lançamento proposto.</summary>
public sealed record SalesSuggestedEntry(
    Guid DocumentId,
    string InvoiceNo,
    string InvoiceType,
    DateOnly InvoiceDate,
    string CustomerTaxId,
    string CustomerName,
    bool IsCreditNote,
    decimal NetTotal,
    decimal TaxPayable,
    decimal GrossTotal,
    IReadOnlyList<decimal> Percentages,
    string? JournalCode,
    string? DocumentTypeCode,
    string Description,
    IReadOnlyList<SuggestedEntryLine> Lines,
    string? Problem,
    string? Warning)
{
    public bool Ready => Problem is null && Lines.Count > 0;
}

public sealed record SalesSuggestedEntriesReport(
    DateOnly From,
    DateOnly To,
    int DocumentCount,
    int BookedCount,
    int VoidedCount,
    IReadOnlyList<decimal> PercentagesWithoutRule,
    IReadOnlyList<SalesSuggestedEntry> Items)
{
    public int ReadyCount => Items.Count(item => item.Ready);
}

/// <summary>
/// Lançamentos sugeridos a partir das vendas de um SAF-T de faturação (F3-18B).
/// Importa os documentos (idempotente pelo número) e propõe o lançamento de cada
/// um somando, para cada taxa de IVA do documento, o lançamento-tipo da regra
/// dessa taxa; as linhas iguais (o cliente, sobretudo) juntam-se. Finalizar cria
/// o lançamento «por conferir» pelo <see cref="JournalPostingService"/>, com
/// origem <see cref="EntrySource.SaftBilling"/>. Um documento conta como lançado
/// quando tem um lançamento ligado que não está anulado ou quando há um
/// lançamento de vendas com o mesmo número de documento
/// (<see cref="CrossCheckBuilder.BookedInvoiceIds"/>).
/// </summary>
public sealed class SalesSuggestedEntryService(
    ISaftSalesDocumentsReader reader, AccountingDbContext context, JournalPostingService posting)
{
    public async Task<SalesDocumentsImportResult?> ImportAsync(
        Guid userId, Guid companyId, Stream content, string fileName, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var read = await reader.ReadAsync(content, cancellationToken).ConfigureAwait(false);
        var companyTaxId = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegistrationNumber)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (read.CompanyTaxId is { Length: > 0 } fileTaxId &&
            !string.Equals(Digits(fileTaxId), Digits(companyTaxId), StringComparison.Ordinal))
        {
            throw new ArgumentException($"O ficheiro é de outra empresa (NIF {fileTaxId}); a empresa ativa tem o NIF {companyTaxId}.", nameof(content));
        }

        var numbers = read.Documents.Select(document => document.InvoiceNo).Distinct(StringComparer.Ordinal).ToList();
        var existing = (await context.SalesDocuments
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && numbers.Contains(item.InvoiceNo))
            .Select(item => item.InvoiceNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        var batchId = Guid.NewGuid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var created = new List<SalesDocument>();
        var warnings = read.Warnings.ToList();
        foreach (var document in read.Documents)
        {
            if (existing.Contains(document.InvoiceNo) || !seen.Add(document.InvoiceNo))
            {
                continue;
            }

            try
            {
                created.Add(SalesDocument.Create(
                    companyId, batchId, document.InvoiceNo, document.Atcud, document.InvoiceType, document.InvoiceDate,
                    document.IsVoided, document.CustomerTaxId, document.CustomerName, document.CustomerCountry,
                    document.NetTotal, document.TaxPayable, document.GrossTotal,
                    document.Taxes.Select(tax => new SalesDocumentTax(tax.Percentage, tax.TaxBase, tax.TaxAmount)),
                    fileName));
            }
            catch (ArgumentException exception)
            {
                warnings.Add($"{document.InvoiceNo}: {exception.Message}");
            }
        }

        if (!dryRun && created.Count > 0)
        {
            context.SalesDocuments.AddRange(created);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SalesDocumentsImportResult(
            dryRun ? null : batchId,
            read.Documents.Count,
            created.Count,
            read.Documents.Count(document => existing.Contains(document.InvoiceNo)),
            read.Documents.Count(document => document.IsVoided),
            read.Documents.Count == 0 ? null : read.Documents.Min(document => document.InvoiceDate),
            read.Documents.Count == 0 ? null : read.Documents.Max(document => document.InvoiceDate),
            warnings);
    }

    public async Task<IReadOnlyList<SalesEntryRuleRow>?> ListRulesAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await context.SalesEntryRules
            .AsNoTracking()
            .Where(rule => rule.CompanyId == companyId)
            .OrderByDescending(rule => rule.TaxPercentage)
            .Select(rule => new SalesEntryRuleRow(rule.TaxPercentage, rule.EntryTemplate!.Code, rule.EntryTemplate.Name, rule.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SalesEntryRuleRow?> SaveRuleAsync(
        Guid userId, Guid companyId, decimal taxPercentage, string entryTemplateCode, bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var percentage = SalesEntryRule.NormalizePercentage(taxPercentage);
        var code = (entryTemplateCode ?? string.Empty).Trim().ToUpperInvariant();
        var template = await context.EntryTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == code, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException($"O lançamento-tipo '{entryTemplateCode}' não existe.", nameof(entryTemplateCode));

        var rule = await context.SalesEntryRules
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.TaxPercentage == percentage, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            rule = SalesEntryRule.Create(companyId, percentage);
            context.SalesEntryRules.Add(rule);
        }

        rule.Update(template.Id, isActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SalesEntryRuleRow(percentage, template.Code, template.Name, rule.IsActive);
    }

    public async Task<bool?> DeleteRuleAsync(
        Guid userId, Guid companyId, decimal taxPercentage, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var percentage = SalesEntryRule.NormalizePercentage(taxPercentage);
        var rule = await context.SalesEntryRules
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.TaxPercentage == percentage, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        context.SalesEntryRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<SalesSuggestedEntriesReport?> ListAsync(
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

        var documents = await context.SalesDocuments
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.InvoiceDate >= from && item.InvoiceDate <= to)
            .OrderBy(item => item.InvoiceDate)
            .ThenBy(item => item.InvoiceNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var (pending, booked, voided) = await SplitAsync(companyId, documents, from, to, cancellationToken).ConfigureAwait(false);
        var (items, withoutRule) = await SuggestAsync(companyId, pending, cancellationToken).ConfigureAwait(false);
        return new SalesSuggestedEntriesReport(from, to, documents.Count, booked, voided, withoutRule, items);
    }

    public async Task<IReadOnlyList<SuggestedEntryOutcome>?> FinalizeAsync(
        Guid userId, Guid companyId, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new ArgumentException("Indique pelo menos uma sugestão.", nameof(documentIds));
        }

        var documents = await context.SalesDocuments
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && ids.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var outcomes = ids
            .Where(id => documents.All(document => document.Id != id))
            .Select(id => new SuggestedEntryOutcome(id, null, "O documento não existe."))
            .ToList();
        if (documents.Count == 0)
        {
            return outcomes;
        }

        var (pending, _, _) = await SplitAsync(
            companyId, documents, documents.Min(item => item.InvoiceDate), documents.Max(item => item.InvoiceDate), cancellationToken)
            .ConfigureAwait(false);
        var suggestions = (await SuggestAsync(companyId, pending, cancellationToken).ConfigureAwait(false)).Items
            .ToDictionary(item => item.DocumentId);

        foreach (var document in documents)
        {
            if (!suggestions.TryGetValue(document.Id, out var suggestion))
            {
                outcomes.Add(new SuggestedEntryOutcome(document.Id, null, "O documento já tem lançamento ou está anulado."));
                continue;
            }

            if (!suggestion.Ready)
            {
                outcomes.Add(new SuggestedEntryOutcome(document.Id, null, suggestion.Problem ?? "A sugestão não tem linhas."));
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
                            line.PartyTaxNumber, line.PartyTaxNumber is null ? null : PartyCountry(document), line.VatCode))
                        .ToList(),
                    cancellationToken,
                    suggestion.DocumentTypeCode,
                    notes: $"Sugerido do SAF-T de faturação: {document.InvoiceNo}, {document.CustomerTaxId} {document.CustomerName}.",
                    source: EntrySource.SaftBilling,
                    documentDate: suggestion.InvoiceDate).ConfigureAwait(false);

                context.ChangeTracker.Clear();
                var tracked = await context.SalesDocuments
                    .SingleAsync(item => item.Id == document.Id, cancellationToken)
                    .ConfigureAwait(false);
                tracked.LinkToEntry(posted.Reference);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                outcomes.Add(new SuggestedEntryOutcome(document.Id, posted.Reference, null));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                context.ChangeTracker.Clear();
                outcomes.Add(new SuggestedEntryOutcome(document.Id, null, exception.Message));
            }
        }

        return outcomes;
    }

    private async Task<(List<SalesDocument> Pending, int Booked, int Voided)> SplitAsync(
        Guid companyId, List<SalesDocument> documents, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var linkedReferences = documents
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

        var entries = await LoadSalesEntriesAsync(
            companyId,
            from.AddDays(-CrossCheckBuilder.DateToleranceDays),
            to.AddDays(CrossCheckBuilder.DateToleranceDays),
            cancellationToken).ConfigureAwait(false);
        var live = documents.Where(item => !item.IsVoided).ToList();
        var matched = CrossCheckBuilder.BookedInvoiceIds(
            live
                .Select(item => new CrossCheckInvoiceInput(
                    item.Id, item.CustomerTaxId, item.CustomerName, item.InvoiceNo, item.Atcud,
                    item.InvoiceDate, item.NetTotal, item.TaxPayable, item.GrossTotal))
                .ToList(),
            entries);

        var pending = new List<SalesDocument>();
        var booked = 0;
        foreach (var document in live)
        {
            if ((document.JournalEntryReference is { } reference && liveReferences.Contains(reference)) || matched.Contains(document.Id))
            {
                booked++;
            }
            else
            {
                pending.Add(document);
            }
        }

        return (pending, booked, documents.Count - live.Count);
    }

    /// <summary>Os lançamentos de vendas (linhas em 71, 72 ou 2433) para a correspondência pelo número do documento.</summary>
    private async Task<List<CrossCheckPurchaseEntryInput>> LoadSalesEntriesAsync(
        Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.Source != EntrySource.Opening &&
                entry.Source != EntrySource.Closing &&
                entry.EntryDate >= from &&
                entry.EntryDate <= to &&
                entry.Lines.Any(line =>
                    line.Account.Code.StartsWith("71") || line.Account.Code.StartsWith("72") || line.Account.Code.StartsWith("2433")))
            .Select(entry => new { entry.Reference, entry.EntryDate, entry.DocumentReference, entry.Description })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new CrossCheckPurchaseEntryInput(row.Reference, row.EntryDate, row.DocumentReference, row.Description, 0m, 0m))
            .ToList();
    }

    private async Task<(List<SalesSuggestedEntry> Items, IReadOnlyList<decimal> WithoutRule)> SuggestAsync(
        Guid companyId, List<SalesDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return ([], []);
        }

        var rules = await context.SalesEntryRules
            .AsNoTracking()
            .Where(rule => rule.CompanyId == companyId && rule.IsActive)
            .ToDictionaryAsync(rule => rule.TaxPercentage, cancellationToken)
            .ConfigureAwait(false);
        var templateIds = rules.Values.Select(rule => rule.EntryTemplateId).Distinct().ToList();
        var templates = await context.EntryTemplates
            .AsNoTracking()
            .Include(item => item.Lines)
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken)
            .ConfigureAwait(false);
        var taxIds = documents.Where(item => !item.IsFinalConsumer).Select(item => item.CustomerTaxId).Distinct().ToList();
        var parties = (await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && party.IsActive && taxIds.Contains(party.TaxRegistrationNumber))
            .Select(party => new { party.CountryCode, party.TaxRegistrationNumber, party.CustomerAccountId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(party => (party.CountryCode, party.TaxRegistrationNumber))
            .ToDictionary(group => group.Key, group => group.First().CustomerAccountId);

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

        var withoutRule = new SortedSet<decimal>();
        var items = new List<SalesSuggestedEntry>();
        foreach (var document in documents)
        {
            var label = document.InvoiceType switch
            {
                "NC" => "Nota de crédito",
                "ND" => "Nota de débito",
                "FS" => "Fatura simplificada",
                "FR" => "Fatura-recibo",
                _ => "Fatura",
            };
            var description = $"{label} {document.InvoiceNo} — {(document.IsFinalConsumer ? "Consumidor final" : document.CustomerName)}".Trim();
            var documentPercentages = document.Taxes.Select(tax => tax.Percentage).ToList();
            SalesSuggestedEntry Without(string problem) => new(
                document.Id, document.InvoiceNo, document.InvoiceType, document.InvoiceDate, document.CustomerTaxId,
                document.CustomerName, document.IsCreditNote, document.NetTotal, document.TaxPayable, document.GrossTotal,
                documentPercentages, null, null, description, [], problem, null);

            var missing = documentPercentages.Where(percentage => !rules.ContainsKey(percentage)).ToList();
            if (document.Taxes.Count == 0)
            {
                items.Add(Without("O documento não tem valores."));
                continue;
            }

            if (missing.Count > 0)
            {
                foreach (var percentage in missing)
                {
                    withoutRule.Add(percentage);
                }

                items.Add(Without($"Sem regra de vendas para a taxa de {string.Join(" e ", missing.Select(Percent))}."));
                continue;
            }

            var inactive = document.Taxes
                .Select(tax => templates[rules[tax.Percentage].EntryTemplateId])
                .FirstOrDefault(template => !template.IsActive);
            if (inactive is not null)
            {
                items.Add(Without($"O lançamento-tipo {inactive.Code} está inativo."));
                continue;
            }

            var partyKey = (PartyCountry(document), document.CustomerTaxId);
            var hasParty = !document.IsFinalConsumer && parties.ContainsKey(partyKey);
            var ownAccount = hasParty ? parties[partyKey] : null;
            string? warning = document.IsFinalConsumer || hasParty
                ? null
                : "O cliente não tem ficha de terceiro: a contrapartida fica sem terceiro.";

            // Soma o lançamento-tipo de cada taxa e junta as linhas iguais (conta, código de IVA, terceiro).
            var merged = new List<(Guid AccountId, Guid? VatCodeId, bool IsBalance, string? Description, decimal Net)>();
            string? problem = null;
            EntryTemplate? first = null;
            foreach (var tax in document.Taxes.OrderByDescending(tax => tax.TaxBase))
            {
                var template = templates[rules[tax.Percentage].EntryTemplateId];
                first ??= template;
                IReadOnlyList<EntryTemplateAmount> amounts;
                try
                {
                    amounts = template.ComputeForDocument(tax.TaxBase, tax.TaxAmount, document.IsCreditNote, percentages);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    problem = $"{Percent(tax.Percentage)}: {exception.Message}";
                    break;
                }

                foreach (var amount in amounts)
                {
                    var isBalance = amount.Line.Kind == EntryTemplateLineKind.Balance;
                    var accountId = isBalance && ownAccount is { } own ? own : amount.Line.AccountId;
                    var index = merged.FindIndex(item => item.AccountId == accountId && item.VatCodeId == amount.Line.VatCodeId && item.IsBalance == isBalance);
                    var net = amount.Debit - amount.Credit;
                    if (index < 0)
                    {
                        merged.Add((accountId, amount.Line.VatCodeId, isBalance, amount.Line.Description, net));
                    }
                    else
                    {
                        merged[index] = merged[index] with { Net = merged[index].Net + net };
                    }
                }
            }

            if (problem is not null || first is null)
            {
                items.Add(Without(problem ?? "O documento não tem valores."));
                continue;
            }

            var lines = merged
                .Where(item => item.Net != 0m)
                .Select(item => new SuggestedEntryLine(
                    accounts[item.AccountId].Code,
                    accounts[item.AccountId].Name,
                    item.Description ?? description,
                    item.Net > 0 ? item.Net : 0m,
                    item.Net < 0 ? -item.Net : 0m,
                    item.VatCodeId is { } vatCodeId ? vatCodes[vatCodeId].Code : null,
                    item.IsBalance && hasParty ? document.CustomerTaxId : null))
                .ToList();

            items.Add(new SalesSuggestedEntry(
                document.Id, document.InvoiceNo, document.InvoiceType, document.InvoiceDate, document.CustomerTaxId,
                document.CustomerName, document.IsCreditNote, document.NetTotal, document.TaxPayable, document.GrossTotal,
                documentPercentages,
                journalCodes[first.JournalId],
                first.DocumentTypeId is { } documentTypeId ? documentTypeCodes[documentTypeId] : null,
                description,
                lines,
                null,
                warning));
        }

        return (items, withoutRule.ToList());
    }

    private static string PartyCountry(SalesDocument document) =>
        document.CustomerCountry is { Length: 2 } country ? country : "PT";

    private static string Percent(decimal percentage) =>
        $"{percentage.ToString("0.##", CultureInfo.GetCultureInfo("pt-PT"))} %";

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
