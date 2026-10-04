using System.Text;
using NexGenAccountingAI.Application.Efatura;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class EFaturaCsvReaderTests
{
    private const string Header =
        "Setor;Fornecedor;Tipo;Situação;Número da fatura;Data de emissão;Valor tributável;Valor IVA;Valor total;NIF";

    private static Stream Csv(string text, Encoding? encoding = null) =>
        new MemoryStream((encoding ?? new UTF8Encoding(false)).GetBytes(text));

    [TestMethod]
    public async Task ReadsTheStandardPortalLayout()
    {
        var csv = $"""
            {Header}
            Outros;Fornecedor A;FT;Registada;FT A/1;2026-07-01;100,00;23,00;123,00;500000001
            Outros;Fornecedor B;FS;Registada;FS B/2;2026-07-02;1.234,56;283,95;1.518,51;500000002
            """;

        var result = await new EFaturaCsvReader().ReadAsync(Csv(csv)).ConfigureAwait(false);

        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual(2, result.ValidCount);
        var first = result.Rows[0];
        Assert.AreEqual("500000001", first.SupplierTaxId);
        Assert.AreEqual("Fornecedor A", first.SupplierName);
        Assert.AreEqual("FT A/1", first.InvoiceNo);
        Assert.AreEqual(new DateOnly(2026, 7, 1), first.InvoiceDate);
        Assert.AreEqual("FT", first.InvoiceType);
        Assert.AreEqual(100.00m, first.TaxBase);
        Assert.AreEqual(23.00m, first.TaxAmount);
        Assert.AreEqual(123.00m, first.GrossTotal);
        // Milhares com ponto, decimal com vírgula.
        Assert.AreEqual(1234.56m, result.Rows[1].TaxBase);
        Assert.AreEqual(1518.51m, result.Rows[1].GrossTotal);
    }

    [TestMethod]
    public async Task AcceptsACommaDelimitedFileWithABom()
    {
        var csv = "Emitente,NIF,Tipo,Nº Fatura / ATCUD,Data de Emissão,Base tributável,IVA,Total\n"
            + "Fornecedor C,500000003,FR,\"FR 3 / AJ4-9931\",2026-07-03,10.00,2.30,12.30\n";

        var result = await new EFaturaCsvReader()
            .ReadAsync(Csv(csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            .ConfigureAwait(false);

        var row = result.Rows.Single();
        Assert.AreEqual("500000003", row.SupplierTaxId);
        Assert.AreEqual("FR 3", row.InvoiceNo);
        Assert.AreEqual("AJ4-9931", row.Atcud);
        Assert.AreEqual(10m, row.TaxBase);
        Assert.AreEqual(12.30m, row.GrossTotal);
    }

    [TestMethod]
    public async Task ReadsWindows1252()
    {
        var csv = $"{Header}\nOutros;Fábrica Glória;FT;Registada;FT G/1;2026-07-04;5,00;1,15;6,15;500000004\n";

        var result = await new EFaturaCsvReader()
            .ReadAsync(Csv(csv, Encoding.GetEncoding(1252)))
            .ConfigureAwait(false);

        Assert.AreEqual("Fábrica Glória", result.Rows.Single().SupplierName);
    }

    [TestMethod]
    public async Task ExtractsTheNifFromTheSupplierNameWhenTheColumnIsMissing()
    {
        var csv = "Emitente;Tipo;Numero da fatura;Data de emissao;Valor tributavel;Valor IVA;Valor total\n"
            + "500000005 - Fornecedor E;FT;FT E/1;2026-07-05;20,00;4,60;24,60\n";

        var row = (await new EFaturaCsvReader().ReadAsync(Csv(csv)).ConfigureAwait(false)).Rows.Single();

        Assert.AreEqual("500000005", row.SupplierTaxId);
        Assert.AreEqual("Fornecedor E", row.SupplierName);
        Assert.IsTrue(row.IsValid);
    }

    [TestMethod]
    public async Task FlagsRowsWithoutAnInvoiceNumberOrABadNif()
    {
        var csv = $"""
            {Header}
            Outros;Fornecedor X;FT;Registada;;2026-07-06;1,00;0,23;1,23;500000006
            Outros;Fornecedor Y;FT;Registada;FT Y/1;2026-07-07;1,00;0,23;1,23;12345
            """;

        var result = await new EFaturaCsvReader().ReadAsync(Csv(csv)).ConfigureAwait(false);

        Assert.AreEqual(2, result.InvalidCount);
        StringAssert.Contains(result.Rows[0].Error, "número");
        StringAssert.Contains(result.Rows[1].Error, "NIF");
    }

    [TestMethod]
    public async Task WarnsAtExactlyThreeHundredRecords()
    {
        var builder = new StringBuilder(Header).Append('\n');
        for (var i = 1; i <= 300; i++)
        {
            builder.Append($"Outros;Fornecedor {i};FT;Registada;FT {i};2026-07-01;1,00;0,23;1,23;500000001\n");
        }

        var result = await new EFaturaCsvReader().ReadAsync(Csv(builder.ToString())).ConfigureAwait(false);

        Assert.AreEqual(1, result.Warnings.Count);
        StringAssert.Contains(result.Warnings[0], "300");
    }

    [TestMethod]
    public async Task AnEmptyFileThrows()
    {
        await Assert.ThrowsExactlyAsync<EFaturaCsvFormatException>(() =>
            new EFaturaCsvReader().ReadAsync(Csv(string.Empty))).ConfigureAwait(false);
    }
}
