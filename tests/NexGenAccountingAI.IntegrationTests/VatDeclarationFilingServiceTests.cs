using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A entrega da declaração periódica de IVA (F4A-02), com um webservice simulado no lugar da AT.</summary>
[TestClass]
public sealed class VatDeclarationFilingServiceTests
{
    [TestMethod]
    public async Task ValidatesAndSubmitsThroughTheGatewayAndKeepsARecord()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("VatFiling"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Entrega", "gabinete-entrega"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Entrega, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista Entrega", "entrega@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var gateway = new FakeGateway();
            var service = new VatDeclarationFilingService(
                context,
                new VatDeclarationService(context, new VatSummaryService(context), new VatReturnService(context)),
                gateway, new PlainProtector(), new FixedUser(userId));
            var request = new VatDeclarationFilingRequest(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, WithinDeadline: true, FiledRecapitulativeStatement: false);
            var none = CancellationToken.None;

            // O ficheiro: nome com o NIF, o ano e o período; válido face ao esquema oficial.
            var file = await service.BuildXmlAsync(userId, companyId, request, none).ConfigureAwait(false);
            Assert.AreEqual("dpiva-500584095-2026-09.xml", file!.Value.FileName);
            var xml = System.Text.Encoding.UTF8.GetString(file.Value.Content);
            StringAssert.Contains(xml, "<periodoDeclaracao>09</periodoDeclaracao>");
            XsdValidation.ValidateIfConfigured(xml, "ACCOUNTINGAI_DPIVA_XSD", VatDeclarationXmlWriter.Namespace, "dpiva-06.xsd");

            // Sem credenciais guardadas, não se pede nada à AT.
            var noCredentials = await service.ValidateAsync(userId, companyId, request, none).ConfigureAwait(false);
            StringAssert.Contains(noCredentials!.Refusal, "credenciais");
            Assert.AreEqual(0, gateway.Calls);

            var credentials = CompanyAtCredentials.Create(companyId, "500584095/3", "cifrado:senha-do-portal", VatFilingPeriodicity.Mensal, AtEnvironment.Testes);
            context.Add(credentials);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Sem certificado ou chave pública, a aplicação explica o que falta.
            gateway.Ready = false;
            var notReady = await service.ValidateAsync(userId, companyId, request, none).ConfigureAwait(false);
            StringAssert.Contains(notReady!.Refusal, "certificado");
            gateway.Ready = true;

            // Validar: vai o ficheiro, com o subutilizador e a senha decifrada, como sujeito passivo, ao ambiente de testes.
            var validated = await service.ValidateAsync(userId, companyId, request, none).ConfigureAwait(false);
            Assert.IsNull(validated!.Refusal);
            Assert.IsTrue(validated.Result!.Succeeded);
            Assert.AreEqual("Testes", validated.Environment);
            Assert.AreEqual(new AtCredential("500584095/3", "senha-do-portal", AtActor.Taxpayer), gateway.LastCredentials!.Single());
            Assert.IsFalse(gateway.LastProduction);
            CollectionAssert.AreEqual(file.Value.Content, gateway.LastXml);

            var submitted = await service.SubmitAsync(userId, companyId, request, acceptAlerts: true, none).ConfigureAwait(false);
            Assert.AreEqual(987654321L, submitted!.Result!.DeclarationId);
            Assert.IsTrue(gateway.LastAcceptAlerts);

            // Fica o registo dos dois pedidos, sem senhas.
            var exchanges = await service.RecentAsync(userId, companyId, 10, none).ConfigureAwait(false);
            Assert.IsNotNull(exchanges);
            CollectionAssert.AreEqual(new[] { "submeterDeclaracao", "validarDeclaracao" }, exchanges.Select(row => row.Operation).ToArray());
            Assert.AreEqual("2026-09", exchanges[0].Period);
            Assert.AreEqual(987654321L, exchanges[0].DeclarationId);
            Assert.AreEqual("Contabilista Entrega", exchanges[0].RequestedByName);
            Assert.IsFalse(exchanges.Any(row => row.Message.Contains("senha-do-portal", StringComparison.Ordinal)));

            // Em produção valida-se, mas a submissão está fechada na aplicação.
            var stored = await context.CompanyAtCredentials.SingleAsync(item => item.CompanyId == companyId).ConfigureAwait(false);
            stored.Update("500584095/3", "cifrado:senha-do-portal", VatFilingPeriodicity.Mensal, AtEnvironment.Producao);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var calls = gateway.Calls;
            var blocked = await service.SubmitAsync(userId, companyId, request, acceptAlerts: false, none).ConfigureAwait(false);
            StringAssert.Contains(blocked!.Refusal, "produção");
            Assert.AreEqual(calls, gateway.Calls, "Nada foi enviado.");
            var productionCheck = await service.ValidateAsync(userId, companyId, request, none).ConfigureAwait(false);
            Assert.IsTrue(gateway.LastProduction);
            Assert.IsNull(productionCheck!.Refusal);

            Assert.IsNull(await service.ValidateAsync(Guid.NewGuid(), companyId, request, none).ConfigureAwait(false), "Sem acesso à empresa.");
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

    private sealed class FakeGateway : IVatReturnAtGateway
    {
        public bool Ready { get; set; } = true;

        public int Calls { get; private set; }

        public byte[]? LastXml { get; private set; }

        public IReadOnlyList<AtCredential>? LastCredentials { get; private set; }

        public bool LastProduction { get; private set; }

        public bool LastAcceptAlerts { get; private set; }

        public AtGatewayStatus Status() => Ready
            ? new AtGatewayStatus(true, true, null)
            : new AtGatewayStatus(false, false, "O certificado da AT não está configurado.");

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
            return Task.FromResult(new AtDeclarationResult(true, 0, "Declaração submetida com sucesso.", [], [], 987654321L, 200, 90));
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
