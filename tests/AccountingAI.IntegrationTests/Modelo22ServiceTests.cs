using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class Modelo22ServiceTests
{
    [TestMethod]
    public void TheCatalogHasTheFieldsOfQuadro07()
    {
        var fields = Modelo22Service.Quadro07Fields;

        Assert.AreEqual(100, fields.Count);
        Assert.AreEqual(fields.Count, fields.Select(field => field.Number).Distinct().Count());
        var tax = fields.Single(field => field.Number == 724);
        Assert.AreEqual("acrescer", tax.Side);
        StringAssert.StartsWith(tax.Label, "IRC, incluindo as tributações autónomas");
        Assert.AreEqual("deduzir", fields.Single(field => field.Number == 774).Side);
        Assert.AreEqual("Benefícios fiscais", fields.Single(field => field.Number == 774).Label);
        // As variações patrimoniais têm grupo próprio, com o seu efeito.
        Assert.AreEqual(("variacoes", "acrescer"), (fields.Single(field => field.Number == 702).Group, fields.Single(field => field.Number == 702).Side));
        Assert.AreEqual(("variacoes", "deduzir"), (fields.Single(field => field.Number == 704).Group, fields.Single(field => field.Number == 704).Side));
        Assert.IsFalse(fields.Any(field => field.Number is 701 or 708 or 753 or 776 or 777 or 778), "Os campos calculados não se preenchem.");
        StringAssert.Contains(Modelo22Service.Source, "Modelo 22");
    }

    [TestMethod]
    public async Task ComputesTaxableProfitTaxableIncomeAndTax()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Modelo22_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Modelo 22", "gabinete-modelo-22"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Modelo 22, Lda.", "M22-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Modelo 22", "m22@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name, taxonomy) in new[]
            {
                ("1201", "Banco", 2), ("241", "Imposto sobre o rendimento", 81), ("6221", "Trabalhos especializados", 356),
                ("7211", "Serviços", 507), ("8121", "Imposto estimado para o período", 644),
            })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", taxonomy));
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

            Entry(("1201", 200000m, 0m), ("7211", 0m, 200000m));
            Entry(("6221", 40000m, 0m), ("1201", 0m, 40000m));
            Entry(("8121", 10000m, 0m), ("241", 0m, 10000m));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new Modelo22Service(context, new FinancialStatementsService(context));
            var initial = await service.BuildAsync(userId, companyId, 2026, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(initial);
            decimal Value(Modelo22Return result, string code) => result.Fields.Single(field => field.Code == code).Amount;

            // Sem nada indicado: o resultado líquido passa a lucro tributável, à taxa geral de 2026.
            Assert.AreEqual(150000m, Value(initial!, "701"));
            Assert.AreEqual(150000m, Value(initial, "778"));
            Assert.AreEqual(150000m, Value(initial, "311"));
            Assert.AreEqual(0m, Value(initial, "347-A"));
            Assert.AreEqual(28500m, Value(initial, "347-B"));
            var estimate = initial.Fields.Single(field => field.Code == "724");
            Assert.AreEqual(10000m, estimate.Suggested, "O imposto estimado na conta 8121 é sugerido, não aplicado.");
            Assert.AreEqual(0m, estimate.Amount);
            Assert.IsTrue(initial.Warnings.Any(warning => warning.Contains("campo 724", StringComparison.Ordinal)));
            Assert.IsTrue(initial.Fields.Single(field => field.Code == "701").Computed);
            Assert.IsFalse(estimate.Computed);

            var saved = await service.SaveAsync(userId, companyId, 2026,
            [
                new("724", 10000m, "Estimativa de IRC"), new("728", 500m), new("774", 1500m), new("309", 20000m),
                new(Modelo22Service.SmeField, 1m), new(Modelo22Service.MunicipalSurtaxRateField, 1.5m),
                new("359", 300m), new("360", 5000m), new("365", 800m),
            ], CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(saved);
            Assert.IsTrue(saved!.SmallOrMediumEnterprise);
            Assert.AreEqual(1.5m, saved.MunicipalSurtaxRate);
            Assert.AreEqual(150000m, Value(saved, "708"));
            Assert.AreEqual(160500m, Value(saved, "753"));
            Assert.AreEqual(1500m, Value(saved, "776"));
            Assert.AreEqual(0m, Value(saved, "777"));
            Assert.AreEqual(159000m, Value(saved, "778"));
            Assert.AreEqual(159000m, Value(saved, "302"));
            Assert.AreEqual(139000m, Value(saved, "311"));
            Assert.AreEqual(7500m, Value(saved, "347-A"), "Os primeiros 50 000 € a 15 %.");
            Assert.AreEqual(16910m, Value(saved, "347-B"), "Os restantes 89 000 € a 19 %.");
            Assert.AreEqual(24410m, Value(saved, "351"));
            Assert.AreEqual(0m, Value(saved, "373"));
            Assert.AreEqual(24410m, Value(saved, "358"));
            Assert.AreEqual(19110m, Value(saved, "361"));
            Assert.AreEqual(0m, Value(saved, "362"));
            Assert.AreEqual(2385m, Value(saved, "364"), "A derrama municipal incide sobre o lucro tributável.");
            Assert.AreEqual(22295m, Value(saved, "367"));
            Assert.AreEqual("Estimativa de IRC", saved.Fields.Single(field => field.Code == "724").Note);
            Assert.IsFalse(saved.Warnings.Any(warning => warning.Contains("campo 724", StringComparison.Ordinal)));

            // Guardar de novo substitui: o que não vem deixa de estar guardado. Um prejuízo fica no campo 777.
            var loss = await service.SaveAsync(userId, companyId, 2026, [new("774", 180000m)], CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0m, Value(loss!, "724"));
            Assert.AreEqual(30000m, Value(loss!, "777"));
            Assert.AreEqual(0m, Value(loss!, "778"));
            Assert.AreEqual(0m, Value(loss!, "367"));
            Assert.AreEqual(1, await context.IrcReturnValues.CountAsync(item => item.CompanyId == companyId).ConfigureAwait(false));

            // Os campos calculados e os desconhecidos são recusados; os valores não podem ser negativos.
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SaveAsync(userId, companyId, 2026, [new("708", 1m)], CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SaveAsync(userId, companyId, 2026, [new("999", 1m)], CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SaveAsync(userId, companyId, 2026, [new("724", -1m)], CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SaveAsync(userId, companyId, 2026, [new("724", 1m), new("724", 2m)], CancellationToken.None)).ConfigureAwait(false);

            Assert.IsNull(await service.BuildAsync(Guid.NewGuid(), companyId, 2026, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.SaveAsync(Guid.NewGuid(), companyId, 2026, [], CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
