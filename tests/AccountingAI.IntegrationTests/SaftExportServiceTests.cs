using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class SaftExportServiceTests
{
    [TestMethod]
    public async Task BuildsTheAccountingSaftOfAFiscalYear()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_SaftExport_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Exportação", "gabinete-exportacao"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Exportação, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Exportação", "exportacao@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "VENDAS", "Diário de vendas"));
            var openingJournalId = Guid.NewGuid();
            context.Add(new AccountingJournal(openingJournalId, companyId, "ABERTURA", "Abertura"));

            var ids = new Dictionary<string, Guid>();
            void Account(string code, string name, string? parent, bool posting, int? taxonomy)
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, parent, posting, null, taxonomy is null ? null : "S", taxonomy));
            }

            Account("2", "Contas a receber e a pagar", null, false, null);
            Account("21", "Clientes", "2", false, null);
            Account("211", "Clientes c/c", "21", false, null);
            Account("21111", "Cliente A", "211", true, 10);
            Account("12", "Depósitos à ordem", null, false, null);
            Account("1201", "Banco", "12", true, 2);
            Account("51", "Capital", null, false, null);
            Account("511", "Capital subscrito", "51", true, 331);
            Account("72", "Prestações de serviços", null, false, null);
            Account("7211", "Serviços", "72", true, 507);
            Account("6221", "Sem taxonomia e sem movimento", null, true, null);

            var customer = Party.Create(companyId, "PT", "509999999", "Cliente A, Lda.");
            customer.SetRoles(isCustomer: true, isSupplier: false);
            customer.LinkAccounts(ids["21111"], null);
            customer.SetAddress("Rua do Cliente, 5", "Porto", "4000-001");
            context.Add(customer);

            JournalEntry Entry(Guid journal, string reference, DateOnly date, EntrySource source, params (string Account, decimal Debit, decimal Credit, Guid? Party)[] lines)
            {
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journal, reference, date, reference, $"Lançamento {reference}");
                entry.SetSource(source);
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, line.Party);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                return entry;
            }

            // Abertura: 1000 em banco contra capital. Não sai como movimento.
            context.Add(Entry(openingJournalId, "ABERTURA-2026-00001", new DateOnly(2026, 1, 1), EntrySource.Opening,
                ("1201", 1000m, 0m, null), ("511", 0m, 1000m, null)));
            // Venda a crédito ao cliente.
            context.Add(Entry(journalId, "VENDAS-2026-00001", new DateOnly(2026, 3, 10), EntrySource.Manual,
                ("21111", 200m, 0m, customer.Id), ("7211", 0m, 200m, null)));
            // Um lançamento importado de um SAF-T: a referência já tem a forma do formato.
            context.Add(Entry(journalId, "2026-04-02 VD 15", new DateOnly(2026, 4, 2), EntrySource.SaftImport,
                ("1201", 50m, 0m, null), ("7211", 0m, 50m, null)));
            // Anulado e de outro exercício: ficam de fora.
            var voided = Entry(journalId, "VENDAS-2026-00002", new DateOnly(2026, 5, 1), EntrySource.Manual,
                ("1201", 999m, 0m, null), ("7211", 0m, 999m, null));
            voided.Void();
            context.Add(voided);
            context.Add(Entry(journalId, "VENDAS-2027-00001", new DateOnly(2027, 1, 5), EntrySource.Manual,
                ("1201", 7m, 0m, null), ("7211", 0m, 7m, null)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var writer = new CapturingWriter();
            var service = new SaftExportService(context, writer);
            using var output = new MemoryStream();
            var report = await service.ExportAsync(userId, companyId, 2026, output, CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(report);
            Assert.IsTrue(report!.CanExport, string.Join(" | ", report.Issues.Select(issue => issue.Message)));
            Assert.AreEqual(2, report.TransactionCount);
            Assert.AreEqual(250m, report.TotalDebit);
            Assert.AreEqual(250m, report.TotalCredit);
            Assert.AreEqual(1, report.PartyCount);
            Assert.IsTrue(report.Issues.Any(issue => !issue.Blocking && issue.Message.Contains("morada", StringComparison.Ordinal)));
            Assert.AreEqual("Desconhecido", writer.Model!.Header.City);

            var model = writer.Model!;
            Assert.AreEqual("S", model.TaxonomyReference);
            Assert.AreEqual("500584095", model.Header.TaxRegistrationNumber);
            Assert.AreEqual(new DateOnly(2026, 12, 31), model.Header.EndDate);

            // Contas: a de um dígito e a sem movimento ficam de fora; as agregadoras somam.
            CollectionAssert.AreEqual(
                new[] { "12", "1201", "21", "211", "21111", "51", "511", "72", "7211" },
                model.Accounts.Select(account => account.AccountId).ToArray());
            var bank = model.Accounts.Single(account => account.AccountId == "1201");
            Assert.AreEqual(1000m, bank.OpeningDebit);
            Assert.AreEqual(1050m, bank.ClosingDebit);
            Assert.AreEqual("GM", bank.GroupingCategory);
            Assert.AreEqual("12", bank.GroupingCode);
            Assert.AreEqual(2, bank.TaxonomyCode);
            var bankGroup = model.Accounts.Single(account => account.AccountId == "12");
            Assert.AreEqual("GR", bankGroup.GroupingCategory, "O pai de um dígito não é exportado, por isso a 12 é de 1.º grau.");
            Assert.IsNull(bankGroup.GroupingCode);
            Assert.IsNull(bankGroup.TaxonomyCode);
            Assert.AreEqual(1050m, bankGroup.ClosingDebit);
            Assert.AreEqual("GA", model.Accounts.Single(account => account.AccountId == "211").GroupingCategory);
            Assert.AreEqual(1000m, model.Accounts.Single(account => account.AccountId == "511").OpeningCredit);
            Assert.AreEqual(250m, model.Accounts.Single(account => account.AccountId == "72").ClosingCredit);

            // Movimentos: sem a abertura, o anulado e o de 2027.
            var transactions = model.Journals.Single().Transactions;
            Assert.AreEqual("VENDAS", model.Journals.Single().JournalId);
            var sale = transactions.Single(item => item.DocArchivalNumber == "VENDAS-2026-00001");
            Assert.AreEqual("2026-03-10 VENDAS VENDAS-2026-00001", sale.TransactionId);
            Assert.AreEqual(3, sale.Period);
            Assert.AreEqual("N", sale.TransactionType);
            Assert.AreEqual("509999999", sale.CustomerId);
            Assert.IsNull(sale.SupplierId);
            var imported = transactions.Single(item => item.DocArchivalNumber == "15");
            Assert.AreEqual("2026-04-02 VD 15", imported.TransactionId, "A referência importada sai tal como veio.");

            var party = model.Parties.Single();
            Assert.IsTrue(party.IsCustomer);
            Assert.AreEqual("21111", party.AccountId);
            Assert.AreEqual("Porto", party.City);

            // Com a morada da empresa registada, o aviso desaparece e o cabeçalho leva-a.
            var companyEntity = await context.Companies.SingleAsync(item => item.Id == companyId).ConfigureAwait(false);
            companyEntity.SetAddress("Avenida Central, 1", "Lisboa", "1000-001");
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var withAddress = await service.ExportAsync(userId, companyId, 2026, output, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(withAddress!.Issues.Any(issue => issue.Message.Contains("morada", StringComparison.Ordinal)));
            Assert.AreEqual("Lisboa", writer.Model!.Header.City);
            Assert.AreEqual("1000-001", writer.Model.Header.PostalCode);

            // Problemas bloqueantes: conta com movimento sem taxonomia.
            context.Add(Entry(journalId, "VENDAS-2026-00003", new DateOnly(2026, 6, 1), EntrySource.Manual,
                ("6221", 10m, 0m, null), ("1201", 0m, 10m, null)));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            writer.Model = null;
            var blocked = await service.ExportAsync(userId, companyId, 2026, output, CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(blocked!.CanExport);
            Assert.IsTrue(blocked.Issues.Any(issue => issue.Blocking && issue.Message.Contains("taxonomia", StringComparison.Ordinal)));
            Assert.IsTrue(blocked.Issues.Any(issue => issue.Blocking && issue.Message.Contains("agregadora", StringComparison.Ordinal)));
            Assert.IsNull(writer.Model, "Com problemas bloqueantes o ficheiro não é escrito.");

            Assert.IsNull(await service.ReportAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private sealed class CapturingWriter : ISaftAccountingWriter
    {
        public SaftExportModel? Model { get; set; }

        public Task WriteAsync(SaftExportModel model, Stream output, CancellationToken cancellationToken = default)
        {
            Model = model;
            return Task.CompletedTask;
        }
    }
}
