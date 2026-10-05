using System.Text;
using ClosedXML.Excel;
using NexGenAccountingAI.Infrastructure.Files;
using PdfSharp.Pdf.IO;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class TableExportWriterTests
{
    private static TableExport Sample(int rows = 3) => new(
        "Balancete",
        "Empresa Exemplo, Lda.",
        "500584095",
        new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
        ["Conta", "Descrição", "Débito", "Crédito", "Saldo"],
        [
            .. Enumerable.Range(1, rows).Select(index => (IReadOnlyList<object?>)[$"11{index}", $"Conta número {index}", 1000.5m * index, 0m, 1000.5m * index]),
            ["Subtotal classe 1", null, 6003m, 0m, 6003m],
            // Um texto que no Excel seria uma fórmula perigosa tem de ficar texto.
            ["=HYPERLINK(\"http://exemplo.invalid\")", "Texto que começa por igual", null, null, null],
        ],
        "De 2026-01-01 a 2026-12-31");

    [TestMethod]
    public void XlsxKeepsNumbersAsNumbersAndTextAsText()
    {
        var bytes = TableExportWriter.ToXlsx(Sample());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Balancete");
        Assert.AreEqual("Empresa Exemplo, Lda.", sheet.Cell(1, 1).GetString());
        Assert.AreEqual("Balancete", sheet.Cell(3, 1).GetString());
        StringAssert.Contains(sheet.Cell(4, 1).GetString(), "De 2026-01-01 a 2026-12-31");
        Assert.AreEqual("Débito", sheet.Cell(5, 3).GetString());
        Assert.IsTrue(sheet.Cell(5, 1).Style.Font.Bold);

        Assert.AreEqual(XLDataType.Text, sheet.Cell(6, 1).DataType, "O código da conta é texto, não número.");
        Assert.AreEqual(XLDataType.Number, sheet.Cell(6, 3).DataType);
        Assert.AreEqual(1000.5, sheet.Cell(6, 3).GetDouble());
        Assert.AreEqual("#,##0.00", sheet.Column(3).Style.NumberFormat.Format);

        Assert.IsTrue(sheet.Cell(9, 1).Style.Font.Bold, "A linha de subtotal sai a negrito.");
        var dangerous = sheet.Cell(10, 1);
        Assert.IsFalse(dangerous.HasFormula, "Um texto começado por «=» não pode virar fórmula.");
        Assert.AreEqual(XLDataType.Text, dangerous.DataType);
    }

    [TestMethod]
    public void PdfHasOnePagePerScreenfulAndRepeatsNothingItShouldNot()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var small = TableExportWriter.ToPdf(Sample());
        Assert.AreEqual("%PDF", Encoding.ASCII.GetString(small, 0, 4));
        using (var document = PdfReader.Open(new MemoryStream(small), PdfDocumentOpenMode.Import))
        {
            Assert.AreEqual(1, document.PageCount);
            Assert.AreEqual("Balancete", document.Info.Title);
        }

        using var large = PdfReader.Open(new MemoryStream(TableExportWriter.ToPdf(Sample(400))), PdfDocumentOpenMode.Import);
        Assert.IsGreaterThan(5, large.PageCount, "400 linhas ocupam várias páginas.");
    }

    [TestMethod]
    public void RefusesAPdfThatWouldBeTooLong()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => TableExportWriter.ToPdf(Sample(TableExportWriter.PdfRowLimit + 1)));
        StringAssert.Contains(error.Message, "Excel");
    }
}
