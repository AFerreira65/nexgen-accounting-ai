using System.Text;
using System.Xml;
using System.Xml.Schema;
using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Saft;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftSchemaValidatorTests
{
    private const string TestNamespace = "urn:test:saft";

    // Esquema mínimo só para exercitar o mecanismo de validação em streaming;
    // não é o SAF-T real (esse é um drop-in — ver Schemas/README.md).
    private const string TestSchema = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="{TestNamespace}"
                   xmlns="{TestNamespace}"
                   elementFormDefault="qualified">
          <xs:element name="AuditFile">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="Header">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="AuditFileVersion" type="xs:string"/>
                      <xs:element name="TaxRegistrationNumber" type="xs:integer"/>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>
                <xs:element name="Count" type="xs:int"/>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private static string Document(
        string ns = TestNamespace,
        string version = "1.04_01",
        string nif = "500000000",
        string count = "3") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AuditFile xmlns="{ns}">
          <Header>
            <AuditFileVersion>{version}</AuditFileVersion>
            <TaxRegistrationNumber>{nif}</TaxRegistrationNumber>
          </Header>
          <Count>{count}</Count>
        </AuditFile>
        """;

    private static SaftSchemaValidator ValidatorWithTestSchema()
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(TestSchema));
        var schema = set.Add(targetNamespace: null, schemaDocument: reader)!;
        set.Compile();
        var catalog = new SaftSchemaCatalog(new Dictionary<string, XmlSchemaSet> { [schema.TargetNamespace!] = set });
        return new SaftSchemaValidator(catalog);
    }

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [TestMethod]
    public async Task AConformantFileHasNoErrorsAndReadsTheVersion()
    {
        var report = await ValidatorWithTestSchema().ValidateAsync(Utf8(Document())).ConfigureAwait(false);

        Assert.IsTrue(report.SchemaAvailable);
        Assert.IsTrue(report.SchemaValid);
        Assert.AreEqual(0, report.ErrorCount);
        Assert.AreEqual("1.04_01", report.Version);
        Assert.IsFalse(report.Truncated);
    }

    [TestMethod]
    public async Task ATypeViolationIsReportedWithLineAndPosition()
    {
        var report = await ValidatorWithTestSchema()
            .ValidateAsync(Utf8(Document(count: "não-é-inteiro")))
            .ConfigureAwait(false);

        Assert.IsTrue(report.SchemaAvailable);
        Assert.IsFalse(report.SchemaValid);
        var error = report.Issues.Single(issue => issue.Severity == SaftValidationSeverity.Error);
        Assert.IsNull(error.RuleId);
        Assert.IsTrue(error.Line is > 0);
        Assert.IsTrue(error.Position is > 0);
    }

    [TestMethod]
    public async Task AnElementOutOfOrderIsAnError()
    {
        var outOfOrder = """
            <?xml version="1.0" encoding="utf-8"?>
            <AuditFile xmlns="urn:test:saft">
              <Count>3</Count>
              <Header>
                <AuditFileVersion>1.04_01</AuditFileVersion>
                <TaxRegistrationNumber>500000000</TaxRegistrationNumber>
              </Header>
            </AuditFile>
            """;

        var report = await ValidatorWithTestSchema().ValidateAsync(Utf8(outOfOrder)).ConfigureAwait(false);

        Assert.IsFalse(report.SchemaValid);
        Assert.IsTrue(report.Issues.Any(issue => issue.Severity == SaftValidationSeverity.Error));
    }

    [TestMethod]
    public async Task AnUnknownVersionValidatesWellFormednessOnly()
    {
        var report = await ValidatorWithTestSchema()
            .ValidateAsync(Utf8(Document(ns: "urn:OECD:StandardAuditFile-Tax:PT_9.99", version: "9.99")))
            .ConfigureAwait(false);

        Assert.IsFalse(report.SchemaAvailable);
        Assert.IsFalse(report.SchemaValid);
        Assert.AreEqual("9.99", report.Version);
        Assert.AreEqual(0, report.ErrorCount);
        Assert.IsTrue(report.Issues.Any(issue => issue.Severity == SaftValidationSeverity.Warning));
    }

    [TestMethod]
    public async Task TheVersionFallsBackToTheNamespaceWhenTheHeaderHasNone()
    {
        var noVersionElement = """
            <?xml version="1.0" encoding="utf-8"?>
            <AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01">
              <Header><CompanyName>Sem versão</CompanyName></Header>
            </AuditFile>
            """;

        var report = await ValidatorWithTestSchema().ValidateAsync(Utf8(noVersionElement)).ConfigureAwait(false);

        Assert.AreEqual("1.04_01", report.Version);
        Assert.IsFalse(report.SchemaAvailable);
    }

    [TestMethod]
    public async Task ThePreviousVersionIsFlaggedWhenItsSchemaIsMissing()
    {
        var report = await ValidatorWithTestSchema()
            .ValidateAsync(Utf8(Document(ns: "urn:OECD:StandardAuditFile-Tax:PT_1.03_01", version: "1.03_01")))
            .ConfigureAwait(false);

        Assert.IsFalse(report.SchemaAvailable);
        Assert.AreEqual("1.03_01", report.Version);
        Assert.IsTrue(report.Issues.Any(issue =>
            issue.Severity == SaftValidationSeverity.Warning && issue.Message.Contains("1.03_01", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task MalformedXmlThrowsSaftFileFormatException()
    {
        await Assert.ThrowsExactlyAsync<SaftFileFormatException>(() =>
            ValidatorWithTestSchema().ValidateAsync(Utf8("<AuditFile xmlns=\"urn:test:saft\"><Header>"))).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ValidatesAForwardOnlyStreamByBufferingToATempFile()
    {
        using var forwardOnly = new ForwardOnlyStream(Encoding.UTF8.GetBytes(Document()));

        var report = await ValidatorWithTestSchema().ValidateAsync(forwardOnly).ConfigureAwait(false);

        Assert.IsTrue(report.SchemaValid);
        Assert.AreEqual("1.04_01", report.Version);
    }

    [TestMethod]
    public void VersionFromNamespaceReadsTheSuffix()
    {
        Assert.AreEqual("1.04_01", SaftSchemaCatalog.VersionFromNamespace("urn:OECD:StandardAuditFile-Tax:PT_1.04_01"));
        Assert.IsNull(SaftSchemaCatalog.VersionFromNamespace(null));
        Assert.IsNull(SaftSchemaCatalog.VersionFromNamespace("urn:something:else"));
    }

    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [TestMethod]
    public void LoadsAnXsd11SchemaInWindows1252WithoutItsAssertions()
    {
        // Como o SAF-T 1.04_01 oficial: Windows-1252, vc:minVersion 1.1 e regras xs:assert,
        // que o validador do .NET (XSD 1.0) recusava ao compilar — a validação dava erro 500.
        const string xsd = """
            <?xml version="1.0" encoding="Windows-1252"?>
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:vc="http://www.w3.org/2007/XMLSchema-versioning"
                xmlns:ns="urn:teste:assert" targetNamespace="urn:teste:assert" elementFormDefault="qualified" vc:minVersion="1.1">
              <xs:element name="Conta">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="Código" type="xs:string"/>
                    <xs:element name="Taxonomia" type="xs:int" minOccurs="0"/>
                  </xs:sequence>
                  <xs:assert test="if (ns:Taxonomia) then true() else false()"/>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(System.Text.Encoding.GetEncoding(1252).GetBytes(xsd.TrimStart()));

        var (set, targetNamespace, removed) = SaftSchemaCatalog.Load(stream);

        Assert.AreEqual("urn:teste:assert", targetNamespace);
        Assert.AreEqual(1, removed);
        Assert.IsTrue(set.IsCompiled);
        var catalog = new SaftSchemaCatalog(
            new Dictionary<string, XmlSchemaSet> { [targetNamespace!] = set },
            new Dictionary<string, int> { [targetNamespace!] = removed });
        Assert.AreEqual(1, catalog.AssertionsNotChecked("urn:teste:assert"));
        Assert.AreEqual(0, catalog.AssertionsNotChecked("urn:outro"));
    }

    [TestMethod]
    public void TheOfficialSchemasInTheFolderAllLoad()
    {
        // Sem esquemas na pasta (scripts/get-official-schemas.ps1 não corrido) o catálogo fica
        // vazio e o teste passa; com eles, nenhum pode ficar por carregar.
        Assert.AreEqual(0, SaftSchemaCatalog.Official.LoadProblems.Count, string.Join(Environment.NewLine, SaftSchemaCatalog.Official.LoadProblems));
    }
}
