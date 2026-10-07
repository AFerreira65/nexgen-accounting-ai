using Microsoft.EntityFrameworkCore;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class AtGeneralSettingsServiceTests
{
    [TestMethod]
    public async Task OnlyOwnersAndAdministratorsChangeTheGeneralSettingsAndThePasswordStaysEncrypted()
    {
        var connectionString = TestDatabase.ConnectionString("AtGeneral");
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete AT Geral", "gabinete-at-geral"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa AT Geral, Lda.", "AG-PT-0001", TaxRegion.Mainland));
            var inactiveCompanyId = Guid.NewGuid();
            var inactiveCompany = new Company(inactiveCompanyId, firmId, "Empresa Inativa, Lda.", "AG-PT-0002", TaxRegion.Mainland);
            context.Add(inactiveCompany);

            Guid User(string name, CompanyUserRole role, Guid company)
            {
                var id = Guid.NewGuid();
                context.Add(new UserAccount(id, name, $"{name.ToLowerInvariant()}@test.invalid"));
                context.Add(new CompanyUser(company, id, role));
                return id;
            }

            var owner = User("Dono", CompanyUserRole.Owner, companyId);
            var administrator = User("Admin", CompanyUserRole.Administrator, companyId);
            var accountant = User("Contabilista", CompanyUserRole.Accountant, companyId);
            var viewer = User("Consulta", CompanyUserRole.Viewer, companyId);
            await context.SaveChangesAsync().ConfigureAwait(false);
            inactiveCompany.Deactivate();
            var ownerOfInactive = User("DonoInativa", CompanyUserRole.Owner, inactiveCompanyId);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var protector = new PrefixProtector();
            var invalidator = new CountingInvalidator();
            var service = new AtGeneralSettingsService(context, protector, invalidator);

            // Antes de haver configuração, só quem pode vê-la a vê vazia.
            var empty = await service.GetAsync(owner, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(empty.CanEdit);
            Assert.IsNull(empty.CertificateDirectory);
            Assert.IsFalse(empty.PasswordConfigured);
            Assert.IsNull(service.Read());

            // Quem não é dono nem administrador não vê nem altera.
            foreach (var other in new[] { accountant, viewer, ownerOfInactive, Guid.NewGuid() })
            {
                var hidden = await service.GetAsync(other, CancellationToken.None).ConfigureAwait(false);
                Assert.IsFalse(hidden.CanEdit);
                Assert.IsNull(hidden.CertificateDirectory);
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() => service.SaveAsync(
                    other, new AtGeneralSettingsInput("C:\\AT", "a.pfx", null, null, "senha", false), CancellationToken.None)).ConfigureAwait(false);
            }

            Assert.AreEqual(0, invalidator.Calls);
            Assert.IsNull(service.Read(), "Os pedidos recusados não gravam nada.");

            // O dono guarda os locais e a senha: ela fica cifrada e nunca volta.
            var saved = await service.SaveAsync(
                owner,
                new AtGeneralSettingsInput(" C:\\AT\\certificados\\ ", "TesteWebservices.pfx", "C:\\AT\\chaves", "ChaveCifraPublicaAT2027.cer", "senha-do-certificado", false),
                CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(saved.CanEdit);
            Assert.AreEqual("C:\\AT\\certificados", saved.CertificateDirectory);
            Assert.AreEqual("TesteWebservices.pfx", saved.CertificateFileName);
            Assert.AreEqual("ChaveCifraPublicaAT2027.cer", saved.PublicKeyFileName);
            Assert.IsTrue(saved.PasswordConfigured);
            Assert.IsNotNull(saved.UpdatedAtUtc);
            Assert.AreEqual(1, invalidator.Calls);

            var row = await context.AtGeneralSettings.AsNoTracking().SingleAsync().ConfigureAwait(false);
            StringAssert.StartsWith(row.EncryptedCertificatePassword, "enc:v1:");
            Assert.AreNotEqual("senha-do-certificado", row.EncryptedCertificatePassword);
            Assert.AreEqual("senha-do-certificado", protector.Unprotect(row.EncryptedCertificatePassword));

            var snapshot = service.Read();
            Assert.AreEqual("C:\\AT\\certificados", snapshot!.CertificateDirectory);
            Assert.AreEqual(row.EncryptedCertificatePassword, snapshot.EncryptedCertificatePassword);

            // Um administrador altera só os nomes: a senha, não indicada, mantém-se.
            var renamed = await service.SaveAsync(
                administrator,
                new AtGeneralSettingsInput("C:\\AT\\certificados", "Producao.pfx", "C:\\AT\\chaves", "ChaveCifraPublicaAT2027.cer", null, false),
                CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("Producao.pfx", renamed.CertificateFileName);
            Assert.IsTrue(renamed.PasswordConfigured);
            Assert.AreEqual(1, await context.AtGeneralSettings.CountAsync().ConfigureAwait(false), "Há uma só linha.");
            Assert.AreEqual(row.EncryptedCertificatePassword, service.Read()!.EncryptedCertificatePassword);

            // Uma senha em branco também mantém; uma senha nova substitui; retirar tira.
            await service.SaveAsync(owner, new AtGeneralSettingsInput("C:\\AT\\certificados", "Producao.pfx", null, null, "", false), CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue((await service.GetAsync(owner, CancellationToken.None).ConfigureAwait(false)).PasswordConfigured);
            await service.SaveAsync(owner, new AtGeneralSettingsInput("C:\\AT\\certificados", "Producao.pfx", null, null, "outra-senha", false), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("outra-senha", protector.Unprotect(service.Read()!.EncryptedCertificatePassword));
            var removed = await service.SaveAsync(owner, new AtGeneralSettingsInput("C:\\AT\\certificados", "Producao.pfx", null, null, null, true), CancellationToken.None).ConfigureAwait(false);
            Assert.IsFalse(removed.PasswordConfigured);
            Assert.IsNull(removed.PublicKeyDirectory, "O que não vem fica por indicar.");

            // Um valor já cifrado guarda-se tal como está.
            await service.SaveAsync(owner, new AtGeneralSettingsInput("C:\\AT", "a.pfx", null, null, "enc:v1:ja-cifrada", false), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("enc:v1:ja-cifrada", service.Read()!.EncryptedCertificatePassword);

            // Dados inválidos são recusados sem gravar.
            var calls = invalidator.Calls;
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveAsync(
                owner, new AtGeneralSettingsInput("relativa", "a.pfx", null, null, null, false), CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveAsync(
                owner, new AtGeneralSettingsInput("C:\\AT", "a.pfx", "C:\\AT", null, null, false), CancellationToken.None)).ConfigureAwait(false);
            Assert.AreEqual(calls, invalidator.Calls);
            Assert.AreEqual("a.pfx", service.Read()!.CertificateFileName);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class CountingInvalidator : IAtSettingsInvalidator
    {
        public int Calls { get; private set; }

        public void Invalidate() => Calls++;
    }

    private sealed class PrefixProtector : IAtSecretProtector
    {
        private const string Prefix = "enc:v1:";

        public bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => Prefix + plaintext;

        public string Unprotect(string? value) =>
            IsProtected(value) ? value![Prefix.Length..] : value ?? string.Empty;
    }
}
