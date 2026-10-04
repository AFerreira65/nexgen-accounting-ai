using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class RecapitulativeStatementServiceTests
{
    [TestMethod]
    public async Task GroupsIntraCommunityOperationsByAcquirerAndType()
    {
        var connectionString = TestDatabase.ConnectionString("Recapitulative");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString, TestDatabase.Configure)
            .Options;

        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Recapitulativa", "gabinete-recapitulativa"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Recapitulativa, Lda.", "DR-PT-0001", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Utilizador Recapitulativa", "dr@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("1201", "Depósito à ordem"), ("2111", "Clientes"), ("7111", "Vendas"), ("7211", "Serviços") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name));
            }

            var goods = VatCode.Create(companyId, "V-TIB", "Transmissões intracomunitárias", VatRate.Isento, 0m, VatOperationNature.TransmissoesIntracomunitariasBens, VatRegion.Continente);
            var services = VatCode.Create(companyId, "S-PSI", "Serviços intracomunitários", VatRate.Isento, 0m, VatOperationNature.PrestacoesServicosIntracomunitarias, VatRegion.Continente);
            var triangular = VatCode.Create(companyId, "V-TRI", "Triangulares", VatRate.Isento, 0m, VatOperationNature.OperacoesTriangulares, VatRegion.Continente);
            context.AddRange(goods, services, triangular);

            var spanish = Party.Create(companyId, "ES", "ESB12345678", "Cliente Espanhol, S.L.");
            var greek = Party.Create(companyId, "GR", "123456789", "Cliente Grego");
            var american = Party.Create(companyId, "US", "99-9999999", "Cliente Americano, Inc.");
            context.AddRange(spanish, greek, american);

            var number = 0;
            void Entry(params (string Account, decimal Debit, decimal Credit, Party? Party, VatCode? Code)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, new DateOnly(2026, 2, 10), reference, $"Lançamento {reference}");
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, line.Party?.Id, line.Code?.Id);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
            }

            // Duas vendas de bens ao mesmo cliente espanhol (o terceiro está na linha do cliente) e uma nota de crédito.
            Entry(("2111", 1000.40m, 0m, spanish, null), ("7111", 0m, 1000.40m, null, goods));
            Entry(("2111", 500.20m, 0m, spanish, null), ("7111", 0m, 500.20m, spanish, goods));
            Entry(("7111", 100.60m, 0m, null, goods), ("2111", 0m, 100.60m, spanish, null));
            // Serviço a um cliente grego e uma operação triangular com o espanhol.
            Entry(("2111", 300m, 0m, greek, null), ("7211", 0m, 300m, null, services));
            Entry(("2111", 200m, 0m, spanish, null), ("7111", 0m, 200m, null, triangular));
            // Sem terceiro, e com um adquirente de fora da União: ficam de fora.
            Entry(("1201", 50m, 0m, null, null), ("7111", 0m, 50m, null, goods));
            Entry(("2111", 70m, 0m, american, null), ("7111", 0m, 70m, null, goods));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var from = new DateOnly(2026, 2, 1);
            var to = new DateOnly(2026, 2, 28);
            var statement = await new RecapitulativeStatementService(context)
                .BuildAsync(userId, companyId, from, to, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsNotNull(statement);
            Assert.AreEqual("02", statement!.Period);
            Assert.AreEqual(3, statement.Lines.Count);

            // A Grécia usa o prefixo EL; o prefixo do país sai do número.
            var greekLine = statement.Lines[0];
            Assert.AreEqual(("EL", "123456789", 5, 300m), (greekLine.Prefix, greekLine.TaxNumber, greekLine.OperationType, greekLine.Value));
            var goodsLine = statement.Lines[1];
            Assert.AreEqual(("ES", "B12345678", 1), (goodsLine.Prefix, goodsLine.TaxNumber, goodsLine.OperationType));
            Assert.AreEqual(1400.00m, goodsLine.ExactValue, "As duas vendas menos a nota de crédito.");
            Assert.AreEqual(1400m, goodsLine.Value);
            var triangularLine = statement.Lines[2];
            Assert.AreEqual((4, 200m), (triangularLine.OperationType, triangularLine.Value));

            Assert.AreEqual(1400m, statement.Field10);
            Assert.AreEqual(200m, statement.Field17);
            Assert.AreEqual(300m, statement.Field18);
            Assert.AreEqual(1700m, statement.Field19, "As operações triangulares não contam para o campo 7 da declaração periódica.");

            Assert.AreEqual(2, statement.Issues.Count);
            StringAssert.Contains(statement.Issues[0].Reason, "não tem terceiro");
            Assert.AreEqual(50m, statement.Issues[0].Amount);
            StringAssert.Contains(statement.Issues[1].Reason, "não é outro Estado-Membro");

            // Na declaração periódica: bens e serviços no campo 7, triangulares no campo 8.
            var declaration = await new VatDeclarationService(context, new VatSummaryService(context), new VatReturnService(context))
                .BuildAsync(userId, companyId, from, to, null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual(1820m, declaration!.Fields.Single(field => field.Number == 7).Amount, "Inclui os 120 que a recapitulativa deixou de fora.");
            Assert.AreEqual(200m, declaration.Fields.Single(field => field.Number == 8).Amount);
            Assert.IsTrue(declaration.Warnings.Any(warning => warning.Contains("declaração recapitulativa", StringComparison.Ordinal)));

            Assert.IsNull(await new RecapitulativeStatementService(context)
                .BuildAsync(Guid.NewGuid(), companyId, from, to, CancellationToken.None)
                .ConfigureAwait(false));
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
