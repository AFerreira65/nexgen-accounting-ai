using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>O motor de listagens (F4D-01): filtros, agrupamento por níveis, parâmetros, isolamento e alcance.</summary>
[TestClass]
public sealed class ListingEngineTests
{
    private static readonly DateOnly Today = new(2026, 6, 30);

    [TestMethod]
    public void ValidatesDefinitionsAsDataOnly()
    {
        Assert.AreEqual(0, ListingCatalog.Validate(new ListingDefinition("linhas", ["conta", "debito"])).Count);
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition("outra", ["conta"])).Count > 0, "Origem desconhecida.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition("linhas", [])).Count > 0, "Sem colunas.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition("linhas", ["conta; drop table"])).Count > 0, "Coluna inventada.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition("linhas", ["conta"], GroupBy: ["descricao"])).Count > 0, "Não se agrupa por texto livre.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition("linhas", ["conta"], GroupBy: ["conta", "classe", "ano", "mes"])).Count > 0, "Níveis a mais.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition(
            "linhas", ["conta"], new ListingFilterGroup("and", [new ListingCondition("debito", "contains", "1")]))).Count > 0, "Operador de texto num valor.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition(
            "linhas", ["conta"], new ListingFilterGroup("and", [new ListingCondition("data", "ge", "@inicio")]))).Count > 0, "Parâmetro não definido.");
        Assert.IsTrue(ListingCatalog.Validate(new ListingDefinition(
            "linhas", ["conta"], new ListingFilterGroup("xor", [new ListingCondition("conta", "eq", "1")]))).Count > 0, "Ligação desconhecida.");
        Assert.AreEqual(new DateOnly(2026, 5, 31), ListingParameters.ResolveDate("fim-mes-anterior", Today));
        Assert.AreEqual(new DateOnly(2025, 1, 1), ListingParameters.ResolveDate("inicio-ano-anterior", Today));
        Assert.IsNull(ListingParameters.ResolveDate("amanhã", Today));
    }

    [TestMethod]
    public async Task RunsFiltersGroupsParametersAndKeepsCompaniesApart()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("Listings"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Listagens", "gabinete-listagens"));
            var companyId = Guid.NewGuid();
            var otherCompanyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Listagens, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(otherCompanyId, firmId, "Outra Empresa, Lda.", "501964843", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            var colleagueId = Guid.NewGuid();
            var strangerId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista Listagens", "listagens@test.invalid"));
            context.Add(new UserAccount(colleagueId, "Colega Listagens", "colega@test.invalid"));
            context.Add(new UserAccount(strangerId, "Estranho Listagens", "estranho@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(companyId, colleagueId, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(otherCompanyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            var otherJournalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));
            context.Add(new AccountingJournal(otherJournalId, otherCompanyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            var otherIds = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("1201", "Banco"), ("6221", "Trabalhos especializados"), ("7211", "Serviços") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", 1));
                otherIds[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(otherIds[code], otherCompanyId, code, name, null, true, null, "S", 1));
            }

            var clientA = Party.Create(companyId, "PT", "500000001", "Cliente A");
            var clientB = Party.Create(companyId, "PT", "500000002", "Cliente B");
            var supplier = Party.Create(companyId, "PT", "500000003", "Água & Luz_100%");
            context.AddRange(clientA, clientB, supplier);

            var number = 0;
            JournalEntry Entry(Guid company, Guid journal, Dictionary<string, Guid> accounts, DateOnly date, params (string Account, decimal Debit, decimal Credit, Guid? Party)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), company, journal, reference, date, reference, $"Lançamento {reference}");
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), accounts[line.Account], "Linha " + line.Account, line.Debit, line.Credit, line.Party);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                context.Add(entry);
                return entry;
            }

            Entry(companyId, journalId, ids, new DateOnly(2026, 1, 15), ("1201", 1000m, 0m, null), ("7211", 0m, 1000m, clientA.Id));
            Entry(companyId, journalId, ids, new DateOnly(2026, 2, 10), ("1201", 500m, 0m, null), ("7211", 0m, 500m, clientB.Id));
            Entry(companyId, journalId, ids, new DateOnly(2026, 2, 20), ("6221", 300m, 0m, supplier.Id), ("1201", 0m, 300m, null));
            var voided = Entry(companyId, journalId, ids, new DateOnly(2026, 3, 5), ("1201", 9999m, 0m, null), ("7211", 0m, 9999m, null));
            voided.Void();
            Entry(otherCompanyId, otherJournalId, otherIds, new DateOnly(2026, 2, 12), ("1201", 77777m, 0m, null), ("7211", 0m, 77777m, null));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var engine = new ListingEngine(context);
            var none = CancellationToken.None;
            Task<ListingResult> Run(ListingDefinition definition, ListingRequest? request = null) =>
                engine.RunAsync(companyId, definition, request ?? new ListingRequest(), Today, none);

            // Sem filtros: as seis linhas da empresa; os anulados e a outra empresa ficam de fora.
            var all = await Run(new ListingDefinition("linhas", ["data", "lancamento", "conta", "terceiro", "debito", "credito", "saldo"])).ConfigureAwait(false);
            Assert.AreEqual("rows", all.Kind);
            Assert.AreEqual(6, all.TotalRows);
            Assert.AreEqual(1800m, all.Totals["debito"]);
            Assert.AreEqual(1800m, all.Totals["credito"]);
            Assert.AreEqual(0m, all.Totals["saldo"]);
            Assert.AreEqual("2026-01-15", all.Rows[0].Cells[0], "A ordem por omissão é pela data.");
            Assert.AreEqual("L-1", all.Rows[0].Link.EntryReference);
            Assert.AreEqual(clientA.Id, all.Rows.Single(row => row.Cells[3] as string == "Cliente A").Link.PartyId, "Cada linha liga ao terceiro.");

            // E: serviços (conta a começar por 72) em fevereiro.
            var february = await Run(new ListingDefinition("linhas", ["lancamento", "credito"], new ListingFilterGroup("and",
            [
                new ListingCondition("conta", "starts", "72"),
                new ListingCondition("data", "between", "2026-02-01", "2026-02-28"),
            ]))).ConfigureAwait(false);
            Assert.AreEqual(1, february.TotalRows);
            Assert.AreEqual(500m, february.Rows.Single().Cells[1]);

            // OU com um grupo: o terceiro contém «cliente a» (sem distinguir maiúsculas) ou o valor é superior a 400 no débito.
            var either = await Run(new ListingDefinition("linhas", ["lancamento"], new ListingFilterGroup("or",
                [new ListingCondition("terceiro", "contains", "CLIENTE a")],
                [new ListingFilterGroup("and", [new ListingCondition("debito", "gt", "400"), new ListingCondition("conta", "eq", "1201")])]))).ConfigureAwait(false);
            Assert.AreEqual(3, either.TotalRows, "A linha do cliente A e as duas linhas de banco com mais de 400 a débito.");

            // O padrão não é interpretado: «%» e «_» valem como texto.
            Task<int> Count(string op, string value) => Run(new ListingDefinition(
                "linhas", ["lancamento"], new ListingFilterGroup("and", [new ListingCondition("terceiro", op, value)]))).ContinueWith(task => task.Result.TotalRows, TaskScheduler.Default);
            Assert.AreEqual(1, await Count("contains", "z_100%").ConfigureAwait(false));
            Assert.AreEqual(0, await Count("contains", "z%100").ConfigureAwait(false), "O % não é um caracter de padrão.");
            Assert.AreEqual(1, await Count("contains", "_").ConfigureAwait(false), "Só há um terceiro com sublinhado.");
            Assert.AreEqual(0, await Count("contains", "'; drop table \"JournalEntryLine\"; --").ConfigureAwait(false));
            Assert.AreEqual(3, await Count("notempty", string.Empty).ConfigureAwait(false));
            Assert.AreEqual(3, await Count("empty", string.Empty).ConfigureAwait(false));

            // Parâmetros: o valor por omissão é relativo à data de hoje; o pedido pode trocá-lo.
            var parameterised = new ListingDefinition(
                "linhas", ["lancamento"], new ListingFilterGroup("and", [new ListingCondition("data", "ge", "@inicio")]), Parameters: [new ListingParameter("inicio", "Desde", "date", "inicio-ano")]);
            Assert.AreEqual(6, (await Run(parameterised).ConfigureAwait(false)).TotalRows);
            Assert.AreEqual(4, (await Run(parameterised, new ListingRequest(new Dictionary<string, string> { ["inicio"] = "2026-02-01" })).ConfigureAwait(false)).TotalRows);
            var withoutDefault = parameterised with { Parameters = [new ListingParameter("inicio", "Desde", "date")] };
            Assert.ThrowsExactly<ArgumentException>(() => Run(withoutDefault).GetAwaiter().GetResult());

            // Agrupar por classe e depois por conta: um nível de cada vez.
            var grouped = new ListingDefinition("linhas", ["data", "conta", "debito", "credito"], GroupBy: ["classe", "conta"]);
            var classes = await Run(grouped).ConfigureAwait(false);
            Assert.AreEqual("groups", classes.Kind);
            CollectionAssert.AreEqual(new[] { "1", "6", "7" }, classes.Groups.Select(group => group.Key).ToArray());
            Assert.AreEqual(1500m, classes.Groups.Single(group => group.Key == "1").Sums["debito"], "Classe 1: 1 500 a débito e 300 a crédito.");
            Assert.AreEqual(300m, classes.Groups.Single(group => group.Key == "1").Sums["credito"]);
            Assert.AreEqual(3, classes.Groups.Single(group => group.Key == "1").Count);
            Assert.AreEqual(2, classes.Groups.Single(group => group.Key == "7").Count);
            Assert.AreEqual(6, classes.TotalRows);
            var accounts = await Run(grouped, new ListingRequest(GroupPath: ["7"])).ConfigureAwait(false);
            Assert.AreEqual("groups", accounts.Kind);
            Assert.AreEqual("7211", accounts.Groups.Single().Key);
            Assert.AreEqual(1500m, accounts.Groups.Single().Sums["credito"]);
            var lines = await Run(grouped, new ListingRequest(GroupPath: ["7", "7211"])).ConfigureAwait(false);
            Assert.AreEqual("rows", lines.Kind);
            Assert.AreEqual(2, lines.Rows.Count);
            Assert.ThrowsExactly<ArgumentException>(() => Run(grouped, new ListingRequest(GroupPath: ["7", "7211", "x"])).GetAwaiter().GetResult());

            // Grupos sem valor aparecem à parte, com a etiqueta própria; os meses ordenam-se como números.
            var byParty = await Run(new ListingDefinition("linhas", ["conta"], GroupBy: ["terceiro"])).ConfigureAwait(false);
            Assert.IsTrue(byParty.Groups.Last().Key is null && byParty.Groups.Last().Label == "(sem valor)");
            var byMonth = await Run(new ListingDefinition("linhas", ["conta"], GroupBy: ["ano", "mes"]), new ListingRequest(GroupPath: ["2026"])).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "1", "2" }, byMonth.Groups.Select(group => group.Key).ToArray());

            // Os anulados só aparecem se a listagem filtrar pelo estado.
            var cancelled = await Run(new ListingDefinition("linhas", ["lancamento", "debito"], new ListingFilterGroup("and", [new ListingCondition("estado", "eq", "anulado")]))).ConfigureAwait(false);
            Assert.AreEqual(2, cancelled.TotalRows);
            Assert.AreEqual(9999m, cancelled.Totals["debito"]);

            // Ordenar do maior para o menor e paginar.
            var sorted = new ListingDefinition("linhas", ["debito"], Sort: [new ListingSort("debito", Descending: true)]);
            var firstPage = await Run(sorted, new ListingRequest(Skip: 0, Take: 2)).ConfigureAwait(false);
            CollectionAssert.AreEqual(new object[] { 1000m, 500m }, firstPage.Rows.Select(row => row.Cells[0]).ToArray());
            Assert.AreEqual(6, firstPage.TotalRows);
            var secondPage = await Run(sorted, new ListingRequest(Skip: 2, Take: 2)).ConfigureAwait(false);
            CollectionAssert.AreEqual(new object[] { 300m, 0m }, secondPage.Rows.Select(row => row.Cells[0]).ToArray());

            // Exportar: todas as linhas, com os campos de grupo primeiro na ordenação.
            var export = await engine.ExportAsync(companyId, grouped, null, Today, none).ConfigureAwait(false);
            Assert.AreEqual(6, export.Rows.Count);
            Assert.IsFalse(export.Truncated);
            CollectionAssert.AreEqual(new[] { "1201", "1201", "1201", "6221", "7211", "7211" }, export.Rows.Select(row => (string?)row.Cells[1]).ToArray());

            // As listagens guardadas: privada só do dono; da empresa para quem tem acesso; do gabinete nas outras empresas.
            var service = new ListingService(context, engine, TimeProvider.System);
            var definition = new ListingDefinition("linhas", ["conta", "debito"]);
            Assert.IsNull(await service.ListAsync(strangerId, companyId, none).ConfigureAwait(false), "Sem acesso à empresa.");
            Assert.IsNull(await service.RunAsync(strangerId, companyId, definition, new ListingRequest(), none).ConfigureAwait(false));
            Assert.IsTrue((await service.CreateAsync(userId, companyId, "private", "Minha", null, definition, none).ConfigureAwait(false)).Saved is not null);
            Assert.IsTrue((await service.CreateAsync(userId, companyId, "company", "Da empresa", null, definition, none).ConfigureAwait(false)).Saved is not null);
            Assert.IsTrue((await service.CreateAsync(userId, companyId, "firm", "Do gabinete", null, definition, none).ConfigureAwait(false)).Saved is not null);
            Assert.IsTrue((await service.CreateAsync(userId, companyId, "private", "minha", null, definition, none).ConfigureAwait(false)).Errors.Count > 0, "O nome repete-se sem distinguir maiúsculas.");
            Assert.IsTrue((await service.CreateAsync(userId, companyId, "private", "Inválida", null, new ListingDefinition("linhas", []), none).ConfigureAwait(false)).Errors.Count > 0);
            Assert.IsTrue((await service.CreateAsync(userId, companyId, "mundo", "Alcance", null, definition, none).ConfigureAwait(false)).Errors.Count > 0);
            CollectionAssert.AreEquivalent(new[] { "Minha", "Da empresa", "Do gabinete" }, (await service.ListAsync(userId, companyId, none).ConfigureAwait(false))!.Select(item => item.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { "Da empresa", "Do gabinete" }, (await service.ListAsync(colleagueId, companyId, none).ConfigureAwait(false))!.Select(item => item.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { "Do gabinete" }, (await service.ListAsync(userId, otherCompanyId, none).ConfigureAwait(false))!.Select(item => item.Name).ToArray());

            var theirs = (await service.ListAsync(colleagueId, companyId, none).ConfigureAwait(false))!;
            var companyOne = theirs.Single(item => item.Name == "Da empresa");
            Assert.IsFalse(companyOne.Mine);
            Assert.IsTrue((await service.UpdateAsync(colleagueId, companyId, companyOne.Id, "company", "Da empresa (revista)", null, definition, none).ConfigureAwait(false)).Saved is not null, "Qualquer membro altera a da empresa.");
            Assert.IsTrue((await service.UpdateAsync(colleagueId, companyId, companyOne.Id, "private", "Da empresa (revista)", null, definition, none).ConfigureAwait(false)).Errors.Count > 0, "Só o dono muda o alcance.");
            var firmOne = theirs.Single(item => item.Name == "Do gabinete");
            Assert.IsTrue((await service.UpdateAsync(colleagueId, companyId, firmOne.Id, "firm", "Roubada", null, definition, none).ConfigureAwait(false)).NotFound, "A do gabinete é só do dono.");
            Assert.IsFalse(await service.DeleteAsync(colleagueId, companyId, firmOne.Id, none).ConfigureAwait(false));
            Assert.IsTrue(await service.DeleteAsync(userId, companyId, firmOne.Id, none).ConfigureAwait(false));

            // O resultado de uma empresa nunca traz as linhas da outra.
            var other = await engine.RunAsync(otherCompanyId, new ListingDefinition("linhas", ["debito"]), new ListingRequest(), Today, none).ConfigureAwait(false);
            Assert.AreEqual(2, other.TotalRows);
            Assert.AreEqual(77777m, other.Totals["debito"]);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
