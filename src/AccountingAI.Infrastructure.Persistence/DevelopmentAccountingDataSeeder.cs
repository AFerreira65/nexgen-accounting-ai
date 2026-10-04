using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.Infrastructure.Persistence;

public static class DevelopmentAccountingDataSeeder
{
    public static async Task SeedDevelopmentAccountingAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var accounts = CreateAccounts();
        var existingAccountIds = await context.Accounts
            .Where(item => accounts.Select(account => account.Id).Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.Accounts.AddRange(accounts.Where(item => !existingAccountIds.Contains(item.Id)));

        var journals = CreateJournals();
        var existingJournalIds = await context.Journals
            .Where(item => journals.Select(journal => journal.Id).Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.Journals.AddRange(journals.Where(item => !existingJournalIds.Contains(item.Id)));

        var entries = CreateEntries();
        var existingEntryIds = await context.JournalEntries
            .Where(item => entries.Select(entry => entry.Id).Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.JournalEntries.AddRange(entries.Where(item => !existingEntryIds.Contains(item.Id)));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LedgerAccount[] CreateAccounts() =>
    [
        Account("50000000-0000-0000-0000-000000000001", DevelopmentDataSeeder.DemoCompanyId, "6221", "Material de escritório"),
        Account("50000000-0000-0000-0000-000000000002", DevelopmentDataSeeder.DemoCompanyId, "24323", "IVA dedutível — Outros bens e serviços"),
        Account("50000000-0000-0000-0000-000000000003", DevelopmentDataSeeder.DemoCompanyId, "2211001", "Norte Office, Lda."),
        Account("50000000-0000-0000-0000-000000000004", DevelopmentDataSeeder.DemoCompanyId, "2111001", "Maré Clara, SA"),
        Account("50000000-0000-0000-0000-000000000005", DevelopmentDataSeeder.DemoCompanyId, "7211", "Serviços prestados — Continente"),
        Account("50000000-0000-0000-0000-000000000006", DevelopmentDataSeeder.DemoCompanyId, "24331", "IVA liquidado"),
        Account("50000000-0000-0000-0000-000000000007", DevelopmentDataSeeder.DemoCompanyId, "1211", "Banco Atlântico — DO"),
        Account("51000000-0000-0000-0000-000000000001", DevelopmentDataSeeder.DemoIslandCompanyId, "2111001", "Hotel Bruma, Lda."),
        Account("51000000-0000-0000-0000-000000000002", DevelopmentDataSeeder.DemoIslandCompanyId, "7212", "Serviços prestados — Açores"),
        Account("51000000-0000-0000-0000-000000000003", DevelopmentDataSeeder.DemoIslandCompanyId, "24332", "IVA liquidado — Açores")
    ];

    private static AccountingJournal[] CreateJournals() =>
    [
        Journal("60000000-0000-0000-0000-000000000001", DevelopmentDataSeeder.DemoCompanyId, "COMPRAS", "Diário de Compras"),
        Journal("60000000-0000-0000-0000-000000000002", DevelopmentDataSeeder.DemoCompanyId, "VENDAS", "Diário de Vendas"),
        Journal("60000000-0000-0000-0000-000000000003", DevelopmentDataSeeder.DemoCompanyId, "BANCOS", "Diário de Bancos"),
        Journal("61000000-0000-0000-0000-000000000001", DevelopmentDataSeeder.DemoIslandCompanyId, "VENDAS", "Diário de Vendas — Açores")
    ];

    private static JournalEntry[] CreateEntries() =>
    [
        Entry(
            "70000000-0000-0000-0000-000000000001",
            DevelopmentDataSeeder.DemoCompanyId,
            "60000000-0000-0000-0000-000000000001",
            "COMP-2026-00842",
            new DateOnly(2026, 8, 29),
            "FT 2026/418 · Norte Office, Lda.",
            "Aquisição de material de escritório",
            JournalEntryStatus.Reviewed,
            [
                ("80000000-0000-0000-0000-000000000001", "50000000-0000-0000-0000-000000000001", "Base tributável", 1200m, 0m),
                ("80000000-0000-0000-0000-000000000002", "50000000-0000-0000-0000-000000000002", "IVA 23%", 276m, 0m),
                ("80000000-0000-0000-0000-000000000003", "50000000-0000-0000-0000-000000000003", "Conta corrente do fornecedor", 0m, 1476m)
            ]),
        Entry(
            "70000000-0000-0000-0000-000000000002",
            DevelopmentDataSeeder.DemoCompanyId,
            "60000000-0000-0000-0000-000000000002",
            "VEND-2026-00327",
            new DateOnly(2026, 8, 28),
            "FT A/327 · Maré Clara, SA",
            "Prestação de serviços de consultoria",
            JournalEntryStatus.Reviewed,
            [
                ("80000000-0000-0000-0000-000000000004", "50000000-0000-0000-0000-000000000004", "Conta corrente do cliente", 3075m, 0m),
                ("80000000-0000-0000-0000-000000000005", "50000000-0000-0000-0000-000000000005", "Base tributável", 0m, 2500m),
                ("80000000-0000-0000-0000-000000000006", "50000000-0000-0000-0000-000000000006", "IVA 23%", 0m, 575m)
            ]),
        Entry(
            "70000000-0000-0000-0000-000000000003",
            DevelopmentDataSeeder.DemoCompanyId,
            "60000000-0000-0000-0000-000000000003",
            "BANC-2026-00196",
            new DateOnly(2026, 8, 27),
            "TRF 908433 · Norte Office, Lda.",
            "Pagamento a fornecedor por transferência",
            JournalEntryStatus.PendingReview,
            [
                ("80000000-0000-0000-0000-000000000007", "50000000-0000-0000-0000-000000000003", "Liquidação do fornecedor", 1476m, 0m),
                ("80000000-0000-0000-0000-000000000008", "50000000-0000-0000-0000-000000000007", "Saída bancária", 0m, 1476m)
            ]),
        Entry(
            "71000000-0000-0000-0000-000000000001",
            DevelopmentDataSeeder.DemoIslandCompanyId,
            "61000000-0000-0000-0000-000000000001",
            "VEND-AZO-2026-00014",
            new DateOnly(2026, 8, 26),
            "FT AZ/14 · Hotel Bruma, Lda.",
            "Serviços de apoio administrativo",
            JournalEntryStatus.PendingReview,
            [
                ("81000000-0000-0000-0000-000000000001", "51000000-0000-0000-0000-000000000001", "Conta corrente do cliente", 1160m, 0m),
                ("81000000-0000-0000-0000-000000000002", "51000000-0000-0000-0000-000000000002", "Base tributável", 0m, 1000m),
                ("81000000-0000-0000-0000-000000000003", "51000000-0000-0000-0000-000000000003", "IVA 16%", 0m, 160m)
            ])
    ];

    private static LedgerAccount Account(string id, Guid companyId, string code, string name) =>
        new(Guid.Parse(id), companyId, code, name);

    private static AccountingJournal Journal(string id, Guid companyId, string code, string name) =>
        new(Guid.Parse(id), companyId, code, name);

    private static JournalEntry Entry(
        string id,
        Guid companyId,
        string journalId,
        string reference,
        DateOnly date,
        string document,
        string description,
        JournalEntryStatus status,
        (string Id, string AccountId, string Description, decimal Debit, decimal Credit)[] lines)
    {
        var entry = new JournalEntry(
            Guid.Parse(id),
            companyId,
            Guid.Parse(journalId),
            reference,
            date,
            document,
            description);

        foreach (var line in lines)
        {
            entry.AddLine(
                Guid.Parse(line.Id),
                Guid.Parse(line.AccountId),
                line.Description,
                line.Debit,
                line.Credit);
        }

        entry.Complete(status);
        return entry;
    }
}
