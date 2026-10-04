using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class PartyImportServiceTests
{
    [TestMethod]
    public async Task ImportsPartiesFromSaftAndEFaturaWithoutDuplicating()
    {
        var connectionString = TestDatabase.ConnectionString("PartyImport");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Importação", "gabinete-importacao"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Importação, Lda.", "PI-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Importação", "pi@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "21111", "Cliente A"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "22111", "Fornecedor A"));

            // Já existe, só como cliente e com outro nome — o nome não pode mudar.
            var existing = Party.Create(companyId, "PT", "500584095", "Nome escolhido pelo utilizador");
            existing.SetRoles(isCustomer: true, isSupplier: false);
            context.Add(existing);

            var batch = Guid.NewGuid();
            var day = new DateOnly(2026, 3, 10);
            context.Add(EFaturaPurchase.Create(companyId, batch, "509999999", "Fornecedor e-Fatura, Lda.", "FT 1/1", null, day, "FT", null, null, 100m, 23m, 123m, "a.csv"));
            context.Add(EFaturaPurchase.Create(companyId, batch, "509999999", "Fornecedor e-Fatura, Lda.", "FT 1/2", null, day, "FT", null, null, 50m, 11.5m, 61.5m, "a.csv"));
            context.Add(EFaturaPurchase.Create(companyId, batch, "500584095", "Cliente A como fornecedor", "FT 9/1", null, day, "FT", null, null, 10m, 2.3m, 12.3m, "a.csv"));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var reader = new StubReader(
            [
                new SaftParty(SaftPartyKind.Customer, "C1", "21111", "500 584 095", "Cliente A, Lda.", "PT"),
                new SaftParty(SaftPartyKind.Supplier, "F1", "22111", "500584095", "Cliente A, Lda.", "Desconhecido"),
                new SaftParty(SaftPartyKind.Supplier, "F2", "22999", "B12345678", "Proveedor B, S.L.", "ES", "Calle Mayor, 1", "Madrid", "Desconhecido"),
                new SaftParty(SaftPartyKind.Customer, "C2", null, "999999990", "Consumidor final", null),
                new SaftParty(SaftPartyKind.Customer, "C3", null, "123456780", "NIF inválido", "PT"),
                new SaftParty(SaftPartyKind.Customer, "C4", null, "", "Sem contribuinte", "PT"),
            ]);
            var service = new PartyImportService(reader, context);
            using var content = new MemoryStream();

            // A análise não escreve.
            var preview = await service.FromSaftAsync(userId, companyId, content, apply: false, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(preview);
            Assert.IsFalse(preview!.Applied);
            Assert.AreEqual(6, preview.SourceCount);
            Assert.AreEqual(1, preview.NewCount);
            Assert.AreEqual(1, preview.UpdatedCount);
            Assert.AreEqual(3, preview.SkippedCount);
            Assert.AreEqual(1, await context.Parties.CountAsync().ConfigureAwait(false));
            context.ChangeTracker.Clear();

            // A importação cria o novo e completa o existente sem lhe mudar o nome.
            var imported = await service.FromSaftAsync(userId, companyId, content, apply: true, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(imported!.Applied);
            Assert.AreEqual("New", imported.Items[0].State);
            Assert.AreEqual("B12345678", imported.Items[0].TaxRegistrationNumber);
            Assert.IsNull(imported.Items[0].SupplierAccountCode, "A conta 22999 não existe na empresa.");
            var withAddress = await context.Parties.AsNoTracking().SingleAsync(party => party.TaxRegistrationNumber == "B12345678").ConfigureAwait(false);
            Assert.AreEqual("Calle Mayor, 1", withAddress.AddressDetail);
            Assert.AreEqual("Madrid", withAddress.City);
            Assert.IsNull(withAddress.PostalCode, "«Desconhecido» não é uma morada.");
            CollectionAssert.AreEquivalent(
                new[] { "Consumidor final (999999990)", "NIF português inválido", "Sem número de contribuinte" },
                imported.Skipped.Select(item => item.Reason).ToArray());
            context.ChangeTracker.Clear();

            var merged = await context.Parties.SingleAsync(party => party.TaxRegistrationNumber == "500584095").ConfigureAwait(false);
            Assert.AreEqual("Nome escolhido pelo utilizador", merged.Name);
            Assert.IsTrue(merged.IsCustomer && merged.IsSupplier);
            Assert.IsNotNull(merged.CustomerAccountId);
            Assert.IsNotNull(merged.SupplierAccountId);
            Assert.AreEqual(2, await context.Parties.CountAsync().ConfigureAwait(false));
            context.ChangeTracker.Clear();

            // Reimportar não muda nada.
            var again = await service.FromSaftAsync(userId, companyId, content, apply: true, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, again!.NewCount);
            Assert.AreEqual(0, again.UpdatedCount);
            Assert.AreEqual(2, again.UnchangedCount);
            context.ChangeTracker.Clear();

            // e-Fatura: um fornecedor novo (duas faturas, um terceiro) e um já conhecido.
            var efatura = await service.FromEFaturaAsync(userId, companyId, apply: true, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(efatura);
            Assert.AreEqual(2, efatura!.SourceCount);
            Assert.AreEqual(1, efatura.NewCount);
            Assert.AreEqual(1, efatura.UnchangedCount);
            context.ChangeTracker.Clear();
            var fromEFatura = await context.Parties.SingleAsync(party => party.TaxRegistrationNumber == "509999999").ConfigureAwait(false);
            Assert.IsTrue(fromEFatura.IsSupplier);
            Assert.IsFalse(fromEFatura.IsCustomer);
            Assert.AreEqual("Fornecedor e-Fatura, Lda.", fromEFatura.Name);

            // Sem acesso.
            Assert.IsNull(await service.FromSaftAsync(Guid.NewGuid(), companyId, content, false, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.FromEFaturaAsync(Guid.NewGuid(), companyId, false, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class StubReader(IReadOnlyList<SaftParty> parties) : ISaftPartiesReader
    {
        public Task<IReadOnlyList<SaftParty>> ReadAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(parties);
    }
}
