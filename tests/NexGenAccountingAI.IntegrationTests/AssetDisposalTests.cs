using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class AssetDisposalTests
{
    [TestMethod]
    public async Task PostsTheSaleAndTheWriteOffOfFixedAssets()
    {
        var connectionString = TestDatabase.ConnectionString("AssetDisposal");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Alienações", "gabinete-alienacoes"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Alienações, Lda.", "AL-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Alienações", "alienacoes@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "GERAL", "Geral"));
            foreach (var (code, name) in new[]
            {
                ("4331", "Equipamento básico"), ("4381", "Depreciações acumuladas"), ("6423", "Depreciações do equipamento básico"),
                ("2781", "Devedores diversos"), ("6871", "Alienações"), ("6873", "Abates"), ("7871", "Alienações"),
            })
            {
                context.Add(new LedgerAccount(Guid.NewGuid(), companyId, code, name));
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new FixedAssetService(context, new JournalPostingService(context));
            FixedAssetInput Input(string code, decimal value) => new(
                code, $"Máquina {code}", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1), value, 0m, 25m, true, "4331", "4381", "6423", null);
            await service.CreateAsync(userId, companyId, Input("MAQ-1", 10000m), CancellationToken.None).ConfigureAwait(false);
            await service.CreateAsync(userId, companyId, Input("MAQ-2", 4000m), CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Venda em julho de 2026: 2024 e 2025 inteiros e janeiro a junho de 2026 (até ao mês anterior).
            var sale = new AssetDisposalInput(new DateOnly(2026, 7, 15), 5000m, "2781", "GERAL", "FT 99");
            var preview = await service.PreviewDisposalAsync(userId, companyId, "maq-1", sale, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(preview);
            Assert.AreEqual(6250m, preview.Accumulated);
            Assert.AreEqual(3750m, preview.NetBookValue);
            Assert.AreEqual(1250m, preview.Result);
            Assert.AreEqual("alienação", preview.Kind);
            CollectionAssert.AreEqual(new[] { "4381", "4331", "2781", "7871" }, preview.Lines.Select(line => line.AccountCode).ToArray());
            Assert.AreEqual(1250m, preview.Lines.Single(line => line.AccountCode == "7871").Credit);
            Assert.AreEqual(preview.Lines.Sum(line => line.Debit), preview.Lines.Sum(line => line.Credit));
            StringAssert.Contains(preview.Warning, "lance primeiro as depreciações", "Não há depreciações lançadas na contabilidade.");

            var posted = await service.PostDisposalAsync(userId, companyId, "MAQ-1", sale, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("FT 99", posted!.Movement.DocumentReference);
            context.ChangeTracker.Clear();
            var asset = await context.FixedAssets.AsNoTracking().SingleAsync(item => item.Code == "MAQ-1").ConfigureAwait(false);
            Assert.AreEqual(new DateOnly(2026, 7, 15), asset.DisposalDate);
            Assert.AreEqual(posted.Movement.Reference, asset.DisposalEntryReference);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.PostDisposalAsync(userId, companyId, "MAQ-1", sale, CancellationToken.None)).ConfigureAwait(false);

            // Abate (sem preço): a perda vai para 6873; venda abaixo do valor líquido: 6871.
            var writeOff = await service.PreviewDisposalAsync(userId, companyId, "MAQ-2", sale with { SalePrice = 0m, CounterpartAccountCode = null }, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("abate", writeOff!.Kind);
            Assert.AreEqual(1500m, writeOff.Lines.Single(line => line.AccountCode == "6873").Debit);
            var loss = await service.PreviewDisposalAsync(userId, companyId, "MAQ-2", sale with { SalePrice = 1000m }, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(500m, loss!.Lines.Single(line => line.AccountCode == "6871").Debit);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewDisposalAsync(userId, companyId, "MAQ-2", sale with { CounterpartAccountCode = null }, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewDisposalAsync(userId, companyId, "MAQ-2", sale with { Date = new DateOnly(2023, 1, 1) }, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsNull(await service.PreviewDisposalAsync(userId, companyId, "NAO-HA", sale, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.PreviewDisposalAsync(Guid.NewGuid(), companyId, "MAQ-2", sale, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
