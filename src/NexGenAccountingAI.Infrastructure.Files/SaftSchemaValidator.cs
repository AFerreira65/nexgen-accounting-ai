using System.Text;
using System.Xml;
using System.Xml.Schema;
using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Saft;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Valida um SAF-T (PT) contra o esquema oficial da versão declarada, em
/// streaming. Grava um ficheiro temporário quando o conteúdo não é
/// reposicionável (o corpo de um pedido não o é) e faz duas passagens sem
/// carregar o ficheiro inteiro: uma para o namespace e a versão, outra a
/// validar contra o esquema, acumulando as ocorrências sem abortar à
/// primeira.
/// </summary>
public sealed class SaftSchemaValidator : ISaftSchemaValidator
{
    private const int MaxIssues = 200;

    private readonly SaftSchemaCatalog _catalog;

    static SaftSchemaValidator()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SaftSchemaValidator()
        : this(SaftSchemaCatalog.Official)
    {
    }

    public SaftSchemaValidator(SaftSchemaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public async Task<SaftValidationReport> ValidateAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string? tempPath = null;
        try
        {
            Func<Stream> open;
            if (content.CanSeek)
            {
                open = () =>
                {
                    content.Position = 0;
                    return new NonClosingStream(content);
                };
            }
            else
            {
                tempPath = Path.Combine(Path.GetTempPath(), $"saft-validate-{Guid.NewGuid():N}.xml");
                await using (var temp = File.Create(tempPath))
                {
                    await content.CopyToAsync(temp, cancellationToken).ConfigureAwait(false);
                }

                var path = tempPath;
                open = () => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            var (namespaceUri, declaredVersion) = await ScanMetadataAsync(open, cancellationToken).ConfigureAwait(false);
            var version = declaredVersion ?? SaftSchemaCatalog.VersionFromNamespace(namespaceUri);
            var schemas = namespaceUri is null ? null : _catalog.For(namespaceUri);

            var issues = new List<SaftValidationIssue>();
            var truncated = false;

            void Add(SaftValidationIssue issue)
            {
                if (issues.Count >= MaxIssues)
                {
                    truncated = true;
                    return;
                }

                issues.Add(issue);
            }

            if (schemas is null)
            {
                await CheckWellFormedAsync(open, Add, cancellationToken).ConfigureAwait(false);
                Add(version is "1.03_01"
                    ? new SaftValidationIssue(null, SaftValidationSeverity.Warning, "Ficheiro",
                        "O ficheiro usa a versão 1.03_01 do SAF-T (anterior à 1.04_01) e não há esquema oficial carregado para a validar.")
                    : new SaftValidationIssue(null, SaftValidationSeverity.Warning, "Ficheiro",
                        $"Não há esquema oficial carregado para a versão '{version ?? "desconhecida"}'; validou-se apenas a boa formação do XML. Ver Schemas/README.md."));
                return new SaftValidationReport(version, SchemaAvailable: false, SchemaValid: false, issues, truncated);
            }

            var currentSection = "Ficheiro";
            var settings = new XmlReaderSettings
            {
                Async = true,
                CloseInput = false,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersFromEntities = 0,
                XmlResolver = null,
                ValidationType = ValidationType.Schema,
                Schemas = schemas,
                ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings
            };
            settings.ValidationEventHandler += (_, args) =>
            {
                var line = args.Exception?.LineNumber ?? 0;
                var position = args.Exception?.LinePosition ?? 0;
                Add(new SaftValidationIssue(
                    RuleId: null,
                    Severity: args.Severity == XmlSeverityType.Error ? SaftValidationSeverity.Error : SaftValidationSeverity.Warning,
                    Scope: currentSection,
                    Message: args.Message,
                    Line: line > 0 ? line : null,
                    Position: position > 0 ? position : null));
            };

            try
            {
                await using var stream = open();
                using var reader = XmlReader.Create(stream, settings);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                    {
                        currentSection = reader.LocalName;
                    }
                }
            }
            catch (XmlException exception)
            {
                throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
            }

            if (version is "1.03_01")
            {
                Add(new SaftValidationIssue(null, SaftValidationSeverity.Warning, "Ficheiro",
                    "O ficheiro usa a versão 1.03_01 do SAF-T, anterior à 1.04_01."));
            }

            var hasError = issues.Any(issue => issue.Severity == SaftValidationSeverity.Error);
            var notChecked = _catalog.AssertionsNotChecked(namespaceUri!);
            if (notChecked > 0)
            {
                Add(new SaftValidationIssue(null, SaftValidationSeverity.Information, "Ficheiro",
                    $"Validado contra o esquema oficial {version}. As {notChecked} regras condicionais do esquema (XSD 1.1, por exemplo «conta de movimento tem de ter taxonomia») não são verificadas aqui; o validador da AT verifica-as."));
            }

            return new SaftValidationReport(version, SchemaAvailable: true, SchemaValid: !hasError, issues, truncated);
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // limpeza best-effort
                }
            }
        }
    }

    private static async Task<(string? NamespaceUri, string? Version)> ScanMetadataAsync(Func<Stream> open, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
            XmlResolver = null
        };

        string? namespaceUri = null;
        string? version = null;

        try
        {
            await using var stream = open();
            using var reader = XmlReader.Create(stream, settings);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                namespaceUri ??= string.IsNullOrEmpty(reader.NamespaceURI) ? null : reader.NamespaceURI;

                if (reader.LocalName.Equals("AuditFileVersion", StringComparison.OrdinalIgnoreCase))
                {
                    version = (await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                    break;
                }

                if (reader.Depth == 1 && !reader.LocalName.Equals("Header", StringComparison.OrdinalIgnoreCase) && namespaceUri is not null)
                {
                    break;
                }
            }
        }
        catch (XmlException)
        {
            // Um XML mal formado é reportado na passagem de validação.
        }

        return (namespaceUri, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    private static async Task CheckWellFormedAsync(Func<Stream> open, Action<SaftValidationIssue> add, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0,
            XmlResolver = null
        };

        try
        {
            await using var stream = open();
            using var reader = XmlReader.Create(stream, settings);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }
    }

    /// <summary>Envolve o stream do chamador para os leitores não o fecharem.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // não fecha o stream do chamador
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
