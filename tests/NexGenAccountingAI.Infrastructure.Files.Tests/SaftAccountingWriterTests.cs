using System.Text;
using System.Xml.Linq;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftAccountingWriterTests
{
    internal static SaftExportModel SampleModel()
    {
        var date = new DateOnly(2026, 3, 10);
        var created = new DateTime(2026, 3, 11, 9, 30, 0, DateTimeKind.Utc);
        return new SaftExportModel(
            new SaftExportHeader(
                "500584095", "Empresa Açoriana de Exemplo, Lda.", "Desconhecido", "Desconhecido", "Desconhecido", "PT",
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 10, 3), "1.0"),
            "S",
            [
                new("21", "Clientes", 0m, 0m, 123m, 0m, "GR", null, null),
                new("211", "Clientes c/c", 0m, 0m, 123m, 0m, "GA", "21", null),
                new("21111", "Cliente A", 0m, 0m, 123m, 0m, "GM", "211", 10),
                new("72", "Prestações de serviços", 0m, 0m, 0m, 100m, "GR", null, null),
                new("7211", "Serviços — taxa normal", 0m, 0m, 0m, 100m, "GM", "72", 507),
                new("24", "Estado", 0m, 0m, 0m, 23m, "GR", null, null),
                new("24331", "IVA liquidado", 0m, 0m, 0m, 23m, "GM", "24", 73),
            ],
            [
                new(true, "500584095", "21111", "500584095", "Cliente A, Lda.", "PT"),
                new(false, "ESB12345678", "Desconhecido", "B12345678", "Proveedor, S.L.", "ES"),
            ],
            [
                new("VENDAS", "Diário de vendas",
                [
                    new(
                        "2026-03-10 VENDAS VENDAS-2026-00001", 3, date, "António Ferreira", "Fatura de serviços — março",
                        "VENDAS-2026-00001", "N", new DateOnly(2026, 3, 11), "500584095", null,
                        [
                            new("1", "21111", created, "Cliente", 123m, 0m),
                            new("2", "7211", created, "Serviço", 0m, 100m),
                            new("3", "24331", created, "IVA", 0m, 23m),
                        ]),
                ]),
            ]);
    }

    private static async Task<MemoryStream> WriteAsync(SaftExportModel model)
    {
        var stream = new MemoryStream();
        await new SaftAccountingWriter().WriteAsync(model, stream).ConfigureAwait(false);
        stream.Position = 0;
        return stream;
    }

    [TestMethod]
    public async Task WritesWindows1252XmlInSchemaOrder()
    {
        using var stream = await WriteAsync(SampleModel()).ConfigureAwait(false);
        var declaration = Encoding.ASCII.GetString(stream.ToArray(), 0, 60);
        StringAssert.Contains(declaration, "encoding=\"windows-1252\"");

        var document = XDocument.Load(stream);
        XNamespace ns = SaftAccountingWriter.Namespace;
        var root = document.Root!;
        Assert.AreEqual(ns + "AuditFile", root.Name);
        CollectionAssert.AreEqual(
            new[] { "Header", "MasterFiles", "GeneralLedgerEntries" },
            root.Elements().Select(element => element.Name.LocalName).ToArray());

        var header = root.Element(ns + "Header")!;
        Assert.AreEqual("1.04_01", header.Element(ns + "AuditFileVersion")!.Value);
        Assert.AreEqual("C", header.Element(ns + "TaxAccountingBasis")!.Value);
        Assert.AreEqual("Empresa Açoriana de Exemplo, Lda.", header.Element(ns + "CompanyName")!.Value);
        Assert.AreEqual("0", header.Element(ns + "SoftwareCertificateNumber")!.Value);
        Assert.AreEqual(SaftProducer.ProductId, header.Element(ns + "ProductID")!.Value);

        // Clientes antes de fornecedores, como o esquema pede.
        CollectionAssert.AreEqual(
            new[] { "GeneralLedgerAccounts", "Customer", "Supplier" },
            root.Element(ns + "MasterFiles")!.Elements().Select(element => element.Name.LocalName).ToArray());

        var movement = root.Descendants(ns + "Account").Single(account => account.Element(ns + "AccountID")!.Value == "21111");
        Assert.AreEqual("GM", movement.Element(ns + "GroupingCategory")!.Value);
        Assert.AreEqual("211", movement.Element(ns + "GroupingCode")!.Value);
        Assert.AreEqual("10", movement.Element(ns + "TaxonomyCode")!.Value);
        var group = root.Descendants(ns + "Account").Single(account => account.Element(ns + "AccountID")!.Value == "21");
        Assert.IsNull(group.Element(ns + "GroupingCode"));
        Assert.IsNull(group.Element(ns + "TaxonomyCode"));

        var entries = root.Element(ns + "GeneralLedgerEntries")!;
        Assert.AreEqual("1", entries.Element(ns + "NumberOfEntries")!.Value);
        Assert.AreEqual("123.00", entries.Element(ns + "TotalDebit")!.Value);
        Assert.AreEqual("123.00", entries.Element(ns + "TotalCredit")!.Value);
        var transaction = entries.Descendants(ns + "Transaction").Single();
        Assert.AreEqual("500584095", transaction.Element(ns + "CustomerID")!.Value);
        Assert.IsNull(transaction.Element(ns + "SupplierID"));
        Assert.AreEqual("2026-03-11T09:30:00", transaction.Descendants(ns + "SystemEntryDate").First().Value);
        CollectionAssert.AreEqual(
            new[] { "DebitLine", "CreditLine", "CreditLine" },
            transaction.Element(ns + "Lines")!.Elements().Select(element => element.Name.LocalName).ToArray());
    }

    [TestMethod]
    public async Task TheFileIsReadBackByTheImportReaders()
    {
        using var stream = await WriteAsync(SampleModel()).ConfigureAwait(false);

        var accounts = await new SaftAccountsReader().ReadAsync(stream).ConfigureAwait(false);
        Assert.AreEqual(7, accounts.Count);
        Assert.IsTrue(accounts.Any(account => account.Code == "24331"));

        stream.Position = 0;
        var read = await new SaftEntriesReader().ReadTransactionsAsync(stream).ConfigureAwait(false);
        Assert.AreEqual(1, read.DeclaredEntryCount);
        Assert.AreEqual(123m, read.DeclaredTotalDebit);
        var transaction = read.Items.Single();
        Assert.AreEqual("VENDAS", transaction.JournalCode);
        Assert.AreEqual("2026-03-10 VENDAS VENDAS-2026-00001", transaction.TransactionId);
        Assert.AreEqual(new DateOnly(2026, 3, 10), transaction.Date);
        Assert.AreEqual("500584095", transaction.CustomerId);
        Assert.AreEqual(3, transaction.Lines.Count);
        Assert.AreEqual(123m, transaction.Lines.Sum(line => line.Debit));
        Assert.AreEqual(2, read.Parties!.Count);
        Assert.AreEqual("ES", read.Parties.Single(party => party.Kind == SaftPartyKind.Supplier).Country);
    }
}
