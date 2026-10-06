using NexGenAccountingAI.Application.Maps;
using PdfSharp.Pdf.IO;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class ReportPackWriterTests
{
    private static ReportPack Pack(int tableRows = 3) => new(
        "Relatório económico-financeiro",
        "Elementos para apreciação de crédito",
        "Empresa de Teste, Lda.",
        "500000000",
        "Exercício de 2026",
        new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero),
        [
            new("Apresentação", ["Primeiro parágrafo da apresentação.", "Segundo parágrafo, com números: 1 234,50 €."]),
            new("Balanço", [], new ReportPackTable(
                ["Rubrica", "Exercício", "Exercício anterior"],
                Enumerable.Range(1, tableRows).Select(index => new ReportPackRow([$"Rubrica {index}", "1 000,00", "900,00"], index % 2, index == tableRows)).ToList(),
                [false, true, true]),
                ["Nota ao balanço."]),
        ],
        "Gabinete de Contabilidade",
        "Texto gerado por IA; não substitui as peças oficiais.");

    [TestMethod]
    public void RendersACoverAndTheSections()
    {
        var bytes = ReportPackWriter.ToPdf(Pack());

        Assert.IsTrue(bytes.Length > 1000);
        Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.AreEqual(2, document.PageCount, "A capa numa página, as secções na seguinte.");
        Assert.AreEqual("Relatório económico-financeiro", document.Info.Title);
        Assert.AreEqual("Empresa de Teste, Lda.", document.Info.Author);
    }

    [TestMethod]
    public void RefusesATableBeyondTheLimit()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => ReportPackWriter.ToPdf(Pack(ReportPackWriter.TableRowLimit + 1)));
        Assert.IsTrue(exception.Message.Contains("Balanço", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheBankPresetHasTheExpectedSectionsAndIsReadBeforeClosing()
    {
        var request = ReportPackPresets.Build(ReportPackPresets.Bank, 2026, 1, 6);

        Assert.IsNotNull(request);
        Assert.IsFalse(request.IncludeClosing);
        Assert.AreEqual(6, request.ToMonth);
        CollectionAssert.AreEqual(new[] { "texto", "mapa", "mapa", "mapa", "racios", "balancete" }, request.Sections.Select(section => section.Kind).ToArray());
        Assert.IsTrue(request.Sections.Where(section => section.Kind == "mapa").All(section => MapTemplates.Find(section.TemplateKey) is not null));
        Assert.IsNull(ReportPackPresets.Build("outro", 2026, 1, 12));
    }
}
