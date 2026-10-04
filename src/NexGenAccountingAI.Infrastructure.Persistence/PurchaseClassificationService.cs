using System.Globalization;
using NexGenAccountingAI.Application.Classification;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma conta (ou código de IVA) proposta, com quantas vezes apareceu nas compras analisadas.</summary>
public sealed record ClassificationChoice(string Code, string Name, int Count);

/// <summary>
/// A proposta de classificação das compras de um fornecedor, tirada do que já
/// foi lançado com ele: a conta de gasto (ou de inventário, ou de
/// investimento), o código de IVA, a conta de IVA, a conta do fornecedor e o
/// diário. <see cref="Confidence"/> é a parte das compras em que a conta
/// proposta foi a usada.
/// </summary>
public sealed record SupplierClassificationProposal(
    string SupplierTaxId,
    string SupplierName,
    int EntryCount,
    ClassificationChoice Account,
    ClassificationChoice? VatCode,
    ClassificationChoice? VatAccount,
    ClassificationChoice? SupplierAccount,
    string JournalCode,
    decimal Confidence,
    string Explanation,
    string Source);

/// <summary>A proposta escolhida (e eventualmente corrigida) pelo utilizador, para aceitar.</summary>
public sealed record SupplierClassificationChoice(
    string AccountCode,
    string? VatCode,
    string? VatAccountCode,
    string SupplierAccountCode,
    string JournalCode);

/// <summary>
/// Classificação das compras pelo histórico do fornecedor (F3-21A). Lê os
/// lançamentos — não anulados, sem abertura nem apuramento — em que o
/// fornecedor aparece como terceiro numa linha ou que foram criados a partir de
/// uma fatura dele no e-Fatura; em cada um, a linha de gasto é a de maior valor
/// nas classes 3, 4 ou 6, o IVA é a linha em 2432 e o fornecedor a linha em 22.
/// A proposta é a escolha mais frequente; aceitá-la cria um lançamento-tipo e a
/// regra do fornecedor — a decisão é sempre do utilizador.
/// </summary>
public sealed class PurchaseClassificationService(AccountingDbContext context)
{
    public const string HistorySource = "historico";

    public const string AiSource = "ia";

    private const int MaxAccountOptions = 400;

    private const int MaxEntriesPerSupplier = 200;

    public async Task<IReadOnlyList<SupplierClassificationProposal>?> ProposeAsync(
        Guid userId, Guid companyId, IReadOnlyCollection<string> supplierTaxIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplierTaxIds);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var result = new List<SupplierClassificationProposal>();
        foreach (var taxId in supplierTaxIds.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct().Take(100))
        {
            if (await ProposeOneAsync(companyId, taxId, cancellationToken).ConfigureAwait(false) is { } proposal)
            {
                result.Add(proposal);
            }
        }

        return result;
    }

    /// <summary>
    /// Aceita a proposta: cria (ou substitui) o lançamento-tipo <c>H-&lt;NIF&gt;</c>
    /// com as contas propostas e liga-o ao fornecedor por uma regra.
    /// </summary>
    public async Task<SupplierEntryRuleRow?> AcceptAsync(
        Guid userId, Guid companyId, string supplierTaxId, SuggestedEntryService suggestions, EntryTemplateService templates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        ArgumentNullException.ThrowIfNull(templates);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var proposal = await ProposeOneAsync(companyId, supplierTaxId.Trim(), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Não há compras lançadas deste fornecedor de onde tirar uma proposta.");
        return await AcceptProposalAsync(userId, companyId, proposal, suggestions, templates, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cria o lançamento-tipo e a regra de uma proposta (do histórico ou validada da IA).</summary>
    public static async Task<SupplierEntryRuleRow?> AcceptProposalAsync(
        Guid userId, Guid companyId, SupplierClassificationProposal proposal, SuggestedEntryService suggestions, EntryTemplateService templates,
        CancellationToken cancellationToken)
    {
        if (proposal.SupplierAccount is null)
        {
            throw new InvalidOperationException("A proposta não tem conta de fornecedor: crie o lançamento-tipo à mão.");
        }

        var code = TemplateCode(proposal.SupplierTaxId);
        var name = $"Compras {proposal.SupplierName}".Trim();
        var lines = new List<EntryTemplateLineInput>
        {
            new(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, proposal.Account.Code, proposal.VatCode?.Code, null),
        };
        if (proposal.VatCode is not null && proposal.VatAccount is not null)
        {
            lines.Add(new EntryTemplateLineInput(EntryTemplateLineKind.Vat, EntryTemplateSide.Debit, proposal.VatAccount.Code, proposal.VatCode.Code, "IVA dedutível"));
        }

        lines.Add(new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, proposal.SupplierAccount.Code, null, null));
        await templates.SaveAsync(userId, companyId, code, new EntryTemplateInput(
            name.Length <= EntryTemplate.NameMaxLength ? name : name[..EntryTemplate.NameMaxLength],
            proposal.JournalCode,
            null,
            null,
            true,
            lines), cancellationToken).ConfigureAwait(false);
        return await suggestions.SaveRuleAsync(userId, companyId, proposal.SupplierTaxId, new SupplierEntryRuleInput(code, null, true), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Pede ao modelo a classificação de um fornecedor sem histórico (F3-21B). O
    /// dossiê leva só o fornecedor, o setor e algumas faturas do e-Fatura e as
    /// contas e os códigos de IVA da empresa; a resposta só é aceite se a conta e
    /// o código existirem nessas listas. As contas de IVA e do fornecedor e o
    /// diário vêm do uso da empresa. É sempre uma proposta para confirmar.
    /// </summary>
    public async Task<SupplierClassificationProposal?> ProposeWithAiAsync(
        Guid userId, Guid companyId, string supplierTaxId, IPurchaseClassifier classifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!classifier.IsConfigured)
        {
            throw new InvalidOperationException("A classificação com IA não está configurada: defina a chave da API (ANTHROPIC_API_KEY).");
        }

        var taxId = supplierTaxId.Trim();
        var invoices = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.SupplierTaxId == taxId)
            .OrderByDescending(item => item.InvoiceDate)
            .Take(10)
            .Select(item => new { item.SupplierName, item.Sector, item.InvoiceDate, item.InvoiceType, item.TaxBase, item.TaxAmount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var name = await SupplierNameAsync(companyId, taxId, cancellationToken).ConfigureAwait(false);
        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.AllowsPosting &&
                (account.Code.StartsWith("3") || account.Code.StartsWith("4") || account.Code.StartsWith("6")))
            .OrderBy(account => account.Code)
            .Take(MaxAccountOptions)
            .Select(account => new ClassificationAccountOption(account.Code, account.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            throw new InvalidOperationException("O plano de contas não tem contas de movimento nas classes 3, 4 ou 6.");
        }

        var vatCodes = await context.VatCodes
            .AsNoTracking()
            .Where(code => code.CompanyId == companyId && code.IsActive)
            .OrderBy(code => code.Code)
            .Select(code => new ClassificationVatOption(code.Code, code.Name, code.Percentage))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dossier = new PurchaseClassificationDossier(
            taxId,
            name,
            invoices.Select(item => item.Sector).FirstOrDefault(sector => !string.IsNullOrWhiteSpace(sector)),
            invoices.Select(item => new ClassificationInvoiceSample(item.InvoiceDate, item.InvoiceType, item.TaxBase, item.TaxAmount)).ToList(),
            accounts,
            vatCodes);
        var answer = await classifier.ClassifyAsync(dossier, cancellationToken).ConfigureAwait(false);

        var account = accounts.FirstOrDefault(item => string.Equals(item.Code, answer.AccountCode, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(answer.AccountCode)
                    ? $"A IA não encontrou uma conta adequada. {answer.Explanation}".Trim()
                    : $"A IA propôs a conta {answer.AccountCode}, que não é uma conta de movimento das classes 3, 4 ou 6 do plano.");
        }

        var vat = vatCodes.FirstOrDefault(item => string.Equals(item.Code, answer.VatCode, StringComparison.OrdinalIgnoreCase));
        var vatAccount = vat is null ? null : await MostUsedVatAccountAsync(companyId, vat.Code, cancellationToken).ConfigureAwait(false);
        var supplierAccount = await SupplierAccountAsync(companyId, taxId, cancellationToken).ConfigureAwait(false);
        var journal = await PurchaseJournalAsync(companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A empresa não tem diários.");

        var explanation = $"{answer.Explanation} (proposta da IA, {answer.Model} — confirme antes de aceitar)".Trim();
        return new SupplierClassificationProposal(
            taxId, name, 0, new ClassificationChoice(account.Code, account.Name, 0),
            vat is null ? null : new ClassificationChoice(vat.Code, vat.Name, 0),
            vatAccount, supplierAccount, journal, 0m, explanation, AiSource);
    }

    /// <summary>Aceita uma proposta com as contas que o utilizador confirmou (ou corrigiu).</summary>
    public async Task<SupplierEntryRuleRow?> AcceptChoiceAsync(
        Guid userId, Guid companyId, string supplierTaxId, SupplierClassificationChoice choice, SuggestedEntryService suggestions, EntryTemplateService templates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(choice.VatCode) && string.IsNullOrWhiteSpace(choice.VatAccountCode))
        {
            throw new ArgumentException("Com código de IVA, indique também a conta de IVA dedutível.", nameof(choice));
        }

        var taxId = supplierTaxId.Trim();
        var name = await SupplierNameAsync(companyId, taxId, cancellationToken).ConfigureAwait(false);
        var proposal = new SupplierClassificationProposal(
            taxId, name, 0,
            new ClassificationChoice(choice.AccountCode.Trim(), string.Empty, 0),
            string.IsNullOrWhiteSpace(choice.VatCode) ? null : new ClassificationChoice(choice.VatCode.Trim(), string.Empty, 0),
            string.IsNullOrWhiteSpace(choice.VatAccountCode) ? null : new ClassificationChoice(choice.VatAccountCode.Trim(), string.Empty, 0),
            new ClassificationChoice(choice.SupplierAccountCode.Trim(), string.Empty, 0),
            choice.JournalCode.Trim(), 0m, string.Empty, string.Empty);
        return await AcceptProposalAsync(userId, companyId, proposal, suggestions, templates, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClassificationChoice?> MostUsedVatAccountAsync(Guid companyId, string vatCode, CancellationToken cancellationToken)
    {
        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.Account.Code.StartsWith("2432") &&
                (line.VatCode != null && line.VatCode.Code == vatCode))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new { group.Key.Code, group.Key.Name, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows is not null)
        {
            return new ClassificationChoice(rows.Code, rows.Name, rows.Count);
        }

        var fallback = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.AllowsPosting && account.Code.StartsWith("2432"))
            .OrderBy(account => account.Code)
            .Select(account => new ClassificationChoice(account.Code, account.Name, 0))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return fallback;
    }

    private async Task<ClassificationChoice?> SupplierAccountAsync(Guid companyId, string taxId, CancellationToken cancellationToken)
    {
        var own = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && party.TaxRegistrationNumber == taxId && party.SupplierAccountId != null)
            .Join(context.Accounts, party => party.SupplierAccountId, account => account.Id, (party, account) => new ClassificationChoice(account.Code, account.Name, 0))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (own is not null)
        {
            return own;
        }

        var used = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.Account.Code.StartsWith("22") && line.Credit > 0 &&
                line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("2432")))
            .GroupBy(line => new { line.Account.Code, line.Account.Name })
            .Select(group => new { group.Key.Code, group.Key.Name, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (used is not null)
        {
            return new ClassificationChoice(used.Code, used.Name, used.Count);
        }

        return await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.AllowsPosting && account.Code.StartsWith("221"))
            .OrderBy(account => account.Code)
            .Select(account => new ClassificationChoice(account.Code, account.Name, 0))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> PurchaseJournalAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var used = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId && entry.Lines.Any(line => line.Account.Code.StartsWith("2432")))
            .GroupBy(entry => entry.Journal.Code)
            .Select(group => new { Code = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (used is not null)
        {
            return used.Code;
        }

        var journals = await context.Journals.AsNoTracking()
            .Where(journal => journal.CompanyId == companyId)
            .OrderBy(journal => journal.Code)
            .Select(journal => journal.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return journals.FirstOrDefault(code => code.Contains("COMPRA", StringComparison.OrdinalIgnoreCase)) ?? journals.FirstOrDefault();
    }

    public static string TemplateCode(string supplierTaxId)
    {
        var digits = new string(supplierTaxId.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        var code = "H-" + digits;
        return code.Length <= EntryTemplate.CodeMaxLength ? code : code[..EntryTemplate.CodeMaxLength];
    }

    private async Task<SupplierClassificationProposal?> ProposeOneAsync(Guid companyId, string taxId, CancellationToken cancellationToken)
    {
        var linked = await context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.SupplierTaxId == taxId && item.JournalEntryReference != null)
            .Select(item => item.JournalEntryReference!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var entryIds = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.Source != EntrySource.Opening &&
                entry.Source != EntrySource.Closing &&
                (linked.Contains(entry.Reference) ||
                    entry.Lines.Any(line => line.Party != null && line.Party.CountryCode == "PT" && line.Party.TaxRegistrationNumber == taxId)))
            .OrderByDescending(entry => entry.EntryDate)
            .Select(entry => entry.Id)
            .Take(MaxEntriesPerSupplier)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (entryIds.Count == 0)
        {
            return null;
        }

        var lines = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => entryIds.Contains(line.JournalEntryId))
            .Select(line => new
            {
                line.JournalEntryId,
                JournalCode = line.JournalEntry.Journal.Code,
                line.JournalEntry.EntryDate,
                AccountCode = line.Account.Code,
                AccountName = line.Account.Name,
                Amount = line.Debit + line.Credit,
                VatCode = line.VatCode != null ? line.VatCode.Code : null,
                VatName = line.VatCode != null ? line.VatCode.Name : null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accounts = new List<(string Code, string Name)>();
        var vatCodes = new List<(string Code, string Name)>();
        var vatAccounts = new List<(string Code, string Name)>();
        var supplierAccounts = new List<(string Code, string Name)>();
        var journals = new List<string>();
        foreach (var entry in lines.GroupBy(line => line.JournalEntryId))
        {
            var expense = entry
                .Where(line => line.AccountCode.StartsWith('3') || line.AccountCode.StartsWith('4') || line.AccountCode.StartsWith('6'))
                .OrderByDescending(line => line.Amount)
                .FirstOrDefault();
            if (expense is null)
            {
                continue;
            }

            accounts.Add((expense.AccountCode, expense.AccountName));
            journals.Add(expense.JournalCode);
            var vat = entry.Where(line => line.AccountCode.StartsWith("2432", StringComparison.Ordinal)).OrderByDescending(line => line.Amount).FirstOrDefault();
            if (vat is not null)
            {
                vatAccounts.Add((vat.AccountCode, vat.AccountName));
            }

            var vatCode = expense.VatCode is not null ? (expense.VatCode, expense.VatName!) : vat?.VatCode is not null ? (vat.VatCode, vat.VatName!) : default;
            if (vatCode != default)
            {
                vatCodes.Add(vatCode);
            }

            var supplier = entry.Where(line => line.AccountCode.StartsWith("22", StringComparison.Ordinal)).OrderByDescending(line => line.Amount).FirstOrDefault();
            if (supplier is not null)
            {
                supplierAccounts.Add((supplier.AccountCode, supplier.AccountName));
            }
        }

        if (accounts.Count == 0)
        {
            return null;
        }

        static ClassificationChoice? Top(List<(string Code, string Name)> items) => items.Count == 0
            ? null
            : items.GroupBy(item => item.Code)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ClassificationChoice(group.Key, group.First().Name, group.Count()))
                .First();

        var account = Top(accounts)!;
        var vatChoice = Top(vatCodes);
        var vatAccount = Top(vatAccounts);
        var supplierAccount = Top(supplierAccounts);
        var journal = journals.GroupBy(item => item).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key;
        var confidence = decimal.Round((decimal)account.Count / accounts.Count, 2);
        var name = await SupplierNameAsync(companyId, taxId, cancellationToken).ConfigureAwait(false);

        var culture = CultureInfo.GetCultureInfo("pt-PT");
        var explanation = string.Create(culture,
            $"Em {accounts.Count} {(accounts.Count == 1 ? "compra lançada" : "compras lançadas")} deste fornecedor, a conta {account.Code} ({account.Name}) foi usada em {account.Count}");
        if (vatChoice is not null)
        {
            explanation += string.Create(culture, $"; o IVA levou o código {vatChoice.Code} em {vatChoice.Count}");
        }

        explanation += ".";
        if (confidence < 0.6m)
        {
            explanation += " As compras deste fornecedor dividem-se por várias contas: confirme antes de aceitar.";
        }

        return new SupplierClassificationProposal(
            taxId, name, accounts.Count, account, vatChoice, vatAccount, supplierAccount, journal, confidence, explanation, HistorySource);
    }

    private async Task<string> SupplierNameAsync(Guid companyId, string taxId, CancellationToken cancellationToken) =>
        await context.Parties.AsNoTracking()
            .Where(party => party.CompanyId == companyId && party.TaxRegistrationNumber == taxId)
            .Select(party => party.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? await context.EFaturaPurchases.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.SupplierTaxId == taxId)
            .OrderByDescending(item => item.InvoiceDate)
            .Select(item => item.SupplierName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? taxId;

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
