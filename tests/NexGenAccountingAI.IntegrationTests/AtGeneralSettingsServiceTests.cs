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
    public async Task OnlyTheProducerChangesTheGeneralSettingsAndTheFilesAndPasswordStayInTheDatabase()
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

            Guid User(string name, CompanyUserRole? role, bool producer = false)
            {
                var id = Guid.NewGuid();
                var user = new UserAccount(id, name, $"{name.ToLowerInvariant()}@test.invalid");
                if (producer)
                {
                    user.MarkAsProducer();
                }

                context.Add(user);
                if (role is { } value)
                {
                    context.Add(new CompanyUser(companyId, id, value));
                }

                return id;
            }

            var producer = User("Produtor", null, producer: true);
            var owner = User("Dono", CompanyUserRole.Owner);
            var administrator = User("Admin", CompanyUserRole.Administrator);
            var accountant = User("Contabilista", CompanyUserRole.Accountant);
            var deactivatedProducer = User("ProdutorInativo", null, producer: true);
            await context.SaveChangesAsync().ConfigureAwait(false);
            (await context.UserAccounts.SingleAsync(item => item.Id == deactivatedProducer).ConfigureAwait(false)).Deactivate();
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var protector = new PrefixProtector();
            var invalidator = new CountingInvalidator();
            var service = new AtGeneralSettingsService(context, protector, new FakeValidator(), invalidator);
            var pfx = new byte[] { 10, 20, 30, 40, 50 };
            var cer = new byte[] { 60, 70, 80 };

            // Nem o dono, nem o administrador, nem um produtor desativado, nem um desconhecido veem ou alteram.
            foreach (var other in new[] { owner, administrator, accountant, deactivatedProducer, Guid.NewGuid() })
            {
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(
                    () => service.GetAsync(other, CancellationToken.None)).ConfigureAwait(false);
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(
                    () => service.SetCertificateAsync(other, "a.pfx", pfx, "senha", CancellationToken.None)).ConfigureAwait(false);
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(
                    () => service.SetPublicKeyAsync(other, "a.cer", cer, CancellationToken.None)).ConfigureAwait(false);
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(
                    () => service.ChangePasswordAsync(other, "senha", CancellationToken.None)).ConfigureAwait(false);
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(
                    () => service.RemoveCertificateAsync(other, CancellationToken.None)).ConfigureAwait(false);
                await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(
                    () => service.RemovePublicKeyAsync(other, CancellationToken.None)).ConfigureAwait(false);
            }

            Assert.AreEqual(0, invalidator.Calls);
            Assert.IsNull(service.Read(), "Os pedidos recusados não gravam nada.");

            // O produtor vê a configuração vazia.
            var empty = await service.GetAsync(producer, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNull(empty.CertificateFileName);
            Assert.IsFalse(empty.PasswordConfigured);

            // Guarda o certificado e a senha: o ficheiro fica na base, a senha cifrada, e nada disso volta na vista.
            var saved = await service.SetCertificateAsync(
                producer, @"C:\Downloads\TesteWebservices.pfx", pfx, "senha-do-certificado", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("TesteWebservices.pfx", saved.CertificateFileName);
            Assert.IsTrue(saved.PasswordConfigured);
            Assert.IsNotNull(saved.UpdatedAtUtc);
            Assert.AreEqual(1, invalidator.Calls);

            var row = await context.AtGeneralSettings.AsNoTracking().SingleAsync().ConfigureAwait(false);
            CollectionAssert.AreEqual(pfx, row.CertificateData);
            StringAssert.StartsWith(row.EncryptedCertificatePassword, "enc:v2:");
            Assert.AreNotEqual("senha-do-certificado", row.EncryptedCertificatePassword);
            Assert.AreEqual("senha-do-certificado", protector.Unprotect(row.EncryptedCertificatePassword));

            var snapshot = service.Read()!;
            CollectionAssert.AreEqual(pfx, snapshot.CertificateData);
            Assert.AreEqual("TesteWebservices.pfx", snapshot.CertificateFileName);
            Assert.AreEqual(row.EncryptedCertificatePassword, snapshot.EncryptedCertificatePassword);

            // A chave pública guarda-se à parte; não mexe no certificado.
            var withKey = await service.SetPublicKeyAsync(producer, "ChaveCifraPublicaAT2027.cer", cer, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("ChaveCifraPublicaAT2027.cer", withKey.PublicKeyFileName);
            Assert.AreEqual("TesteWebservices.pfx", withKey.CertificateFileName);
            CollectionAssert.AreEqual(cer, service.Read()!.PublicKeyData);
            Assert.AreEqual(1, await context.AtGeneralSettings.CountAsync().ConfigureAwait(false), "Há uma só linha.");

            // Trocar a senha só vale se ela abrir o certificado guardado.
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.ChangePasswordAsync(producer, FakeValidator.WrongPassword, CancellationToken.None)).ConfigureAwait(false);
            Assert.AreEqual("senha-do-certificado", protector.Unprotect(service.Read()!.EncryptedCertificatePassword));
            await service.ChangePasswordAsync(producer, "nova-senha", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual("nova-senha", protector.Unprotect(service.Read()!.EncryptedCertificatePassword));

            // Ficheiros que não passam a validação são recusados sem gravar nada.
            var calls = invalidator.Calls;
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetCertificateAsync(producer, "x.pfx", [FakeValidator.BadFirstByte], "senha", CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetCertificateAsync(producer, "x.pfx", pfx, FakeValidator.WrongPassword, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetCertificateAsync(producer, "x.txt", pfx, "senha", CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.SetPublicKeyAsync(producer, "x.cer", [FakeValidator.BadFirstByte], CancellationToken.None)).ConfigureAwait(false);
            Assert.AreEqual(calls, invalidator.Calls);
            Assert.AreEqual("TesteWebservices.pfx", service.Read()!.CertificateFileName);

            // Retirar o certificado leva a senha com ele; a chave pública fica.
            var removed = await service.RemoveCertificateAsync(producer, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNull(removed.CertificateFileName);
            Assert.IsFalse(removed.PasswordConfigured);
            Assert.IsNull(service.Read()!.CertificateData);
            Assert.IsNull(service.Read()!.EncryptedCertificatePassword);
            Assert.IsNotNull(service.Read()!.PublicKeyData);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => service.ChangePasswordAsync(producer, "senha", CancellationToken.None)).ConfigureAwait(false);

            var keyRemoved = await service.RemovePublicKeyAsync(producer, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNull(keyRemoved.PublicKeyFileName);
            Assert.IsNull(service.Read()!.PublicKeyData);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task TheProducerAccountIsInvisibleToTheCompanyUserAdministration()
    {
        var connectionString = TestDatabase.ConnectionString("ProducerGuard");
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Produtor", "gabinete-produtor"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Produtor, Lda.", "PR-PT-0001", TaxRegion.Mainland));
            var ownerId = Guid.NewGuid();
            context.Add(new UserAccount(ownerId, "Dono", "dono@test.invalid"));
            context.Add(new CompanyUser(companyId, ownerId, CompanyUserRole.Owner));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var auth = new AuthService(context);
            var producer = await auth.CreateProducerAsync("produtor@test.invalid", "Produtor").ConfigureAwait(false);
            await auth.SetPasswordAsync("produtor@test.invalid", "uma-senha-com-mais-de-12").ConfigureAwait(false);
            Assert.IsTrue(producer.IsProducer);
            Assert.IsTrue(await auth.IsProducerAsync(producer.Id).ConfigureAwait(false));
            Assert.IsFalse(await auth.IsProducerAsync(ownerId).ConfigureAwait(false));

            var login = await auth.ValidateCredentialsAsync("PRODUTOR@test.invalid", "uma-senha-com-mais-de-12").ConfigureAwait(false);
            Assert.IsTrue(login!.IsProducer);

            // Um utilizador normal não passa a produtor por este caminho.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => auth.CreateProducerAsync("dono@test.invalid", "Dono")).ConfigureAwait(false);

            // Quem administra empresas não o vê, não o altera nem lhe dá acessos.
            var admin = new OrganizationAdminService(new CompanyAccountingProvisioner(context), context);
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => admin.UpdateUserAsync(
                ownerId, producer.Id, "Outro nome", "produtor@test.invalid", true, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => admin.SetMembershipAsync(
                ownerId, companyId, producer.Id, CompanyUserRole.Administrator, true, CancellationToken.None)).ConfigureAwait(false);
            Assert.IsFalse(await context.CompanyUsers.AnyAsync(item => item.UserId == producer.Id).ConfigureAwait(false));

            // Retirar o produtor desativa-o e tira-lhe o acesso.
            await auth.RemoveProducerAsync("produtor@test.invalid").ConfigureAwait(false);
            Assert.IsFalse(await auth.IsProducerAsync(producer.Id).ConfigureAwait(false));
            Assert.IsNull(await auth.ValidateCredentialsAsync("produtor@test.invalid", "uma-senha-com-mais-de-12").ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => auth.RemoveProducerAsync("dono@test.invalid")).ConfigureAwait(false);
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

    /// <summary>Aceita tudo menos o que começa por <see cref="BadFirstByte"/> ou usa a senha <see cref="WrongPassword"/>.</summary>
    private sealed class FakeValidator : IAtFileValidator
    {
        public const byte BadFirstByte = 0;

        public const string WrongPassword = "senha-errada";

        public AtCertificateInfo ValidateCertificate(byte[] pfx, string password)
        {
            if (pfx.Length == 0 || pfx[0] == BadFirstByte || password == WrongPassword)
            {
                throw new ArgumentException("Não foi possível abrir o certificado com essa senha.");
            }

            return new AtCertificateInfo("CN=Teste", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        }

        public AtCertificateInfo ValidatePublicKey(byte[] certificate)
        {
            if (certificate.Length == 0 || certificate[0] == BadFirstByte)
            {
                throw new ArgumentException("Não foi possível ler a chave pública.");
            }

            return new AtCertificateInfo("CN=Chave", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        }
    }

    private sealed class PrefixProtector : IAtSecretProtector
    {
        private const string Prefix = "enc:v2:";

        public bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => Prefix + plaintext;

        public string Unprotect(string? value) =>
            IsProtected(value) ? value![Prefix.Length..] : value ?? string.Empty;
    }
}
