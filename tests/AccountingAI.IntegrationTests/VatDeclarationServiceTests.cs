using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class VatDeclarationServiceTests
{
    [TestMethod]
    public void ThePeriodIsAMonthOrAQuarter()
    {
        Assert.AreEqual("02", VatDeclarationService.PeriodOf(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        Assert.AreEqual("03T", VatDeclarationService.PeriodOf(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
        Assert.AreEqual("12T", VatDeclarationService.PeriodOf(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)));
        Assert.IsNull(VatDeclarationService.PeriodOf(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30)));
        Assert.IsNull(VatDeclarationService.PeriodOf(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 28)));
        Assert.IsNull(VatDeclarationService.PeriodOf(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 30)));
    }

    [TestMethod]
    public async Task FillsTheFieldsOfTheDeclarationFromTheLedger()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_VatDeclaration_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Declaração", "gabinete-declaracao"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Declaração, Lda.", "DP-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Declaração", "dp@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[]
            {
                ("1201", "Depósito à ordem"), ("2111", "Cliente"), ("2211", "Fornecedor"), ("311", "Compras"), ("6221", "Serviços"),
                ("7111", "Vendas"), ("24321", "IVA dedutível — inventários"), ("24323", "IVA dedutível — outros"),
                ("24331", "IVA liquidado"), ("24332", "IVA liquidado — aquisições intracomunitárias"), ("24341", "IVA — regularizações"),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            VatCode Code(string code, VatRate rate, decimal percentage, VatOperationNature nature, VatRegion region = VatRegion.Continente)
            {
                var vatCode = VatCode.Create(companyId, code, code, rate, percentage, nature, region);
                context.Add(vatCode);
                return vatCode;
            }

            var normal = Code("V-NOR", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas);
            var reduced = Code("V-RED", VatRate.Reduzida, 6m, VatOperationNature.TransmissoesInternas);
            var azores = Code("V-ACO", VatRate.Normal, 16m, VatOperationNature.TransmissoesInternas, VatRegion.Acores);
            var stock = Code("C-EXI", VatRate.Normal, 23m, VatOperationNature.IvaDedutivelExistencias);
            var other = Code("C-OBS", VatRate.Normal, 23m, VatOperationNature.IvaDedutivelOutros);
            var intra = Code("AIB", VatRate.Normal, 23m, VatOperationNature.AquisicoesIntracomunitariasBens);
            var regularisation = Code("REG-SP", VatRate.Normal, 23m, VatOperationNature.RegularizacoesFavorSujeitoPassivo);

            var number = 0;
            void Entry(params (string Account, decimal Debit, decimal Credit, VatCode? Code)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 2, 10), reference, $"Lançamento {reference}");
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, vatCodeId: line.Code?.Id);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // Vendas à taxa normal e à reduzida, e uma venda localizada nos Açores.
            Entry(("1201", 1230m, 0m, null), ("7111", 0m, 1000m, normal), ("24331", 0m, 230m, normal));
            Entry(("1201", 106m, 0m, null), ("7111", 0m, 100m, reduced), ("24331", 0m, 6m, reduced));
            Entry(("1201", 116m, 0m, null), ("7111", 0m, 100m, azores), ("24331", 0m, 16m, azores));
            // Compra de mercadorias e de serviços.
            Entry(("311", 400m, 0m, stock), ("24321", 92m, 0m, stock), ("1201", 0m, 492m, null));
            Entry(("6221", 100m, 0m, other), ("24323", 23m, 0m, other), ("1201", 0m, 123m, null));
            // Aquisição intracomunitária de bens: liquida e deduz.
            Entry(("311", 500m, 0m, intra), ("24332", 0m, 115m, intra), ("24321", 115m, 0m, stock), ("2211", 0m, 500m, null));
            // Regularização a favor do sujeito passivo.
            Entry(("24341", 10m, 0m, regularisation), ("2111", 0m, 10m, null));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new VatDeclarationService(context, new VatSummaryService(context), new VatReturnService(context));
            var declaration = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(declaration);
            Assert.AreEqual("03T", declaration!.Period);
            Assert.AreEqual("Continente", declaration.Seat);
            VatFormField Field(int number) => declaration.Fields.Single(field => field.Number == number);

            Assert.AreEqual(1000m, Field(3).Amount);
            Assert.AreEqual(230m, Field(4).Amount);
            Assert.AreEqual(100m, Field(1).Amount);
            Assert.AreEqual(6m, Field(2).Amount);
            Assert.AreEqual(500m, Field(12).Amount);
            Assert.AreEqual(115m, Field(13).Amount);
            Assert.AreEqual(500m, Field(10).Amount);
            Assert.IsTrue(Field(10).Computed);
            Assert.AreEqual(115m, Field(11).Amount);
            Assert.AreEqual(207m, Field(22).Amount, "Mercadorias (92) e a dedução da aquisição intracomunitária (115).");
            CollectionAssert.AreEqual(new[] { "24321" }, Field(22).Sources.Select(source => source.AccountCode).ToArray());
            Assert.AreEqual(23m, Field(24).Amount);
            Assert.AreEqual(10m, Field(40).Amount);
            // A venda dos Açores não entra nos campos 3 e 4: só o imposto, no total do Anexo R.
            Assert.AreEqual(16m, Field(66).Amount);
            Assert.AreEqual(0m, Field(65).Amount);
            Assert.IsFalse(Field(7).Supported);

            Assert.AreEqual(1600m, Field(90).Amount);
            Assert.AreEqual(240m, Field(91).Amount);
            Assert.AreEqual(367m, Field(92).Amount);
            Assert.AreEqual(127m, Field(93).Amount);
            Assert.AreEqual(0m, Field(94).Amount);

            // Bate com o apuramento contabilístico das contas 243.
            Assert.AreEqual(127m, declaration.AccountingPayable);
            Assert.AreEqual(0m, declaration.Difference);
            Assert.IsTrue(declaration.Warnings.Any(warning => warning.Contains("Anexo R", StringComparison.Ordinal)));
            Assert.IsTrue(declaration.Warnings.Any(warning => warning.Contains("06-A", StringComparison.Ordinal)));
            Assert.IsTrue(declaration.Warnings.Any(warning => warning.Contains("anexos das regularizações", StringComparison.Ordinal)));
            Assert.IsFalse(declaration.Warnings.Any(warning => warning.Contains("correspondência rigorosa", StringComparison.Ordinal)));
            Assert.IsFalse(declaration.Warnings.Any(warning => warning.Contains("difere do apuramento", StringComparison.Ordinal)));

            // Com excesso a reportar do período anterior, passa a haver imposto a recuperar.
            var withCredit = await service
                .BuildAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 200m, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(200m, withCredit!.Fields.Single(field => field.Number == 61).Amount);
            Assert.AreEqual(73m, withCredit.Fields.Single(field => field.Number == 94).Amount);
            Assert.AreEqual(73m, withCredit.Fields.Single(field => field.Number == 96).Amount);
            Assert.AreEqual(0m, withCredit.Difference);

            Assert.IsNull(await service
                .BuildAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, CancellationToken.None)
                .ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
