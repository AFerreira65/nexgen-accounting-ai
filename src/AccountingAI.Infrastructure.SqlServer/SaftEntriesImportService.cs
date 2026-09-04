using System.Globalization;
using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// Importa para uma empresa as transações do bloco <c>GeneralLedgerEntries</c> de
/// um SAF-T. Cria os diários em falta e usa o <c>TransactionID</c> do ficheiro como
/// referência do lançamento, pelo que reimportar o mesmo ficheiro não duplica.
/// Salta (e reporta, em pt-PT) a transação cuja referência já existe, que refira
/// uma conta inexistente/inativa/agregadora, que tenha menos de duas linhas, que
/// não equilibre, ou cuja data caia num exercício já apurado ou no período
/// bloqueado da empresa. Uma só transação de base de dados envolve toda a
/// importação. No fim, reconcilia: soma os débitos e créditos da empresa (sem
/// anulados) no período do ficheiro para confrontar com o
/// <c>TotalDebit</c>/<c>TotalCredit</c> declarados. <see cref="AnalyzeAsync"/> faz
/// a mesma classificação sem escrever nada.
/// </summary>
public sealed class SaftEntriesImportService(ISaftEntriesReader entriesReader, AccountingDbContext context)
{
    public async Task<SaftEntriesImportResult?> ImportAsync(
        Guid userId,
        Guid companyId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var read = await entriesReader.ReadTransactionsAsync(content, cancellationToken).ConfigureAwait(false);
        var transactions = read.Items;
        var (periodFrom, periodTo) = FilePeriod(transactions);

        if (transactions.Count == 0)
        {
            return new SaftEntriesImportResult(
                0, 0, [], [],
                read.DeclaredEntryCount, read.DeclaredTotalDebit, read.DeclaredTotalCredit,
                null, null, 0m, 0m);
        }

        var imported = 0;
        var journalsCreated = new List<string>();
        var skipped = new List<SaftEntrySkipped>();

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            imported = 0;
            journalsCreated.Clear();
            skipped.Clear();

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var journalsByCode = await context.Journals
                .Where(journal => journal.CompanyId == companyId)
                .ToDictionaryAsync(journal => journal.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
            var accountsByCode = await context.Accounts
                .Where(account => account.CompanyId == companyId)
                .ToDictionaryAsync(account => account.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
            var seenReferences = await SeenReferencesAsync(companyId, cancellationToken).ConfigureAwait(false);
            var lockDate = await LockDateAsync(companyId, cancellationToken).ConfigureAwait(false);
            var closedYears = await ClosedYearsAsync(companyId, cancellationToken).ConfigureAwait(false);

            foreach (var fileTransaction in transactions)
            {
                var check = ClassifyTransaction(
                    fileTransaction, companyId, seenReferences, accountsByCode, lockDate, closedYears);
                if (check.SkipReason is { } reason)
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, reason));
                    continue;
                }

                if (!journalsByCode.TryGetValue(check.JournalCode, out var journal))
                {
                    var name = string.IsNullOrWhiteSpace(fileTransaction.JournalDescription)
                        ? check.JournalCode
                        : fileTransaction.JournalDescription;
                    journal = new AccountingJournal(Guid.NewGuid(), companyId, check.JournalCode, name);
                    context.Journals.Add(journal);
                    journalsByCode[journal.Code] = journal;
                    journalsCreated.Add(journal.Code);
                }

                var reference = Normalize(fileTransaction.TransactionId);
                var description = FallbackText(fileTransaction.Description, fileTransaction.TransactionId);
                var documentReference = FallbackText(fileTransaction.DocumentReference, fileTransaction.TransactionId);

                var entry = new JournalEntry(
                    Guid.NewGuid(), companyId, journal.Id, reference, check.EntryDate, documentReference, description);
                entry.SetSource(EntrySource.SaftImport);
                foreach (var line in fileTransaction.Lines)
                {
                    entry.AddLine(
                        Guid.NewGuid(),
                        accountsByCode[Normalize(line.AccountCode)].Id,
                        FallbackText(line.Description, description),
                        line.Debit,
                        line.Credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.JournalEntries.Add(entry);
                imported++;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var (companyDebit, companyCredit) = await PeriodTotalsAsync(
            companyId, periodFrom, periodTo, cancellationToken).ConfigureAwait(false);

        return new SaftEntriesImportResult(
            imported, skipped.Count, journalsCreated, skipped,
            read.DeclaredEntryCount, read.DeclaredTotalDebit, read.DeclaredTotalCredit,
            periodFrom, periodTo, companyDebit, companyCredit);
    }

    /// <summary>
    /// Corre a mesma classificação da importação sem escrever nada: diz quantas
    /// transações entrariam, quantas seriam saltadas (com a razão), que diários
    /// seriam criados, e a reconciliação prevista com os totais declarados.
    /// </summary>
    public async Task<SaftEntriesAnalysis?> AnalyzeAsync(
        Guid userId,
        Guid companyId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var read = await entriesReader.ReadTransactionsAsync(content, cancellationToken).ConfigureAwait(false);
        var transactions = read.Items;
        var (periodFrom, periodTo) = FilePeriod(transactions);

        if (transactions.Count == 0)
        {
            return new SaftEntriesAnalysis(
                0, 0, [], [],
                read.DeclaredEntryCount, read.DeclaredTotalDebit, read.DeclaredTotalCredit,
                null, null, 0m, 0m);
        }

        var knownJournalCodes = (await context.Journals
            .AsNoTracking()
            .Where(journal => journal.CompanyId == companyId)
            .Select(journal => journal.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accountsByCode = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .ToDictionaryAsync(account => account.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
        var seenReferences = await SeenReferencesAsync(companyId, cancellationToken).ConfigureAwait(false);
        var lockDate = await LockDateAsync(companyId, cancellationToken).ConfigureAwait(false);
        var closedYears = await ClosedYearsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var wouldImport = 0;
        var wouldDebit = 0m;
        var wouldCredit = 0m;
        var journalsToCreate = new List<string>();
        var skipped = new List<SaftEntrySkipped>();

        foreach (var fileTransaction in transactions)
        {
            var check = ClassifyTransaction(
                fileTransaction, companyId, seenReferences, accountsByCode, lockDate, closedYears);
            if (check.SkipReason is { } reason)
            {
                skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, reason));
                continue;
            }

            wouldImport++;
            wouldDebit += check.Debit;
            wouldCredit += check.Credit;
            if (knownJournalCodes.Add(check.JournalCode))
            {
                journalsToCreate.Add(check.JournalCode);
            }
        }

        var (currentDebit, currentCredit) = await PeriodTotalsAsync(
            companyId, periodFrom, periodTo, cancellationToken).ConfigureAwait(false);

        return new SaftEntriesAnalysis(
            wouldImport, skipped.Count, journalsToCreate, skipped,
            read.DeclaredEntryCount, read.DeclaredTotalDebit, read.DeclaredTotalCredit,
            periodFrom, periodTo,
            currentDebit + wouldDebit, currentCredit + wouldCredit);
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId &&
                    item.CompanyId == companyId &&
                    item.IsActive &&
                    item.Company.IsActive,
                cancellationToken);

    private async Task<HashSet<string>> SeenReferencesAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId)
            .Select(entry => entry.Reference)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private Task<DateOnly?> LockDateAsync(Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.PostingLockDate)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<HashSet<int>> ClosedYearsAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.DocumentType != null &&
                entry.DocumentType.Code == "AP")
            .Select(entry => entry.EntryDate.Year)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .ToHashSet();

    private async Task<(decimal Debit, decimal Credit)> PeriodTotalsAsync(
        Guid companyId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (from is not { } start || to is not { } end)
        {
            return (0m, 0m);
        }

        var rows = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId &&
                entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate >= start &&
                entry.EntryDate <= end)
            .Select(entry => new
            {
                Debit = entry.Lines.Sum(line => line.Debit),
                Credit = entry.Lines.Sum(line => line.Credit)
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return (rows.Sum(row => row.Debit), rows.Sum(row => row.Credit));
    }

    private static (DateOnly? From, DateOnly? To) FilePeriod(IReadOnlyList<SaftTransaction> transactions)
    {
        var dates = transactions
            .Where(item => item.Date is not null)
            .Select(item => item.Date!.Value)
            .ToList();
        return dates.Count > 0 ? (dates.Min(), dates.Max()) : (null, null);
    }

    /// <summary>
    /// Aplica a uma transação do ficheiro exatamente as mesmas regras que a
    /// importação usaria — mutando <paramref name="seenReferences"/> para apanhar
    /// duplicados dentro do próprio ficheiro. Devolve a razão do salto (em pt-PT)
    /// ou, quando entraria, os totais e a data já validados.
    /// </summary>
    private static TransactionCheck ClassifyTransaction(
        SaftTransaction fileTransaction,
        Guid companyId,
        ISet<string> seenReferences,
        IReadOnlyDictionary<string, LedgerAccount> accountsByCode,
        DateOnly? lockDate,
        IReadOnlySet<int> closedYears)
    {
        var reference = Normalize(fileTransaction.TransactionId);
        if (reference.Length == 0)
        {
            return TransactionCheck.Skip("sem identificador de transação.");
        }

        if (!seenReferences.Add(reference))
        {
            return TransactionCheck.Skip("já existe um lançamento com esta referência.");
        }

        if (fileTransaction.Date is not { } entryDate)
        {
            return TransactionCheck.Skip("sem data de transação.");
        }

        if (lockDate is { } limit && entryDate <= limit)
        {
            return TransactionCheck.Skip(
                $"a data está no período bloqueado para lançamentos (até {limit:yyyy-MM-dd}).");
        }

        if (closedYears.Contains(entryDate.Year))
        {
            return TransactionCheck.Skip($"o exercício de {entryDate.Year} está apurado.");
        }

        var journalCode = Normalize(fileTransaction.JournalCode);
        if (journalCode.Length == 0)
        {
            return TransactionCheck.Skip("sem diário.");
        }

        if (fileTransaction.Lines.Count < 2)
        {
            return TransactionCheck.Skip("tem menos de duas linhas.");
        }

        if (fileTransaction.Lines.Any(line =>
                line.Debit < 0 || line.Credit < 0 || (line.Debit == 0) == (line.Credit == 0)))
        {
            return TransactionCheck.Skip("tem uma linha sem um único débito ou crédito positivo.");
        }

        var totalDebit = fileTransaction.Lines.Sum(line => decimal.Round(line.Debit, 2, MidpointRounding.AwayFromZero));
        var totalCredit = fileTransaction.Lines.Sum(line => decimal.Round(line.Credit, 2, MidpointRounding.AwayFromZero));
        if (totalDebit != totalCredit)
        {
            return TransactionCheck.Skip(
                $"o débito ({totalDebit.ToString("0.00", CultureInfo.InvariantCulture)}) e o crédito " +
                $"({totalCredit.ToString("0.00", CultureInfo.InvariantCulture)}) não coincidem.");
        }

        var accountProblem = fileTransaction.Lines
            .Select(line => Normalize(line.AccountCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                if (!accountsByCode.TryGetValue(code, out var account))
                {
                    return $"a conta {code} não existe na empresa.";
                }

                if (!account.IsActive)
                {
                    return $"a conta {code} está inativa.";
                }

                return account.AllowsPosting ? null : $"a conta {code} é agregadora e não aceita movimentos.";
            })
            .FirstOrDefault(message => message is not null);
        if (accountProblem is not null)
        {
            return TransactionCheck.Skip(accountProblem);
        }

        var domainProblem = TrialBuild(fileTransaction, companyId, reference, entryDate);
        return domainProblem is not null
            ? TransactionCheck.Skip(domainProblem)
            : new TransactionCheck(null, totalDebit, totalCredit, entryDate, journalCode);
    }

    /// <summary>
    /// Constrói o lançamento pelo domínio (sem persistir) para apanhar qualquer
    /// regra que os testes explícitos não cubram; devolve a mensagem em minúscula
    /// ou <c>null</c> se passar.
    /// </summary>
    private static string? TrialBuild(SaftTransaction fileTransaction, Guid companyId, string reference, DateOnly entryDate)
    {
        try
        {
            var description = FallbackText(fileTransaction.Description, fileTransaction.TransactionId);
            var documentReference = FallbackText(fileTransaction.DocumentReference, fileTransaction.TransactionId);
            var entry = new JournalEntry(
                Guid.NewGuid(), companyId, Guid.NewGuid(), reference, entryDate, documentReference, description);
            foreach (var line in fileTransaction.Lines)
            {
                entry.AddLine(
                    Guid.NewGuid(), Guid.NewGuid(), FallbackText(line.Description, description), line.Debit, line.Credit);
            }

            entry.Complete(JournalEntryStatus.Reviewed);
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Lower(exception.Message);
        }
    }

    private static string FallbackText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string Lower(string message) =>
        message.Length == 0 ? message : char.ToLowerInvariant(message[0]) + message[1..];

    private readonly record struct TransactionCheck(
        string? SkipReason,
        decimal Debit,
        decimal Credit,
        DateOnly EntryDate,
        string JournalCode)
    {
        public static TransactionCheck Skip(string reason) => new(reason, 0m, 0m, default, string.Empty);
    }
}
