using System.Text;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftHeaderReaderTests
{
    private const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    private static string SaftDocument(
        string basis = "I",
        string nif = "500000000",
        bool withNamespace = true,
        string version = "1.04_01") =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile{(withNamespace ? $" xmlns=\"{Namespace}\"" : string.Empty)}>
          <Header>
            <AuditFileVersion>{version}</AuditFileVersion>
            <CompanyID>Sintetica {nif}</CompanyID>
            <TaxRegistrationNumber>{nif}</TaxRegistrationNumber>
            <TaxAccountingBasis>{basis}</TaxAccountingBasis>
            <CompanyName>Empresa Sintética, Lda.</CompanyName>
            <BusinessName>Sintética</BusinessName>
            <FiscalYear>2026</FiscalYear>
            <StartDate>2026-01-01</StartDate>
            <EndDate>2026-12-31</EndDate>
            <CurrencyCode>EUR</CurrencyCode>
            <DateCreated>2026-08-30</DateCreated>
            <TaxEntity>Sede</TaxEntity>
            <ProductID>NexGen Accounting AI/NexGenAccountingAI</ProductID>
          </Header>
          <MasterFiles />
        </AuditFile>
        """;

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [TestMethod]
    public async Task ReadsAWellFormedAccountingHeader()
    {
        var header = await new SaftHeaderReader().ReadAsync(Utf8(SaftDocument())).ConfigureAwait(false);

        Assert.AreEqual("1.04_01", header.AuditFileVersion);
        Assert.AreEqual("500000000", header.TaxRegistrationNumber);
        Assert.AreEqual("I", header.TaxAccountingBasis);
        Assert.AreEqual("Empresa Sintética, Lda.", header.CompanyName);
        Assert.AreEqual("Sintética", header.BusinessName);
        Assert.AreEqual(2026, header.FiscalYear);
        Assert.AreEqual(new DateOnly(2026, 1, 1), header.StartDate);
        Assert.AreEqual(new DateOnly(2026, 12, 31), header.EndDate);
        Assert.AreEqual("EUR", header.CurrencyCode);
        Assert.AreEqual(new DateOnly(2026, 8, 30), header.DateCreated);
        Assert.IsTrue(header.IncludesAccounting);
    }

    [TestMethod]
    public async Task InvoicingBasisIsNotAccounting()
    {
        var header = await new SaftHeaderReader().ReadAsync(Utf8(SaftDocument(basis: "F"))).ConfigureAwait(false);

        Assert.AreEqual("F", header.TaxAccountingBasis);
        Assert.IsFalse(header.IncludesAccounting);
    }

    [TestMethod]
    public async Task ReadsTheHeaderWithoutADeclaredNamespace()
    {
        var header = await new SaftHeaderReader()
            .ReadAsync(Utf8(SaftDocument(withNamespace: false)))
            .ConfigureAwait(false);

        Assert.AreEqual("500000000", header.TaxRegistrationNumber);
    }

    [TestMethod]
    public async Task ReadsAWindows1252EncodedFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var document = SaftDocument().Replace("encoding=\"utf-8\"", "encoding=\"Windows-1252\"", StringComparison.Ordinal);
        using var stream = new MemoryStream(Encoding.GetEncoding(1252).GetBytes(document));

        var header = await new SaftHeaderReader().ReadAsync(stream).ConfigureAwait(false);

        Assert.AreEqual("Empresa Sintética, Lda.", header.CompanyName);
    }

    [TestMethod]
    public async Task RejectsAHeaderMissingTheTaxRegistrationNumber()
    {
        var document = SaftDocument().Replace(
            "<TaxRegistrationNumber>500000000</TaxRegistrationNumber>",
            string.Empty,
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<SaftFileFormatException>(async () =>
            await new SaftHeaderReader().ReadAsync(Utf8(document)).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task RejectsContentThatIsNotSaft()
    {
        await Assert.ThrowsAsync<SaftFileFormatException>(async () =>
            await new SaftHeaderReader().ReadAsync(Utf8("<html><body>não é um SAF-T</body></html>")).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task RejectsAnInvalidPeriod()
    {
        var document = SaftDocument().Replace(
            "<EndDate>2026-12-31</EndDate>",
            "<EndDate>2025-12-31</EndDate>",
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<SaftFileFormatException>(async () =>
            await new SaftHeaderReader().ReadAsync(Utf8(document)).ConfigureAwait(false));
    }
}
