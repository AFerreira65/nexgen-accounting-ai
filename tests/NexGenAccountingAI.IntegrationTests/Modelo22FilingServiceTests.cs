using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A entrega do Modelo 22 (F4C-06), com um webservice simulado no lugar da AT.</summary>
[TestClass]
public sealed class Modelo22FilingServiceTests
{
    [TestMethod]
    public async Task ValidatesAndSubmitsThroughTheGatewayAndKeepsARecord()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("M22Filing"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete M22", "gabinete-m22"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa M22, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista M22", "m22@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var gateway = new FakeGateway();
            var service = new Modelo22FilingService(
                context, new Modelo22Service(context, new FinancialStatementsService(context)), gateway, new PlainProtector(), new FixedUser(userId));
            var none = CancellationToken.None;

            // Sem credenciais guardadas, não se pede nada à AT.
            var noCredentials = await service.ValidateAsync(userId, companyId, 2026, none).ConfigureAwait(false);
            StringAssert.Contains(noCredentials!.Refusal, "credenciais");
            Assert.AreEqual(0, gateway.Calls);

            context.Add(CompanyAtCredentials.Create(companyId, "500584095/3", "cifrado:senha-do-portal", VatFilingPeriodicity.Mensal, AtEnvironment.Testes));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            gateway.Ready = false;
            StringAssert.Contains((await service.ValidateAsync(userId, companyId, 2026, none).ConfigureAwait(false))!.Refusal, "certificado");
            gateway.Ready = true;

            // Validar: vai o ficheiro do Modelo 22 com as credenciais decifradas, ao ambiente de testes.
            var validated = await service.ValidateAsync(userId, companyId, 2026, none).ConfigureAwait(false);
            Assert.IsTrue(validated!.Result!.Succeeded);
            Assert.AreEqual(new AtCredential("500584095/3", "senha-do-portal", AtActor.Taxpayer), gateway.LastCredentials!.Single());
            Assert.IsFalse(gateway.LastProduction);
            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(gateway.LastXml!), "<Modelo22IRCv2026");

            var submitted = await service.SubmitAsync(userId, companyId, 2026, acceptAlerts: true, none).ConfigureAwait(false);
            Assert.AreEqual(123456789L, submitted!.Result!.DeclarationId);
            Assert.IsTrue(gateway.LastAcceptAlerts);

            var rows = await context.AtExchanges.AsNoTracking().Where(item => item.CompanyId == companyId).OrderBy(item => item.RequestedAtUtc).ToListAsync().ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "validarDeclaracao", "submeterDeclaracao" }, rows.Select(row => row.Operation).ToArray());
            Assert.IsTrue(rows.All(row => row.Service == "modelo22" && row.Period == "2026"));

            // Em produção valida-se, mas a submissão está fechada na aplicação.
            var stored = await context.CompanyAtCredentials.SingleAsync(item => item.CompanyId == companyId).ConfigureAwait(false);
            stored.Update("500584095/3", "cifrado:senha-do-portal", VatFilingPeriodicity.Mensal, AtEnvironment.Producao);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var calls = gateway.Calls;
            StringAssert.Contains((await service.SubmitAsync(userId, companyId, 2026, false, none).ConfigureAwait(false))!.Refusal, "produção");
            Assert.AreEqual(calls, gateway.Calls);

            Assert.IsNull(await service.ValidateAsync(Guid.NewGuid(), companyId, 2026, none).ConfigureAwait(false), "Sem acesso à empresa.");
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class FixedUser(Guid userId) : ICurrentUserAccessor
    {
        public Guid? UserId { get; set; } = userId;
    }

    private sealed class PlainProtector : IAtSecretProtector
    {
        public bool IsProtected(string? value) => value?.StartsWith("cifrado:", StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => "cifrado:" + plaintext;

        public string Unprotect(string? value) => value?.Replace("cifrado:", string.Empty, StringComparison.Ordinal) ?? string.Empty;
    }

    private sealed class FakeGateway : IModelo22AtGateway
    {
        public bool Ready { get; set; } = true;

        public int Calls { get; private set; }

        public byte[]? LastXml { get; private set; }

        public IReadOnlyList<AtCredential>? LastCredentials { get; private set; }

        public bool LastProduction { get; private set; }

        public bool LastAcceptAlerts { get; private set; }

        public AtGatewayStatus Status() => Ready ? new AtGatewayStatus(true, true, null) : new AtGatewayStatus(false, false, "O certificado da AT não está configurado.");

        public Task<AtDeclarationResult> ValidateAsync(
            byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default)
        {
            Record(declarationXml, credentials, production);
            return Task.FromResult(new AtDeclarationResult(true, 0, "Declaração válida.", [], [], null, 200, 40));
        }

        public Task<AtDeclarationResult> SubmitAsync(
            byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, bool acceptAlerts, CancellationToken cancellationToken = default)
        {
            Record(declarationXml, credentials, production);
            LastAcceptAlerts = acceptAlerts;
            return Task.FromResult(new AtDeclarationResult(true, 0, "Declaração submetida com sucesso.", [], [], 123456789L, 200, 90));
        }

        private void Record(byte[] xml, IReadOnlyList<AtCredential> credentials, bool production)
        {
            Calls++;
            LastXml = xml;
            LastCredentials = credentials;
            LastProduction = production;
        }
    }
}
