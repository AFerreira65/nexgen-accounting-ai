using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class FiscalDossierServiceTests
{
    [TestMethod]
    public async Task ListsTheElementsWithLedgerEvidenceAndKeepsTheirStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_FiscalDossier_{Guid.NewGuid():N}";
        var configured = Environment.GetEnvironmentVariable("ACCOUNTINGAI_TEST_SQL_CONNECTION");
        var builder = string.IsNullOrWhiteSpace(configured)
            ? new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            }
            : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = databaseName;

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure"))
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Dossier", "gabinete-dossier"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Dossier, Lda.", "DF-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Dossier", "df@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("1201", "Banco"), ("219", "Perdas por imparidade acumuladas"), ("6511", "Perdas por imparidade — clientes"),
                ("6911", "Juros suportados"), ("7871", "Mais-valias na alienação de ativos"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var number = 0;
            void Entry(params (string Account, decimal Debit, decimal Credit)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 6, 30), reference, $"Lançamento {reference}");
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            Entry(("6511", 400m, 0m), ("219", 0m, 400m));
            Entry(("1201", 150m, 0m), ("7871", 0m, 150m));
            Entry(("6911", 60m, 0m), ("1201", 0m, 60m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var statements = new FinancialStatementsService(context);
            var service = new FiscalDossierService(
                context, new FixedAssetService(context, new JournalPostingService(context)), new Modelo22Service(context, statements));
            var dossier = await service.BuildAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(dossier);
            Assert.AreEqual(12, dossier!.Elements.Count);
            Assert.AreEqual(12, dossier.Pending);
            FiscalDossierElement Element(FiscalDossier source, int element) => source.Elements.Single(item => item.Number == element);

            StringAssert.Contains(Element(dossier, 3).Evidence, "400");
            StringAssert.Contains(Element(dossier, 4).Evidence, "mais-valias (7871): 150");
            StringAssert.Contains(Element(dossier, 11).Evidence, "60");
            Assert.AreEqual("parcial", Element(dossier, 5).Support);
            Assert.AreEqual("/mapa-depreciacoes", Element(dossier, 5).Link);
            Assert.AreEqual("externo", Element(dossier, 1).Support);
            Assert.IsNull(Element(dossier, 1).Evidence);
            StringAssert.Contains(dossier.Source, "Portaria n.º 92-A/2011");

            // O estado e a nota guardam-se por exercício.
            var updated = await service.SetAsync(userId, companyId, 2026, 4, "pronto", "  Mapa 31 arquivado na pasta 2026.  ", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("pronto", Element(updated!, 4).Status);
            Assert.AreEqual("Mapa 31 arquivado na pasta 2026.", Element(updated!, 4).Note);
            updated = await service.SetAsync(userId, companyId, 2026, 9, "nao-aplicavel", null, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(10, updated!.Pending);
            updated = await service.SetAsync(userId, companyId, 2026, 4, "pendente", null, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(11, updated!.Pending);
            Assert.IsNull(Element(updated, 4).Note);
            Assert.AreEqual(12, (await service.BuildAsync(userId, companyId, 2025, CancellationToken.None).ConfigureAwait(false))!.Pending, "Outro exercício tem o seu próprio estado.");

            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetAsync(userId, companyId, 2026, 13, "pronto", null, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetAsync(userId, companyId, 2026, 4, "feito", null, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetAsync(userId, companyId, 2026, 4, "pronto", new string('x', 501), CancellationToken.None)).ConfigureAwait(false);

            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.SetAsync(Guid.NewGuid(), companyId, 2026, 4, "pronto", null, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
