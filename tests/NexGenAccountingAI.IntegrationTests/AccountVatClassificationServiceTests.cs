using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class AccountVatClassificationServiceTests
{
    [TestMethod]
    public async Task ListsRelevantAccountsAndSetsPerAccountAndInBulk()
    {
        var connectionString = TestDatabase.ConnectionString("VatClass");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete VatClass", "gabinete-vatclass"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa VatClass, Lda.", "VC-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador VatClass", "vc@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));

            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "7111", "Vendas"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "6221", "Serviços especializados"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "24331131", "IVA liquidado tx normal"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "24331331", "IVA liquidado NUE tx normal"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "1201", "Depósito à ordem"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new AccountVatClassificationService(context);

            var list = await service.ListAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(list);
            CollectionAssert.AreEqual(
                new[] { "24331131", "24331331", "6221", "7111" },
                list!.Select(row => row.AccountCode).ToArray(),
                "Só as contas de classe 6/7 e 243, sem a 1201.");
            Assert.IsTrue(list.All(row => !row.Classified));

            // Por conta.
            Assert.IsTrue(await service
                .SetAsync(userId, companyId, "7111", VatRate.Normal, VatOperationNature.TransmissoesInternas, VatRegion.Continente, CancellationToken.None)
                .ConfigureAwait(false));
            var afterSet = await service.ListAsync(userId, companyId, CancellationToken.None).ConfigureAwait(false);
            var vendas = afterSet!.Single(row => row.AccountCode == "7111");
            Assert.IsTrue(vendas.Classified);
            Assert.AreEqual("Normal", vendas.Rate);
            Assert.AreEqual("TransmissoesInternas", vendas.Nature);

            // Definir tudo «nenhuma» apaga a linha.
            Assert.IsTrue(await service
                .SetAsync(userId, companyId, "7111", VatRate.Nenhuma, VatOperationNature.Nenhuma, VatRegion.Continente, CancellationToken.None)
                .ConfigureAwait(false));
            Assert.AreEqual(0, await context.AccountVatClassifications.CountAsync().ConfigureAwait(false));

            // Em lote por prefixo (só apanha as 243*).
            var count = await service
                .BulkSetAsync(userId, companyId, "2433", VatRate.Normal, VatOperationNature.TransmissoesInternas, VatRegion.Continente, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(2, count);
            Assert.AreEqual(2, await context.AccountVatClassifications.CountAsync().ConfigureAwait(false));

            // Conta desconhecida / sem acesso.
            Assert.IsFalse(await service.SetAsync(userId, companyId, "9999", VatRate.Normal, VatOperationNature.Nenhuma, VatRegion.Continente, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, CancellationToken.None).ConfigureAwait(false));
            Assert.AreEqual(-1, await service.BulkSetAsync(Guid.NewGuid(), companyId, "24", VatRate.Normal, VatOperationNature.Nenhuma, VatRegion.Continente, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
