using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A deteção de anomalias (F4F-01): cada regra, a exclusão do que não conta e a dispensa de achados.</summary>
[TestClass]
public sealed class AnomalyServiceTests
{
    [TestMethod]
    public async Task FlagsDuplicatesRepeatedDocumentsOutliersMissingPartiesAndInvertedBalances()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString("Anomalies"), TestDatabase.Configure)
            .Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);

            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Anomalias", "gabinete-anomalias"));
            var companyId = Guid.NewGuid();
            context.Add(new Company(companyId, firmId, "Empresa Anomalias, Lda.", "500584095", TaxRegion.Mainland));
            var userId = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Contabilista Anomalias", "anomalias@test.invalid"));
            context.Add(new CompanyUser(companyId, userId, CompanyUserRole.Accountant));
            var journalId = Guid.NewGuid();
            context.Add(new AccountingJournal(journalId, companyId, "GERAL", "Diário Geral"));

            var ids = new Dictionary<string, Guid>();
            foreach (var (code, name) in new[] { ("1201", "Banco"), ("211", "Clientes"), ("221", "Fornecedores"), ("6221", "Trabalhos especializados"), ("7211", "Serviços") })
            {
                ids[code] = Guid.NewGuid();
                context.Add(new LedgerAccount(ids[code], companyId, code, name, null, true, null, "S", 1));
            }

            var clientA = Party.Create(companyId, "PT", "500000001", "Cliente A");
            var clientB = Party.Create(companyId, "PT", "500000002", "Cliente B");
            var supplier = Party.Create(companyId, "PT", "500000003", "Fornecedor Z");
            context.AddRange(clientA, clientB, supplier);

            var number = 0;
            JournalEntry Entry(DateOnly date, string document, EntrySource? source, params (string Account, decimal Debit, decimal Credit, Guid? Party)[] lines)
            {
                var reference = $"L-{++number}";
                var entry = new JournalEntry(Guid.NewGuid(), companyId, journalId, reference, date, document, $"Lançamento {reference}");
                foreach (var line in lines)
                {
                    entry.AddLine(Guid.NewGuid(), ids[line.Account], "Linha", line.Debit, line.Credit, line.Party);
                }

                entry.Complete(JournalEntryStatus.Reviewed);
                if (source is { } value)
                {
                    entry.SetSource(value);
                }

                context.Add(entry);
                return entry;
            }

            // Histórico: 12 compras normais de cerca de 100 € em 2025 (a média e o desvio da conta 6221).
            for (var month = 1; month <= 12; month++)
            {
                Entry(new DateOnly(2025, month, 10), $"H-{month}", null, ("6221", 100m + month, 0m, null), ("1201", 0m, 100m + month, null));
            }

            // 2026: duas linhas iguais no mesmo dia e terceiro (duplicado), com 1 500 € (alta).
            Entry(new DateOnly(2026, 3, 5), "FT 1", null, ("211", 1500m, 0m, clientA.Id), ("7211", 0m, 1500m, null));
            Entry(new DateOnly(2026, 3, 5), "FT 1B", null, ("211", 1500m, 0m, clientA.Id), ("7211", 0m, 1500m, null));
            // O mesmo documento do mesmo terceiro em dois lançamentos (documento repetido).
            Entry(new DateOnly(2026, 4, 1), "FA 77", null, ("6221", 40m, 0m, supplier.Id), ("221", 0m, 40m, supplier.Id));
            Entry(new DateOnly(2026, 4, 9), "FA 77", null, ("6221", 41m, 0m, supplier.Id), ("221", 0m, 41m, supplier.Id));
            // Um valor muito acima do habitual na conta 6221.
            Entry(new DateOnly(2026, 5, 2), "FA 900", null, ("6221", 5000m, 0m, null), ("1201", 0m, 5000m, null));
            // Linha de fornecedor sem terceiro.
            Entry(new DateOnly(2026, 5, 15), "FA 55", null, ("6221", 300m, 0m, null), ("221", 0m, 300m, null));
            // Cliente B com saldo credor (recebeu a mais) e fornecedor Z com saldo devedor (pagou a mais).
            Entry(new DateOnly(2026, 6, 1), "RE 1", null, ("1201", 800m, 0m, null), ("211", 0m, 800m, clientB.Id));
            Entry(new DateOnly(2026, 6, 2), "PG 1", null, ("221", 500m, 0m, supplier.Id), ("1201", 0m, 500m, null));
            // O que não conta: anulado, e abertura.
            var voided = Entry(new DateOnly(2026, 7, 1), "ANU", null, ("211", 2000m, 0m, clientA.Id), ("7211", 0m, 2000m, null));
            voided.Void();
            Entry(new DateOnly(2026, 1, 1), "ABERT", EntrySource.Opening, ("211", 777m, 0m, null), ("7211", 0m, 777m, null));
            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var service = new AnomalyService(context);
            var none = CancellationToken.None;
            var report = await service.ScanAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, none).ConfigureAwait(false);
            Assert.IsNotNull(report);
            Assert.IsFalse(report!.Truncated);
            IEnumerable<AnomalyFinding> Of(string rule) => report.Findings.Where(finding => finding.Rule == rule);

            // Duplicado: as duas linhas de 1 500 € do cliente A, ordenadas para o fim da lista de gravidade.
            var duplicate = Of(AnomalyService.Duplicate).Single(finding => finding.Amount == 1500m);
            Assert.AreEqual("high", duplicate.Severity);
            CollectionAssert.AreEqual(new[] { "L-13", "L-14" }, duplicate.EntryReferences.ToArray());
            Assert.AreEqual("Cliente A", duplicate.PartyName);
            Assert.AreEqual("211", duplicate.AccountCode, "Dos dois achados do mesmo par de lançamentos fica o que tem terceiro.");
            Assert.AreEqual(1, Of(AnomalyService.Duplicate).Count());
            Assert.IsFalse(Of(AnomalyService.Duplicate).Any(finding => finding.EntryReferences.Contains("L-21")), "O lançamento anulado não conta.");

            // Documento repetido.
            var repeated = Of(AnomalyService.RepeatedDocument).Single();
            StringAssert.Contains(repeated.Title, "FA 77");
            Assert.AreEqual("Fornecedor Z", repeated.PartyName);
            Assert.AreEqual(2, repeated.EntryReferences.Count);

            // Valor atípico: só o de 5 000 €.
            var outlier = Of(AnomalyService.Outlier).Single();
            Assert.AreEqual(5000m, outlier.Amount);
            Assert.AreEqual("6221", outlier.AccountCode);
            CollectionAssert.AreEqual(new[] { "L-17" }, outlier.EntryReferences.ToArray());

            // Sem terceiro: a linha de 300 € no fornecedor; a de abertura (777 €) não conta.
            var noParty = Of(AnomalyService.NoParty).Single();
            Assert.AreEqual(300m, noParty.Amount);
            Assert.AreEqual("221", noParty.AccountCode);

            // Saldos invertidos: o cliente B (credor) e o fornecedor Z (devedor: pagou 500 € e deve 81 € em faturas, ficou em 419 € devedor).
            var inverted = Of(AnomalyService.InvertedBalance).ToList();
            Assert.AreEqual(2, inverted.Count);
            Assert.AreEqual(800m, inverted.Single(finding => finding.PartyName == "Cliente B").Amount);
            Assert.IsTrue(inverted.Any(finding => finding.PartyName == "Fornecedor Z"));
            Assert.AreEqual(5, report.Rules.Count);
            Assert.IsTrue(report.Rules.All(rule => rule.Count > 0));

            // Um período sem o que foi lançado não traz achados dessas regras.
            var quiet = await service.ScanAsync(userId, companyId, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), false, none).ConfigureAwait(false);
            Assert.AreEqual(0, quiet!.Findings.Count(finding => finding.Rule is AnomalyService.Duplicate or AnomalyService.RepeatedDocument or AnomalyService.Outlier or AnomalyService.NoParty));

            // Dispensar: some da lista, fica nos dispensados, e restaura-se.
            Assert.IsTrue(await service.DismissAsync(userId, companyId, duplicate.Key, "Duas vendas iguais mesmo", none).ConfigureAwait(false));
            Assert.IsTrue(await service.DismissAsync(userId, companyId, duplicate.Key, "repetido", none).ConfigureAwait(false), "Dispensar de novo não falha.");
            var after = await service.ScanAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, none).ConfigureAwait(false);
            Assert.IsFalse(after!.Findings.Any(finding => finding.Key == duplicate.Key));
            Assert.AreEqual(report.Rules.Single(rule => rule.Rule == AnomalyService.Duplicate).Count - 1, after.Rules.Single(rule => rule.Rule == AnomalyService.Duplicate).Count);
            var withDismissed = await service.ScanAsync(userId, companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true, none).ConfigureAwait(false);
            var shown = withDismissed!.Findings.Single(finding => finding.Key == duplicate.Key);
            Assert.IsTrue(shown.Dismissed);
            Assert.AreEqual("Duas vendas iguais mesmo", shown.DismissReason);
            Assert.AreEqual(shown.Key, withDismissed.Findings.Last(finding => finding.Dismissed).Key, "Os dispensados vão para o fim.");
            Assert.IsTrue(await service.RestoreAsync(userId, companyId, duplicate.Key, none).ConfigureAwait(false));
            Assert.IsFalse(await service.RestoreAsync(userId, companyId, duplicate.Key, none).ConfigureAwait(false), "Já não estava dispensado.");

            // Acesso e validação.
            Assert.IsNull(await service.ScanAsync(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, none).ConfigureAwait(false));
            Assert.IsFalse(await service.DismissAsync(Guid.NewGuid(), companyId, "x", null, none).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ScanAsync(userId, companyId, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), false, none)).ConfigureAwait(false);
        }
        finally
        {
            await TestDatabase.DropAsync(context).ConfigureAwait(false);
        }
    }
}
