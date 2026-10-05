using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Banking;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record BankEntryRuleRow(string Pattern, string EntryTemplateCode, string EntryTemplateName, bool IsActive);

/// <summary>Um movimento do extrato por conciliar a que uma regra se aplica, com o lançamento proposto.</summary>
public sealed record BankEntrySuggestion(
    Guid LineId,
    DateOnly BookingDate,
    string Description,
    decimal Amount,
    string Pattern,
    string EntryTemplateCode,
    string? JournalCode,
    string? DocumentTypeCode,
    string EntryDescription,
    IReadOnlyList<SuggestedEntryLine> Lines,
    string? Problem)
{
    public bool Ready => Problem is null && Lines.Count > 0;
}

/// <summary>
/// Lançar a partir do extrato (F3-19B). Um movimento por conciliar cujo
/// descritivo contém o texto de uma regra (a de texto mais comprido, se várias)
/// é proposto com o lançamento-tipo da regra aplicado ao valor do movimento: a
/// contrapartida passa a ser a conta do banco e, se o lançamento-tipo estiver
/// escrito para o sentido contrário (uma saída e o movimento é uma entrada),
/// os lados invertem-se. Finalizar cria o lançamento por conferir, com origem
/// <see cref="EntrySource.BankStatement"/>, e concilia-o com o movimento.
/// </summary>
public sealed class BankEntryService(AccountingDbContext context, JournalPostingService posting)
{
    public async Task<IReadOnlyList<BankEntryRuleRow>?> ListRulesAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await (from rule in context.BankEntryRules.AsNoTracking()
                      join template in context.EntryTemplates.AsNoTracking() on rule.EntryTemplateId equals template.Id
                      where rule.CompanyId == companyId
                      orderby rule.Pattern
                      select new BankEntryRuleRow(rule.Pattern, template.Code, template.Name, rule.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BankEntryRuleRow?> SaveRuleAsync(
        Guid userId, Guid companyId, string pattern, string entryTemplateCode, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalized = BankEntryRule.NormalizePattern(pattern);
        var code = (entryTemplateCode ?? string.Empty).Trim().ToUpperInvariant();
        var template = await context.EntryTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == code, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException($"O lançamento-tipo '{entryTemplateCode}' não existe.", nameof(entryTemplateCode));

        var rule = await context.BankEntryRules
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Pattern == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            rule = BankEntryRule.Create(companyId, normalized);
            context.BankEntryRules.Add(rule);
        }

        rule.Update(template.Id, isActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BankEntryRuleRow(rule.Pattern, template.Code, template.Name, rule.IsActive);
    }

    public async Task<bool?> DeleteRuleAsync(Guid userId, Guid companyId, string pattern, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalized = BankEntryRule.Normalize(pattern);
        var rule = await context.BankEntryRules
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Pattern == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        context.BankEntryRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Os movimentos por conciliar do período a que uma regra se aplica, com o lançamento proposto.</summary>
    public async Task<IReadOnlyList<BankEntrySuggestion>?> SuggestAsync(
        Guid userId, Guid companyId, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var account = await ResolveAccountAsync(companyId, accountCode, cancellationToken).ConfigureAwait(false);
        var lines = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.AccountId == account.Id &&
                line.BookingDate >= from && line.BookingDate <= to && line.ReconciliationId == null)
            .OrderBy(line => line.BookingDate)
            .ThenBy(line => line.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await BuildAsync(companyId, account, lines, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cria os lançamentos dos movimentos indicados, um a um, e concilia cada um
    /// com o seu movimento. Um movimento já conciliado, sem regra ou que falhe
    /// fica com o erro e não impede os outros.
    /// </summary>
    public async Task<IReadOnlyList<SuggestedEntryOutcome>?> PostAsync(
        Guid userId, Guid companyId, IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var ids = lineIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new ArgumentException("Indique pelo menos um movimento.", nameof(lineIds));
        }

        var lines = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && ids.Contains(line.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var outcomes = ids
            .Where(id => lines.All(line => line.Id != id))
            .Select(id => new SuggestedEntryOutcome(id, null, "O movimento não existe."))
            .ToList();

        foreach (var group in lines.GroupBy(line => line.AccountId))
        {
            var account = await context.Accounts
                .AsNoTracking()
                .Where(item => item.Id == group.Key)
                .Select(item => new AccountInfo(item.Id, item.Code, item.Name))
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
            var pending = group.Where(line => line.ReconciliationId is null).ToList();
            outcomes.AddRange(group
                .Where(line => line.ReconciliationId is not null)
                .Select(line => new SuggestedEntryOutcome(line.Id, null, "O movimento já está conciliado.")));
            var suggestions = (await BuildAsync(companyId, account, pending, cancellationToken).ConfigureAwait(false))
                .ToDictionary(item => item.LineId);

            foreach (var line in pending)
            {
                if (!suggestions.TryGetValue(line.Id, out var suggestion))
                {
                    outcomes.Add(new SuggestedEntryOutcome(line.Id, null, "Nenhuma regra se aplica ao descritivo do movimento."));
                    continue;
                }

                if (!suggestion.Ready)
                {
                    outcomes.Add(new SuggestedEntryOutcome(line.Id, null, suggestion.Problem ?? "A sugestão não tem linhas."));
                    continue;
                }

                try
                {
                    var documentReference = line.Reference ?? $"Extrato {line.BookingDate:yyyy-MM-dd}";
                    var posted = await posting.PostAsync(
                        companyId,
                        suggestion.JournalCode!,
                        line.BookingDate,
                        documentReference.Length <= 240 ? documentReference : documentReference[..240],
                        suggestion.EntryDescription,
                        reviewed: false,
                        suggestion.Lines
                            .Select(item => new JournalEntryLineInput(item.AccountCode, item.Description, item.Debit, item.Credit, VatCode: item.VatCode))
                            .ToList(),
                        cancellationToken,
                        suggestion.DocumentTypeCode,
                        notes: $"Lançado do extrato ({account.Code}): {line.Description}",
                        source: EntrySource.BankStatement,
                        documentDate: line.BookingDate).ConfigureAwait(false);

                    context.ChangeTracker.Clear();
                    var tracked = await context.BankStatementLines
                        .SingleAsync(item => item.Id == line.Id, cancellationToken)
                        .ConfigureAwait(false);
                    var reconciliation = new BankReconciliation(Guid.NewGuid(), companyId, tracked.AccountId, [posted.Reference], automatic: true);
                    context.BankReconciliations.Add(reconciliation);
                    tracked.AttachTo(reconciliation.Id);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    outcomes.Add(new SuggestedEntryOutcome(line.Id, posted.Reference, null));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    context.ChangeTracker.Clear();
                    outcomes.Add(new SuggestedEntryOutcome(line.Id, null, exception.Message));
                }
            }
        }

        return outcomes;
    }

    private async Task<List<BankEntrySuggestion>> BuildAsync(
        Guid companyId, AccountInfo bank, List<BankStatementLine> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var rules = await context.BankEntryRules
            .AsNoTracking()
            .Where(rule => rule.CompanyId == companyId && rule.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        rules = rules.OrderByDescending(rule => rule.Pattern.Length).ThenBy(rule => rule.Pattern, StringComparer.Ordinal).ToList();
        if (rules.Count == 0)
        {
            return [];
        }

        var templateIds = rules.Select(rule => rule.EntryTemplateId).Distinct().ToList();
        var templates = await context.EntryTemplates
            .AsNoTracking()
            .Include(item => item.Lines)
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken)
            .ConfigureAwait(false);
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

        var result = new List<BankEntrySuggestion>();
        foreach (var line in lines)
        {
            var rule = rules.FirstOrDefault(item => item.Matches(line.Description));
            if (rule is null)
            {
                continue;
            }

            var template = templates[rule.EntryTemplateId];
            var description = template.Description ?? line.Description;
            BankEntrySuggestion Problem(string problem) => new(
                line.Id, line.BookingDate, line.Description, line.Amount, rule.Pattern, template.Code,
                null, null, description, [], problem);

            if (!template.IsActive)
            {
                result.Add(Problem($"O lançamento-tipo {template.Code} está inativo."));
                continue;
            }

            var vatLines = template.Lines.Count(item => item.Kind == EntryTemplateLineKind.Vat);
            if (vatLines > 1)
            {
                result.Add(Problem($"O lançamento-tipo {template.Code} tem várias linhas de IVA: não serve para lançar do extrato."));
                continue;
            }

            IReadOnlyList<EntryTemplateAmount> amounts;
            try
            {
                // O valor do movimento é o total: com uma linha de IVA, a base obtém-se dele.
                amounts = template.Compute(Math.Abs(line.Amount), amountIncludesVat: vatLines == 1, percentages);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                result.Add(Problem(exception.Message));
                continue;
            }

            // A contrapartida é a conta do banco; o banco é debitado quando entra dinheiro.
            var balance = amounts.Single(item => item.Line.Kind == EntryTemplateLineKind.Balance);
            var reverse = (balance.Debit > 0m) != (line.Amount > 0m);
            var suggestionLines = amounts
                .Select(item =>
                {
                    var isBalance = item.Line.Kind == EntryTemplateLineKind.Balance;
                    var (code, name) = isBalance ? (bank.Code, bank.Name) : (accounts[item.Line.AccountId].Code, accounts[item.Line.AccountId].Name);
                    return new SuggestedEntryLine(
                        code,
                        name,
                        item.Line.Description ?? description,
                        reverse ? item.Credit : item.Debit,
                        reverse ? item.Debit : item.Credit,
                        item.Line.VatCodeId is { } vatCodeId ? vatCodes[vatCodeId].Code : null,
                        null);
                })
                .ToList();

            result.Add(new BankEntrySuggestion(
                line.Id, line.BookingDate, line.Description, line.Amount, rule.Pattern, template.Code,
                journalCodes[template.JournalId],
                template.DocumentTypeId is { } documentTypeId ? documentTypeCodes[documentTypeId] : null,
                description,
                suggestionLines,
                null));
        }

        return result;
    }

    private async Task<AccountInfo> ResolveAccountAsync(Guid companyId, string accountCode, CancellationToken cancellationToken)
    {
        var code = (accountCode ?? string.Empty).Trim();
        var account = await context.Accounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Code == code && item.AllowsPosting)
            .Select(item => new AccountInfo(item.Id, item.Code, item.Name))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null || !(code.StartsWith("11", StringComparison.Ordinal) || code.StartsWith("12", StringComparison.Ordinal) || code.StartsWith("13", StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A conta '{accountCode}' não é uma conta de depósitos (11, 12 ou 13) de movimento.", nameof(accountCode));
        }

        return account;
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);

    private sealed record AccountInfo(Guid Id, string Code, string Name);
}
