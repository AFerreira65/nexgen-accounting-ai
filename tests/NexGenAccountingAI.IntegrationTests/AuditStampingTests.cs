using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class AuditStampingTests
{
    [TestMethod]
    public async Task StampsCreatedAndModifiedOnAuditedEntities()
    {
        var connectionString = TestDatabase.ConnectionString("Audit");

        var currentUser = new CurrentUserAccessor { UserId = Guid.NewGuid() };
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .AddInterceptors(new AuditStampingInterceptor(currentUser))
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete de Auditoria", "gabinete-auditoria"));
            var companyId = Guid.NewGuid();
            var company = new Company(companyId, firmId, "Empresa Auditada, Lda.", "AUDIT-PT-0001", TaxRegion.Mainland);
            context.Add(company);
            await context.SaveChangesAsync().ConfigureAwait(false);

            var afterCreate = context.Entry(company);
            Assert.AreEqual(currentUser.UserId, afterCreate.Property("CreatedBy").CurrentValue);
            Assert.AreNotEqual(default(DateTimeOffset), afterCreate.Property("CreatedAtUtc").CurrentValue);
            Assert.IsNull(afterCreate.Property("ModifiedBy").CurrentValue);
            Assert.IsNull(afterCreate.Property("ModifiedAtUtc").CurrentValue);

            var createdAt = (DateTimeOffset)afterCreate.Property("CreatedAtUtc").CurrentValue!;

            company.Rename("Empresa Auditada e Renomeada, Lda.");
            await context.SaveChangesAsync().ConfigureAwait(false);

            context.ChangeTracker.Clear();
            var reloaded = await context.Companies.SingleAsync(item => item.Id == companyId).ConfigureAwait(false);
            var afterUpdate = context.Entry(reloaded);
            Assert.AreEqual(currentUser.UserId, afterUpdate.Property("CreatedBy").CurrentValue);
            Assert.AreEqual(
                createdAt,
                (DateTimeOffset)afterUpdate.Property("CreatedAtUtc").CurrentValue!,
                "Uma alteração não pode mexer no carimbo de criação.");
            Assert.AreEqual(currentUser.UserId, afterUpdate.Property("ModifiedBy").CurrentValue);
            Assert.IsNotNull(afterUpdate.Property("ModifiedAtUtc").CurrentValue);

            // F2-06B: o RecordInfoService lê os mesmos carimbos por nome lógico de tabela.
            context.ChangeTracker.Clear();
            var recordInfo = new RecordInfoService(context);
            var info = await recordInfo
                .FindAsync("empresas", companyId, companyId.ToString(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.IsNotNull(info);
            Assert.AreEqual(currentUser.UserId, info!.CreatedBy);
            Assert.AreEqual(currentUser.UserId, info.ModifiedBy);
            Assert.IsNotNull(info.CreatedAtUtc);
            Assert.IsNotNull(info.ModifiedAtUtc);

            Assert.IsNull(
                await recordInfo.FindAsync("inexistente", companyId, "x", CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(
                await recordInfo.FindAsync("contas", companyId, "SEM-CODIGO", CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
