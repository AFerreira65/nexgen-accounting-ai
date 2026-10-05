using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class DossierMapsServiceTests
{
    [TestMethod]
    public async Task BuildsModelo32AndModelo31FromTheAssetRegister()
    {
        var connectionString = TestDatabase.ConnectionString("DossierMaps");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Mapas", "gabinete-mapas"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Mapas, Lda.", "MP-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Mapas", "mapas@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "GERAL", "Geral"));
            foreach (var (code, name) in new[]
            {
                ("4331", "Equipamento básico"), ("4381", "Depreciações acumuladas"), ("6423", "Depreciações"),
                ("4421", "Programas de computador"), ("4481", "Amortizações acumuladas"), ("6431", "Amortizações"),
                ("2781", "Devedores diversos"), ("7871", "Alienações"), ("6871", "Alienações"),
            })
            {
                context.Add(new LedgerAccount(Guid.NewGuid(), companyId, code, name));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var assets = new FixedAssetService(context, new JournalPostingService(context));
            FixedAssetInput Tangible(string code, DateOnly start, decimal value, decimal rate, decimal? taxRate, string? taxCode = "2375") => new(
                code, $"Máquina {code}", start, start, value, 0m, rate, true, "4331", "4381", "6423", null, true, taxCode, taxRate);
            await assets.CreateAsync(userId, companyId, Tangible("A", new DateOnly(2024, 1, 1), 10000m, 25m, 20m), CancellationToken.None).ConfigureAwait(false);
            await assets.CreateAsync(userId, companyId, new FixedAssetInput(
                "B", "Programa de faturação", new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 1), 3000m, 0m, 33.33m, true, "4421", "4481", "6431", null), CancellationToken.None).ConfigureAwait(false);
            await assets.CreateAsync(userId, companyId, Tangible("C", new DateOnly(2020, 1, 1), 1000m, 50m, 50m), CancellationToken.None).ConfigureAwait(false);
            await assets.CreateAsync(userId, companyId, Tangible("D", new DateOnly(2023, 1, 1), 4000m, 25m, 20m), CancellationToken.None).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => assets.CreateAsync(userId, companyId, Tangible("E", new DateOnly(2023, 1, 1), 1m, 10m, 10m, "23"), CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            await assets.PostDisposalAsync(userId, companyId, "D",
                new AssetDisposalInput(new DateOnly(2025, 7, 15), 3000m, "2781", "GERAL", "FT 7", 1.05m), CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var maps = await new DossierMapsService(context).BuildAsync(userId, companyId, 2025, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(maps);
            Assert.AreEqual(1, maps.FullyDepreciatedOmitted, "O C está totalmente depreciado desde 2021.");

            var tangible = maps.Modelo32.Single(map => map.Nature == 1);
            Assert.AreEqual("Ativos fixos tangíveis", tangible.NatureLabel);
            var a = tangible.Lines.Single(line => line.AssetCode == "A");
            Assert.AreEqual("2375", a.TaxTableCode);
            Assert.AreEqual(1, a.StartMonth);
            Assert.AreEqual(2500m, a.BookedInPeriod, "Coluna 8: 25 % de 10 000.");
            Assert.AreEqual(2000m, a.TaxLimit, "Coluna 12: 20 % de 10 000.");
            Assert.AreEqual(500m, a.NotAccepted, "Coluna 15 = (8) − (12).");
            Assert.AreEqual(2000m, a.AcceptedInPriorPeriods, "Em 2024 só foram aceites os 2 000 do limite.");
            var d = tangible.Lines.Single(line => line.AssetCode == "D");
            Assert.AreEqual(500m, d.BookedInPeriod, "Alienado em julho: seis duodécimos.");
            Assert.AreEqual(400m, d.TaxLimit);
            Assert.AreEqual(600m, tangible.NotAccepted);

            var intangible = maps.Modelo32.Single(map => map.Nature == 2).Lines.Single();
            Assert.IsTrue(intangible.RateFromAccounting);
            Assert.AreEqual(0m, intangible.NotAccepted, "Sem taxa fiscal, o limite é o próprio gasto.");
            Assert.IsTrue(maps.Warnings.Any(warning => warning.Contains("Sem taxa máxima fiscal") && warning.Contains("B")));
            Assert.IsTrue(maps.Warnings.Any(warning => warning.Contains("não batem com os gastos lançados")), "Nada foi lançado nas contas 64.");

            // Modelo 31: 3 000 de preço; contabilístico 3 000 − (4 000 − 2 500) = 1 500;
            // fiscal 3 000 − (4 000 − 2 000) × 1,05 = 900 (aceites: 800 + 800 + 400).
            var sale = maps.Modelo31.Single().Lines.Single();
            Assert.AreEqual(2023, sale.AcquisitionYear);
            Assert.AreEqual(2500m, sale.BookDepreciation);
            Assert.AreEqual(1500m, sale.BookGain);
            Assert.AreEqual(2000m, sale.AcceptedDepreciation);
            Assert.AreEqual(1.05m, sale.Coefficient);
            Assert.AreEqual(900m, sale.TaxGain);
            Assert.AreEqual(900m, maps.Modelo31TaxGain);

            CollectionAssert.Contains(maps.Sources.ToList(), DossierMapsService.Modelo32Source);

            // O ficheiro XML: o cabeçalho, a natureza do Modelo 31 na codificação desse mapa e os montantes sem sinal.
            var xml = System.Text.Encoding.UTF8.GetString(DossierMapsXmlWriter.Write("500584095", 2025, maps, null));
            StringAssert.Contains(xml, "<NIF>500584095</NIF>");
            StringAssert.Contains(xml, "<Q02-15>500.00</Q02-15>");
            StringAssert.Contains(xml, "<Q02-12>+</Q02-12>");
            StringAssert.Contains(xml, "<Q02-11>1.05</Q02-11>");
            Assert.ThrowsExactly<InvalidOperationException>(() => DossierMapsXmlWriter.Write("MP-PT-0001", 2025, maps, null));
            XsdValidation.ValidateIfConfigured(xml, "ACCOUNTINGAI_DOSSIER_XSD", DossierMapsXmlWriter.Namespace);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => new DossierMapsService(context).BuildAsync(userId, companyId, 99, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await new DossierMapsService(context).BuildAsync(Guid.NewGuid(), companyId, 2025, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
