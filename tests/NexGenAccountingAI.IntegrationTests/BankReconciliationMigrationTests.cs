using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class BankReconciliationMigrationTests
{
    [TestMethod]
    public async Task ExistingReconciliationsBecomeGroups()
    {
        var connectionString = TestDatabase.ConnectionString("BankMigration");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261004232941_TaxObligationRecords").ConfigureAwait(false);

            // A empresa e a conta são iguais nos dois modelos; só o movimento vai no formato antigo (a referência na própria linha).
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Migração", "gabinete-migracao"));
            var company = Guid.NewGuid();
            context.Add(new Company(company, firmId, "Empresa Migração, Lda.", "MG-PT-0001", TaxRegion.Mainland));
            var account = Guid.NewGuid();
            context.Add(new LedgerAccount(account, company, "12011", "Banco"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            var import = Guid.NewGuid();
            var line = Guid.NewGuid();
            // Os valores são GUIDs gerados aqui, não texto de fora.
            var seed = $"""
                INSERT INTO accounting."BankStatementImports" ("Id", "CompanyId", "AccountId", "FileName", "Format", "LineCount")
                    VALUES ('{import}', '{company}', '{account}', 'x.csv', 'CSV', 1);
                INSERT INTO accounting."BankStatementLines" ("Id", "CompanyId", "AccountId", "ImportId", "LineKey", "BookingDate", "Description", "Amount", "ReconciledEntryReference", "ReconciledAutomatically")
                    VALUES ('{line}', '{company}', '{account}', '{import}', 'K1', DATE '2026-03-02', 'TRF', 10.00, 'B-1', true);
                """;
            await context.Database.ExecuteSqlRawAsync(seed).ConfigureAwait(false);

            await context.Database.MigrateAsync().ConfigureAwait(false);

            var migrated = await context.BankStatementLines.AsNoTracking().SingleAsync().ConfigureAwait(false);
            Assert.AreEqual(line, migrated.ReconciliationId);
            var reconciliation = await context.BankReconciliations.AsNoTracking().SingleAsync().ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "B-1" }, reconciliation.EntryReferences);
            Assert.IsTrue(reconciliation.Automatic);
            Assert.AreEqual(account, reconciliation.AccountId);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
