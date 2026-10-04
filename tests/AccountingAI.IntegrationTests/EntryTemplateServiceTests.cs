using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class EntryTemplateServiceTests
{
    [TestMethod]
    public async Task SavesReplacesAndAppliesTemplates()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("SQL Server validation is available only on Windows.");
        }

        var databaseName = $"AccountingAI_Templates_{Guid.NewGuid():N}";
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
            context.Add(new AccountingFirm(firmId, "Gabinete Modelos", "gabinete-modelos"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Modelos, Lda.", "MD-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Modelos", "modelos@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new AccountingJournal(Guid.NewGuid(), companyId, "COMPRAS", "Compras"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "62", "Fornecimentos", allowsPosting: false));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "6221", "Trabalhos especializados"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "24323", "IVA dedutível OBS"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "22111", "Fornecedores c/c"));
            context.Add(new LedgerAccount(Guid.NewGuid(), companyId, "1201", "Depósitos à ordem"));
            context.Add(VatCode.Create(companyId, "C-OBS-NOR", "Compras OBS normal", VatRate.Normal, 23m, VatOperationNature.IvaDedutivelOutros, VatRegion.Continente));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new EntryTemplateService(context);
            EntryTemplateInput Input(string balanceAccount, bool isActive = true) => new(
                "Compra de serviços", "compras", null, "Compra de serviços", isActive,
                [
                    new EntryTemplateLineInput(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, "6221", "c-obs-nor", null),
                    new EntryTemplateLineInput(EntryTemplateLineKind.Vat, EntryTemplateSide.Debit, "24323", "C-OBS-NOR", "IVA dedutível"),
                    new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, balanceAccount, null, null),
                ]);

            var saved = await service.SaveAsync(userId, companyId, " compra-srv ", Input("22111"), CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(saved);
            Assert.AreEqual("COMPRA-SRV", saved!.Code);
            Assert.AreEqual("COMPRAS", saved.JournalCode);
            CollectionAssert.AreEqual(new[] { "6221", "24323", "22111" }, saved.Lines.Select(line => line.AccountCode).ToArray());
            Assert.AreEqual("C-OBS-NOR", saved.Lines[0].VatCode);
            context.ChangeTracker.Clear();

            // Aplicar: só calcula.
            var applied = await service.ApplyAsync(userId, companyId, "compra-srv", 100m, false, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(applied);
            Assert.AreEqual("COMPRAS", applied!.JournalCode);
            Assert.AreEqual(100m, applied.Lines[0].Debit);
            Assert.AreEqual("C-OBS-NOR", applied.Lines[0].VatCode);
            Assert.AreEqual("Compra de serviços", applied.Lines[0].Description);
            Assert.AreEqual(23m, applied.Lines[1].Debit);
            Assert.AreEqual("IVA dedutível", applied.Lines[1].Description);
            Assert.AreEqual(123m, applied.Lines[2].Credit);
            CollectionAssert.AreEqual(new[] { "Base", "Vat", "Balance" }, applied.Lines.Select(line => line.Kind).ToArray());
            Assert.AreEqual(0, await context.JournalEntries.CountAsync().ConfigureAwait(false));

            var withVat = await service.ApplyAsync(userId, companyId, "COMPRA-SRV", 61.50m, true, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(50m, withVat!.Lines[0].Debit);
            Assert.AreEqual(11.50m, withVat.Lines[1].Debit);
            Assert.AreEqual(61.50m, withVat.Lines[2].Credit);
            context.ChangeTracker.Clear();

            // Guardar de novo substitui as linhas, sem duplicar o lançamento-tipo.
            var replaced = await service.SaveAsync(userId, companyId, "COMPRA-SRV", Input("1201"), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("1201", replaced!.Lines[2].AccountCode);
            context.ChangeTracker.Clear();
            Assert.AreEqual(1, await context.EntryTemplates.CountAsync().ConfigureAwait(false));
            Assert.AreEqual(3, await context.Set<EntryTemplateLine>().CountAsync().ConfigureAwait(false));

            // Regras: conta de integração, código de IVA e diário desconhecidos.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveAsync(userId, companyId, "X", Input("62"), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveAsync(userId, companyId, "X", Input("22111") with { JournalCode = "NAO-HA" }, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service
                .SaveAsync(userId, companyId, "X", Input("22111") with
                {
                    Lines =
                    [
                        new EntryTemplateLineInput(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, "6221", "NAO-HA", null),
                        new EntryTemplateLineInput(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, "22111", null, null),
                    ],
                }, CancellationToken.None)).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Inativo: sai da lista por omissão e não se aplica.
            await service.SaveAsync(userId, companyId, "COMPRA-SRV", Input("1201", isActive: false), CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            Assert.AreEqual(0, (await service.ListAsync(userId, companyId, false, CancellationToken.None).ConfigureAwait(false))!.Count);
            Assert.AreEqual(1, (await service.ListAsync(userId, companyId, true, CancellationToken.None).ConfigureAwait(false))!.Count);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service
                .ApplyAsync(userId, companyId, "COMPRA-SRV", 10m, false, CancellationToken.None)).ConfigureAwait(false);

            // Desconhecido / sem acesso.
            Assert.IsNull(await service.ApplyAsync(userId, companyId, "NAO-HA", 10m, false, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.ListAsync(Guid.NewGuid(), companyId, false, CancellationToken.None).ConfigureAwait(false));
            Assert.IsNull(await service.SaveAsync(Guid.NewGuid(), companyId, "X", Input("22111"), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
