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
/// <c>TotalDebit</c>/<c>TotalCredit</c> declarados.
/// </summary>
public sealed class SaftEntriesImportService(ISaftEntriesReader entriesReader, AccountingDbContext context)
{
    public async Task<SaftEntriesImportResult?> ImportAsync(
        Guid userId,
        Guid companyId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId &&
                    item.CompanyId == companyId &&
                    item.IsActive &&
                    item.Company.IsActive,
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var read = await entriesReader.ReadTransactionsAsync(content, cancellationToken).ConfigureAwait(false);
        var transactions = read.Items;

        var fileDates = transactions
            .Where(item => item.Date is not null)
            .Select(item => item.Date!.Value)
            .ToList();
        DateOnly? periodFrom = fileDates.Count > 0 ? fileDates.Min() : null;
        DateOnly? periodTo = fileDates.Count > 0 ? fileDates.Max() : null;

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
            var seenReferences = (await context.JournalEntries
                .AsNoTracking()
                .Where(entry => entry.CompanyId == companyId)
                .Select(entry => entry.Reference)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lockDate = await context.Companies
                .AsNoTracking()
                .Where(company => company.Id == companyId)
                .Select(company => company.PostingLockDate)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var closedYears = (await context.JournalEntries
                .AsNoTracking()
                .Where(entry => entry.CompanyId == companyId &&
                    entry.Status != JournalEntryStatus.Voided &&
                    entry.DocumentType != null &&
                    entry.DocumentType.Code == "AP")
                .Select(entry => entry.EntryDate.Year)
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();

            foreach (var fileTransaction in transactions)
            {
                var reference = Normalize(fileTransaction.TransactionId);
                if (reference.Length == 0)
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, "sem identificador de transação."));
                    continue;
                }

                if (!seenReferences.Add(reference))
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, "já existe um lançamento com esta referência."));
                    continue;
                }

                if (fileTransaction.Date is not { } entryDate)
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, "sem data de transação."));
                    continue;
                }

                if (lockDate is { } limit && entryDate <= limit)
                {
                    skipped.Add(new SaftEntrySkipped(
                        fileTransaction.TransactionId,
                        $"a data está no período bloqueado para lançamentos (até {limit:yyyy-MM-dd})."));
                    continue;
                }

                if (closedYears.Contains(entryDate.Year))
                {
                    skipped.Add(new SaftEntrySkipped(
                        fileTransaction.TransactionId, $"o exercício de {entryDate.Year} está apurado."));
                    continue;
                }

                var journalCode = Normalize(fileTransaction.JournalCode);
                if (journalCode.Length == 0)
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, "sem diário."));
                    continue;
                }

                if (fileTransaction.Lines.Count < 2)
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, "tem menos de duas linhas."));
                    continue;
                }

                if (fileTransaction.Lines.Any(line =>
                        line.Debit < 0 || line.Credit < 0 || (line.Debit == 0) == (line.Credit == 0)))
                {
                    skipped.Add(new SaftEntrySkipped(
                        fileTransaction.TransactionId, "tem uma linha sem um único débito ou crédito positivo."));
                    continue;
                }

                var totalDebit = fileTransaction.Lines.Sum(line => decimal.Round(line.Debit, 2, MidpointRounding.AwayFromZero));
                var totalCredit = fileTransaction.Lines.Sum(line => decimal.Round(line.Credit, 2, MidpointRounding.AwayFromZero));
                if (totalDebit != totalCredit)
                {
                    skipped.Add(new SaftEntrySkipped(
                        fileTransaction.TransactionId,
                        $"o débito ({totalDebit.ToString("0.00", CultureInfo.InvariantCulture)}) e o crédito " +
                        $"({totalCredit.ToString("0.00", CultureInfo.InvariantCulture)}) não coincidem."));
                    continue;
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
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, accountProblem));
                    continue;
                }

                if (!journalsByCode.TryGetValue(journalCode, out var journal))
                {
                    var name = string.IsNullOrWhiteSpace(fileTransaction.JournalDescription)
                        ? journalCode
                        : fileTransaction.JournalDescription;
                    journal = new AccountingJournal(Guid.NewGuid(), companyId, journalCode, name);
                    context.Journals.Add(journal);
                    journalsByCode[journal.Code] = journal;
                    journalsCreated.Add(journal.Code);
                }

                var description = string.IsNullOrWhiteSpace(fileTransaction.Description)
                    ? fileTransaction.TransactionId
                    : fileTransaction.Description;
                var documentReference = string.IsNullOrWhiteSpace(fileTransaction.DocumentReference)
                    ? fileTransaction.TransactionId
                    : fileTransaction.DocumentReference;

                JournalEntry entry;
                try
                {
                    entry = new JournalEntry(
                        Guid.NewGuid(), companyId, journal.Id, reference, entryDate, documentReference, description);
                    foreach (var line in fileTransaction.Lines)
                    {
                        var lineDescription = string.IsNullOrWhiteSpace(line.Description) ? description : line.Description;
                        entry.AddLine(
                            Guid.NewGuid(),
                            accountsByCode[Normalize(line.AccountCode)].Id,
                            lineDescription,
                            line.Debit,
                            line.Credit);
                    }

                    entry.Complete(JournalEntryStatus.Reviewed);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    skipped.Add(new SaftEntrySkipped(fileTransaction.TransactionId, Lower(exception.Message)));
                    continue;
                }

                context.JournalEntries.Add(entry);
                imported++;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Reconciliação: a soma dos débitos e créditos da empresa (sem anulados) no
        // período do ficheiro, para confrontar com os totais declarados.
        var companyDebit = 0m;
        var companyCredit = 0m;
        if (periodFrom is { } from && periodTo is { } to)
        {
            var periodEntries = await context.JournalEntries
                .AsNoTracking()
                .Where(entry => entry.CompanyId == companyId &&
                    entry.Status != JournalEntryStatus.Voided &&
                    entry.EntryDate >= from &&
                    entry.EntryDate <= to)
                .Select(entry => new
                {
                    Debit = entry.Lines.Sum(line => line.Debit),
                    Credit = entry.Lines.Sum(line => line.Credit)
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            companyDebit = periodEntries.Sum(item => item.Debit);
            companyCredit = periodEntries.Sum(item => item.Credit);
        }

        return new SaftEntriesImportResult(
            imported, skipped.Count, journalsCreated, skipped,
            read.DeclaredEntryCount, read.DeclaredTotalDebit, read.DeclaredTotalCredit,
            periodFrom, periodTo, companyDebit, companyCredit);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string Lower(string message) =>
        message.Length == 0 ? message : char.ToLowerInvariant(message[0]) + message[1..];
}
