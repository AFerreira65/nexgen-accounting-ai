using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A entrega da IES (F4C-07), com um webservice simulado no lugar da AT.</summary>
[TestClass]
public sealed class IesFilingServiceTests
{
    [TestMethod]
    public async Task SendsTheAccountantAndTheTaxpayerAndNeverKeepsPasswords()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("IesFiling"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete IES", "gabinete-ies"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa IES, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista IES", "ies@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var gateway = new FakeGateway();
            var statements = new FinancialStatementsService(context);
            var service = new IesFilingService(context, new IesAnnexAService(context, statements), gateway, new FixedUser(userId));
            var none = CancellationToken.None;

            // Sem as credenciais do contabilista não se pede nada à AT.
            var missing = await service.ValidateAsync(userId, companyId, 2026, new IesAtCredentials(string.Empty, string.Empty, null), none).ConfigureAwait(false);
            StringAssert.Contains(missing!.Refusal, "contabilista");
            Assert.AreEqual(0, gateway.Calls);

            // Com plenos poderes: o contabilista com senha e o sujeito passivo só com o NIF.
            var validated = await service.ValidateAsync(userId, companyId, 2026, new IesAtCredentials("123456789/1", "senha-cc", null), none).ConfigureAwait(false);
            Assert.IsTrue(validated!.Result!.Succeeded);
            CollectionAssert.AreEqual(
                new[] { new AtCredential("123456789/1", "senha-cc", AtActor.CertifiedAccountant), new AtCredential("500584095", null, AtActor.Taxpayer) },
                gateway.LastCredentials!.ToArray());
            Assert.IsFalse(gateway.LastProduction);
            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(gateway.LastXml!), "<IES-DA");

            // Sem plenos poderes: o sujeito passivo leva a senha dele.
            await service.SubmitAsync(userId, companyId, 2026, new IesAtCredentials("123456789", "senha-cc", "senha-empresa"), none).ConfigureAwait(false);
            Assert.AreEqual("senha-empresa", gateway.LastCredentials!.Single(item => item.Actor == AtActor.Taxpayer).Password);

            // Fica o registo dos pedidos, sem senhas.
            var rows = await context.AtExchanges.AsNoTracking().Where(item => item.CompanyId == companyId).OrderBy(item => item.RequestedAtUtc).ToListAsync().ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "validarDeclaracao", "submeterDeclaracao" }, rows.Select(row => row.Operation).ToArray());
            Assert.IsTrue(rows.All(row => row.Service == "ies" && row.Period == "2026"));
            Assert.IsFalse(rows.Any(row => row.Message.Contains("senha", StringComparison.OrdinalIgnoreCase) && !row.Message.Contains("Declaração", StringComparison.Ordinal)));

            // Em produção valida-se, mas a submissão está fechada.
            context.Add(CompanyAtCredentials.Create(companyId, "500584095/3", "cifrado:x", VatFilingPeriodicity.Mensal, AtEnvironment.Producao));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var calls = gateway.Calls;
            var blocked = await service.SubmitAsync(userId, companyId, 2026, new IesAtCredentials("123456789", "senha-cc", null), none).ConfigureAwait(false);
            StringAssert.Contains(blocked!.Refusal, "produção");
            Assert.AreEqual(calls, gateway.Calls);

            Assert.IsNull(await service.ValidateAsync(Guid.NewGuid(), companyId, 2026, new IesAtCredentials("1", "2", null), none).ConfigureAwait(false));
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

    private sealed class FakeGateway : IIesAtGateway
    {
        public int Calls { get; private set; }

        public byte[]? LastXml { get; private set; }

        public IReadOnlyList<AtCredential>? LastCredentials { get; private set; }

        public bool LastProduction { get; private set; }

        public AtGatewayStatus Status() => new(true, true, null);

        public Task<AtDeclarationResult> ValidateAsync(
            byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default)
        {
            Record(declarationXml, credentials, production);
            return Task.FromResult(new AtDeclarationResult(true, 0, "Declaração válida.", [], [], null, 200, 40));
        }

        public Task<AtDeclarationResult> SubmitAsync(
            byte[] declarationXml, IReadOnlyList<AtCredential> credentials, bool production, CancellationToken cancellationToken = default)
        {
            Record(declarationXml, credentials, production);
            return Task.FromResult(new AtDeclarationResult(true, 0, "Declaração submetida com sucesso.", [], [], 555L, 200, 90));
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
