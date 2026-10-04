using System.Reflection;
using System.Text.RegularExpressions;
using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Saft;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Prepara e escreve o SAF-T (PT) de contabilidade de um exercício. Só lê.
/// <list type="bullet">
/// <item>Entram os lançamentos não anulados com data no exercício.</item>
/// <item>Os lançamentos de abertura não saem como movimentos: dão os saldos de abertura das contas.</item>
/// <item>Saem as contas de movimento com saldo ou movimento e as agregadoras de que dependem; as de um só dígito ficam de fora (o formato pede pelo menos dois caracteres).</item>
/// <item>Saem os terceiros indicados nas linhas exportadas.</item>
/// </list>
/// Os problemas que tornariam o ficheiro inválido são bloqueantes: com eles o
/// ficheiro não é gerado. Não é uma validação oficial — o ficheiro deve ser
/// conferido no validador da AT antes de ser entregue.
/// </summary>
public sealed partial class SaftExportService(AccountingDbContext context, ISaftAccountingWriter writer)
{
    private const string Unknown = "Desconhecido";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<SaftExportReport?> ReportAsync(
        Guid userId, Guid companyId, int fiscalYear, CancellationToken cancellationToken = default) =>
        (await BuildAsync(userId, companyId, fiscalYear, cancellationToken).ConfigureAwait(false))?.Report;

    /// <summary>Escreve o ficheiro em <paramref name="output"/> se não houver problemas bloqueantes.</summary>
    /// <returns><c>null</c> sem acesso à empresa; senão o relatório (ver <see cref="SaftExportReport.CanExport"/>).</returns>
    public async Task<SaftExportReport?> ExportAsync(
        Guid userId, Guid companyId, int fiscalYear, Stream output, CancellationToken cancellationToken = default)
    {
        var built = await BuildAsync(userId, companyId, fiscalYear, cancellationToken).ConfigureAwait(false);
        if (built is null)
        {
            return null;
        }

        if (built.Value.Report.CanExport)
        {
            await writer.WriteAsync(built.Value.Model, output, cancellationToken).ConfigureAwait(false);
        }

        return built.Value.Report;
    }

    private async Task<(SaftExportModel Model, SaftExportReport Report)?> BuildAsync(
        Guid userId, Guid companyId, int fiscalYear, CancellationToken cancellationToken)
    {
        var company = await context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == companyId &&
                    item.IsActive &&
                    item.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var issues = new List<SaftExportIssue>();
        var start = new DateOnly(fiscalYear, 1, 1);
        var end = new DateOnly(fiscalYear, 12, 31);

        if (!PortugueseTaxNumber.IsValid(company.TaxRegistrationNumber))
        {
            issues.Add(new(true, $"O NIF da empresa ({company.TaxRegistrationNumber}) não é um NIF português válido."));
        }

        if (company.AddressDetail is null || company.City is null || company.PostalCode is null)
        {
            issues.Add(new(false, "A morada da empresa está incompleta: o que falta sai no cabeçalho como «Desconhecido»."));
        }

        var rows = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= start &&
                line.JournalEntry.EntryDate <= end)
            .OrderBy(line => line.JournalEntry.EntryDate)
            .ThenBy(line => line.JournalEntry.Reference)
            .ThenBy(line => line.Sequence)
            .Select(line => new LineRow(
                line.JournalEntryId,
                line.JournalEntry.Reference,
                line.JournalEntry.EntryDate,
                line.JournalEntry.Description,
                line.JournalEntry.Source,
                line.JournalEntry.Journal.Code,
                line.JournalEntry.Journal.Name,
                EF.Property<DateTimeOffset>(line.JournalEntry, "CreatedAtUtc"),
                EF.Property<Guid?>(line.JournalEntry, "CreatedBy"),
                line.Sequence,
                line.Account.Code,
                line.Description,
                line.Debit,
                line.Credit,
                line.PartyId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var opening = rows.Where(row => row.Source == EntrySource.Opening).ToList();
        var movements = rows.Where(row => row.Source != EntrySource.Opening).ToList();

        if (opening.Count == 0 &&
            await context.JournalEntries
                .AnyAsync(
                    entry => entry.CompanyId == companyId && entry.Status != JournalEntryStatus.Voided && entry.EntryDate < start,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            issues.Add(new(false, $"Não há lançamento de abertura em {fiscalYear} e existem movimentos anteriores: os saldos de abertura saem a zero."));
        }

        var accounts = await BuildAccountsAsync(company, opening, movements, issues, cancellationToken).ConfigureAwait(false);
        var (journals, parties) = await BuildJournalsAsync(companyId, movements, issues, cancellationToken).ConfigureAwait(false);

        var model = new SaftExportModel(
            new SaftExportHeader(
                company.TaxRegistrationNumber,
                company.LegalName,
                company.AddressDetail ?? Unknown,
                company.City ?? Unknown,
                company.PostalCode ?? Unknown,
                "PT",
                fiscalYear,
                start,
                end,
                DateOnly.FromDateTime(DateTime.UtcNow),
                typeof(SaftExportService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0"),
            accounts.TaxonomyReference,
            accounts.Accounts,
            parties,
            journals);

        var report = new SaftExportReport(
            fiscalYear, model.Accounts.Count, model.Parties.Count, model.NumberOfEntries, model.TotalDebit, model.TotalCredit, issues);
        return (model, report);
    }

    private async Task<(string TaxonomyReference, IReadOnlyList<SaftExportAccount> Accounts)> BuildAccountsAsync(
        Company company, List<LineRow> opening, List<LineRow> movements, List<SaftExportIssue> issues,
        CancellationToken cancellationToken)
    {
        var chart = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.Id)
            .Select(account => new { account.Code, account.Name, account.ParentCode, account.AllowsPosting, account.TaxonomyReference, account.TaxonomyCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // O formato pede pelo menos dois caracteres no código da conta.
        var byCode = chart.Where(account => account.Code.Length >= 2).ToDictionary(account => account.Code, StringComparer.Ordinal);

        string? ParentOf(string code)
        {
            var declared = byCode[code].ParentCode;
            if (declared is not null && declared != code && byCode.ContainsKey(declared))
            {
                return declared;
            }

            for (var length = code.Length - 1; length >= 2; length--)
            {
                if (byCode.ContainsKey(code[..length]))
                {
                    return code[..length];
                }
            }

            return null;
        }

        var openingNet = opening.GroupBy(row => row.AccountCode).ToDictionary(group => group.Key, group => group.Sum(row => row.Debit - row.Credit));
        var movementNet = movements.GroupBy(row => row.AccountCode).ToDictionary(group => group.Key, group => group.Sum(row => row.Debit - row.Credit));
        var used = openingNet.Keys.Union(movementNet.Keys).Where(byCode.ContainsKey).OrderBy(code => code, StringComparer.Ordinal).ToList();

        var totals = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        decimal[] TotalsOf(string code) => totals.TryGetValue(code, out var value) ? value : totals[code] = new decimal[4];

        var withoutParent = 0;
        var withoutTaxonomy = 0;
        foreach (var code in used)
        {
            var open = openingNet.GetValueOrDefault(code);
            var close = open + movementNet.GetValueOrDefault(code);
            var values = new[] { Math.Max(open, 0m), Math.Max(-open, 0m), Math.Max(close, 0m), Math.Max(-close, 0m) };

            var own = TotalsOf(code);
            for (var index = 0; index < 4; index++)
            {
                own[index] += values[index];
            }

            // As agregadoras somam os saldos devedores e credores das suas contas, em separado.
            var seen = new HashSet<string>(StringComparer.Ordinal) { code };
            for (var parent = ParentOf(code); parent is not null && seen.Add(parent); parent = ParentOf(parent))
            {
                var aggregate = TotalsOf(parent);
                for (var index = 0; index < 4; index++)
                {
                    aggregate[index] += values[index];
                }
            }

            if (ParentOf(code) is null)
            {
                withoutParent++;
            }

            if (byCode[code].TaxonomyCode is null)
            {
                withoutTaxonomy++;
            }
        }

        if (withoutParent > 0)
        {
            issues.Add(new(true, "Há contas de movimento sem conta agregadora no plano: o formato exige que cada uma pertença a um grupo.", withoutParent));
        }

        if (withoutTaxonomy > 0)
        {
            issues.Add(new(true, "Há contas de movimento com saldo ou movimento sem código de taxonomia.", withoutTaxonomy));
        }

        var usedSet = used.ToHashSet(StringComparer.Ordinal);
        var exported = totals.Keys
            .OrderBy(code => code, StringComparer.Ordinal)
            .Select(code =>
            {
                var account = byCode[code];
                var parent = ParentOf(code);
                var isMovement = usedSet.Contains(code);
                var values = totals[code];
                return new SaftExportAccount(
                    code,
                    account.Name,
                    values[0], values[1], values[2], values[3],
                    isMovement ? "GM" : parent is null ? "GR" : "GA",
                    parent,
                    isMovement ? account.TaxonomyCode : null);
            })
            .ToList();

        var reference = used
            .Select(code => byCode[code].TaxonomyReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault()
            ?? company.AccountingStandard switch
            {
                AccountingStandard.SncMicroEntities => "M",
                AccountingStandard.IfrsEu => "N",
                _ => "S",
            };

        return (reference, exported);
    }

    private async Task<(IReadOnlyList<SaftExportJournal> Journals, IReadOnlyList<SaftExportParty> Parties)> BuildJournalsAsync(
        Guid companyId, List<LineRow> movements, List<SaftExportIssue> issues, CancellationToken cancellationToken)
    {
        var partyIds = movements.Where(row => row.PartyId is not null).Select(row => row.PartyId!.Value).Distinct().ToList();
        var parties = (await context.Parties
                .AsNoTracking()
                .Where(party => party.CompanyId == companyId)
                .Select(party => new
                {
                    party.Id,
                    party.CountryCode,
                    party.TaxRegistrationNumber,
                    party.Name,
                    party.IsCustomer,
                    party.IsSupplier,
                    party.AddressDetail,
                    party.City,
                    party.PostalCode,
                    CustomerAccount = context.Accounts.Where(account => account.Id == party.CustomerAccountId).Select(account => account.Code).FirstOrDefault(),
                    SupplierAccount = context.Accounts.Where(account => account.Id == party.SupplierAccountId).Select(account => account.Code).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(party => partyIds.Contains(party.Id))
            .ToDictionary(party => party.Id);

        var userIds = movements.Where(row => row.CreatedBy is not null).Select(row => row.CreatedBy!.Value).Distinct().ToList();
        var userNames = await context.UserAccounts
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        static string PartyKey(string country, string taxNumber) => country == Party.DefaultCountryCode ? taxNumber : country + taxNumber;

        var exportedParties = new Dictionary<(bool IsCustomer, Guid Id), SaftExportParty>();
        var transactionIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        var journals = new List<SaftExportJournal>();

        foreach (var journalGroup in movements.GroupBy(row => (row.JournalCode, row.JournalName)).OrderBy(group => group.Key.JournalCode, StringComparer.Ordinal))
        {
            var transactions = new List<SaftExportTransaction>();
            foreach (var entry in journalGroup.GroupBy(row => row.EntryId))
            {
                var head = entry.First();
                var (transactionId, archival) = TransactionIdOf(head);
                if (!transactionIds.Add(transactionId))
                {
                    duplicates++;
                }

                // O terceiro da transação é o da primeira linha que o indica; a conta
                // dessa linha diz se é o cliente (21) ou o fornecedor (22).
                string? customerId = null;
                string? supplierId = null;
                var partyLine = entry.FirstOrDefault(row => row.PartyId is not null && parties.ContainsKey(row.PartyId.Value));
                if (partyLine is not null)
                {
                    var party = parties[partyLine.PartyId!.Value];
                    var asSupplier = partyLine.AccountCode.StartsWith("22", StringComparison.Ordinal) ||
                        (!partyLine.AccountCode.StartsWith("21", StringComparison.Ordinal) && party.IsSupplier && !party.IsCustomer);
                    var key = PartyKey(party.CountryCode, party.TaxRegistrationNumber);
                    exportedParties.TryAdd(
                        (!asSupplier, party.Id),
                        new SaftExportParty(
                            !asSupplier, key, (asSupplier ? party.SupplierAccount : party.CustomerAccount) ?? Unknown,
                            party.TaxRegistrationNumber, party.Name, party.CountryCode,
                            party.AddressDetail, party.City, party.PostalCode));
                    if (asSupplier)
                    {
                        supplierId = key;
                    }
                    else
                    {
                        customerId = key;
                    }
                }

                var created = head.CreatedAtUtc.UtcDateTime;
                transactions.Add(new SaftExportTransaction(
                    transactionId,
                    head.EntryDate.Month,
                    head.EntryDate,
                    head.CreatedBy is { } userId && userNames.TryGetValue(userId, out var name) ? name : "Sistema",
                    head.EntryDescription,
                    archival,
                    head.Source == EntrySource.Closing ? "A" : "N",
                    DateOnly.FromDateTime(created),
                    customerId,
                    supplierId,
                    entry
                        .OrderBy(row => row.Sequence)
                        .Select(row => new SaftExportLine(
                            row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            row.AccountCode, created, row.LineDescription, row.Debit, row.Credit))
                        .ToList()));
            }

            journals.Add(new SaftExportJournal(NoSpaces(journalGroup.Key.JournalCode), journalGroup.Key.JournalName, transactions));
        }

        if (duplicates > 0)
        {
            issues.Add(new(true, "Há lançamentos cujo identificador de transação ficaria repetido no ficheiro.", duplicates));
        }

        return (journals, exportedParties.Values.OrderBy(party => party.PartyId, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// O <c>TransactionID</c> do formato — «data diário número», sem espaços em
    /// cada parte — e o número de arquivo. Uma referência que já tem essa forma
    /// (os lançamentos importados de um SAF-T) sai tal como está.
    /// </summary>
    private static (string TransactionId, string Archival) TransactionIdOf(LineRow head)
    {
        if (TransactionIdPattern().IsMatch(head.Reference))
        {
            return (head.Reference, head.Reference[(head.Reference.LastIndexOf(' ') + 1)..]);
        }

        var archival = NoSpaces(head.Reference);
        archival = archival.Length > 20 ? archival[^20..] : archival;
        var journal = NoSpaces(head.JournalCode);
        journal = journal.Length > 30 ? journal[..30] : journal;
        return ($"{head.EntryDate:yyyy-MM-dd} {journal} {archival}", archival);
    }

    private static string NoSpaces(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    [GeneratedRegex(@"^[1-9][0-9]{3}-[01][0-9]-[0-3][0-9] [^ ]{1,30} [^ ]{1,20}$")]
    private static partial Regex TransactionIdPattern();

    private sealed record LineRow(
        Guid EntryId,
        string Reference,
        DateOnly EntryDate,
        string EntryDescription,
        EntrySource Source,
        string JournalCode,
        string JournalName,
        DateTimeOffset CreatedAtUtc,
        Guid? CreatedBy,
        int Sequence,
        string AccountCode,
        string LineDescription,
        decimal Debit,
        decimal Credit,
        Guid? PartyId);
}
