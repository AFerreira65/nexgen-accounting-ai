using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Application.Efatura;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A importação das compras do e-Fatura pelo webservice da AT, com a AT simulada.</summary>
[TestClass]
public sealed class EFaturaAtImportServiceTests
{
    [TestMethod]
    public async Task ImportsFromTheAtIdempotentlyAndNeverKeepsPasswords()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("EFaturaAt"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete e-Fatura", "gabinete-efatura"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa e-Fatura, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista", "efatura@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var gateway = new FakeGateway();
            var service = new EFaturaAtImportService(
                context, new EFaturaPurchaseImportService(new NoCsv(), context), gateway, new PrefixProtector(), new FixedUser(userId));
            var none = CancellationToken.None;
            var from = new DateOnly(2026, 9, 1);
            var to = new DateOnly(2026, 9, 30);

            // Sem credenciais não se pede nada à AT.
            var missing = await service.ImportAsync(userId, companyId, from, to, false, none).ConfigureAwait(false);
            StringAssert.Contains(missing!.Problem, "credenciais");
            Assert.AreEqual(0, gateway.Calls);

            // Um subutilizador de outro NIF seria recusado pela AT (414): nem se tenta.
            context.Add(CompanyAtCredentials.Create(companyId, "123456789/1", "enc:v1:senha-wfa", VatFilingPeriodicity.Mensal, AtEnvironment.Testes));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var wrongTaxNumber = await service.ImportAsync(userId, companyId, from, to, false, none).ConfigureAwait(false);
            StringAssert.Contains(wrongTaxNumber!.Problem, "123456789");
            Assert.AreEqual(0, gateway.Calls);

            var stored = await context.CompanyAtCredentials.SingleAsync(item => item.CompanyId == companyId).ConfigureAwait(false);
            stored.Update("500584095/1", "enc:v1:senha-wfa", VatFilingPeriodicity.Mensal, AtEnvironment.Testes);
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            // Datas inválidas e períodos demasiado longos.
            StringAssert.Contains((await service.ImportAsync(userId, companyId, to, from, false, none).ConfigureAwait(false))!.Problem, "anterior");
            StringAssert.Contains(
                (await service.ImportAsync(userId, companyId, from, from.AddDays(500), false, none).ConfigureAwait(false))!.Problem, "400");
            Assert.AreEqual(0, gateway.Calls);

            // O nome do emitente que a AT não dá vem do cadastro de terceiros; senão fica o NIF.
            context.Add(Party.Create(companyId, "PT", "509999999", "Fornecedor Conhecido, S.A."));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            gateway.Result = new EFaturaAtFetchResult(
                true, 200, "2 documento(s).",
                [
                    new EFaturaAtInvoice("509999999", "FT 2026/15", "JJ1234-15", new DateOnly(2026, 9, 12), "FT", "C", 100m, 23m, 123m),
                    new EFaturaAtInvoice("500888888", "FS 9/1", null, new DateOnly(2026, 9, 30), "FS", null, 12.5m, 0m, 12.5m),
                ],
                [], 200, 80);

            var preview = await service.ImportAsync(userId, companyId, from, to, true, none).ConfigureAwait(false);
            Assert.IsNull(preview!.Problem);
            Assert.AreEqual(2, preview.Fetched);
            Assert.AreEqual(2, preview.Import!.Imported);
            Assert.IsNull(preview.Import.BatchId, "Pré-visualização não grava.");
            Assert.AreEqual(0, await context.EFaturaPurchases.CountAsync().ConfigureAwait(false));
            Assert.AreEqual("Testes", preview.Environment);

            var imported = await service.ImportAsync(userId, companyId, from, to, false, none).ConfigureAwait(false);
            Assert.AreEqual(2, imported!.Import!.Imported);
            Assert.AreEqual(0, imported.Import.AlreadyExisted);
            Assert.AreEqual(new AtCredential("500584095/1", "senha-wfa", AtActor.Taxpayer), gateway.LastCredential);
            Assert.AreEqual("500584095", gateway.LastCustomerTaxId);
            Assert.AreEqual(from, gateway.LastFrom);
            Assert.AreEqual(to, gateway.LastTo);
            Assert.IsFalse(gateway.LastProduction);

            var rows = await context.EFaturaPurchases.AsNoTracking().OrderBy(item => item.InvoiceNo).ToListAsync().ConfigureAwait(false);
            var invoice = rows.Single(item => item.SupplierTaxId == "509999999");
            Assert.AreEqual("Fornecedor Conhecido, S.A.", invoice.SupplierName);
            Assert.AreEqual(100m, invoice.TaxBase);
            Assert.AreEqual(23m, invoice.TaxAmount);
            Assert.AreEqual(123m, invoice.GrossTotal);
            Assert.AreEqual("FT", invoice.InvoiceType);
            Assert.AreEqual("JJ1234-15", invoice.Atcud);
            Assert.AreEqual(EFaturaAtImportService.SourceName, invoice.SourceFileName);
            Assert.AreEqual("NIF 500888888", rows.Single(item => item.SupplierTaxId == "500888888").SupplierName);

            // Repetir não duplica.
            var again = await service.ImportAsync(userId, companyId, from, to, false, none).ConfigureAwait(false);
            Assert.AreEqual(0, again!.Import!.Imported);
            Assert.AreEqual(2, again.Import.AlreadyExisted);
            Assert.AreEqual(2, await context.EFaturaPurchases.CountAsync().ConfigureAwait(false));

            // Uma recusa da AT não grava nada e traz a mensagem dela.
            gateway.Result = new EFaturaAtFetchResult(true, 414, "A AT recusou a consulta (414): o nif não corresponde ao de login.", [], [], 200, 20);
            var refused = await service.ImportAsync(userId, companyId, from, to, false, none).ConfigureAwait(false);
            StringAssert.Contains(refused!.Problem, "414");
            Assert.IsTrue(refused.ReachedAt);
            Assert.IsNull(refused.Import);

            // Fica o registo de cada pedido à AT, sem senhas.
            var exchanges = await context.AtExchanges.AsNoTracking().Where(item => item.CompanyId == companyId)
                .OrderBy(item => item.RequestedAtUtc).ToListAsync().ConfigureAwait(false);
            Assert.HasCount(4, exchanges);
            Assert.IsTrue(exchanges.All(item => item.Service == "efatura" && item.Operation == "consultarFaturas" && item.Period == "20260901-20260930"));
            Assert.AreEqual(414, exchanges[^1].ResultCode);
            Assert.IsFalse(exchanges.Any(item => item.Message.Contains("senha-wfa", StringComparison.Ordinal)));

            // Sem acesso à empresa.
            Assert.IsNull(await service.ImportAsync(Guid.NewGuid(), companyId, from, to, false, none).ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class NoCsv : IEFaturaCsvReader
    {
        public Task<EFaturaCsvResult> ReadAsync(Stream content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedUser(Guid userId) : ICurrentUserAccessor
    {
        public Guid? UserId { get; set; } = userId;
    }

    private sealed class PrefixProtector : IAtSecretProtector
    {
        private const string Prefix = "enc:v1:";

        public bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => Prefix + plaintext;

        public string Unprotect(string? value) =>
            IsProtected(value) ? value![Prefix.Length..] : value ?? string.Empty;
    }

    private sealed class FakeGateway : IEFaturaAtGateway
    {
        public int Calls { get; private set; }

        public EFaturaAtFetchResult Result { get; set; } = new(true, 200, string.Empty, [], [], 200, 1);

        public AtCredential? LastCredential { get; private set; }

        public string? LastCustomerTaxId { get; private set; }

        public DateOnly LastFrom { get; private set; }

        public DateOnly LastTo { get; private set; }

        public bool LastProduction { get; private set; }

        public AtGatewayStatus Status() => new(true, true, null);

        public Task<EFaturaAtFetchResult> FetchPurchasesAsync(
            DateOnly from, DateOnly to, AtCredential credential, string customerTaxId, bool production, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastFrom = from;
            LastTo = to;
            LastCredential = credential;
            LastCustomerTaxId = customerTaxId;
            LastProduction = production;
            return Task.FromResult(Result);
        }
    }
}
