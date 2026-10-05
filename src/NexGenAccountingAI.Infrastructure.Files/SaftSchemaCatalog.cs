using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Conjunto de esquemas SAF-T disponíveis, indexados pelo namespace do
/// ficheiro. Em produção vem dos ficheiros <c>Schemas/SAFTPT_*.xsd</c>
/// embebidos no assembly (colocados manualmente — ver
/// <c>Schemas/README.md</c>); os testes constroem um catálogo próprio.
/// </summary>
public sealed class SaftSchemaCatalog
{
    private const string ResourcePrefix = "NexGenAccountingAI.Infrastructure.Files.Schemas.SAFTPT_";
    private const string ResourceSuffix = ".xsd";

    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";

    private readonly IReadOnlyDictionary<string, XmlSchemaSet> _byNamespace;
    private readonly IReadOnlyDictionary<string, int> _assertionsNotChecked;

    public SaftSchemaCatalog(
        IReadOnlyDictionary<string, XmlSchemaSet> byNamespace,
        IReadOnlyDictionary<string, int>? assertionsNotChecked = null,
        IReadOnlyList<string>? loadProblems = null)
    {
        ArgumentNullException.ThrowIfNull(byNamespace);
        _byNamespace = byNamespace;
        _assertionsNotChecked = assertionsNotChecked ?? new Dictionary<string, int>();
        LoadProblems = loadProblems ?? [];
    }

    /// <summary>Esquemas embebidos que não foi possível carregar, com o motivo.</summary>
    public IReadOnlyList<string> LoadProblems { get; }

    /// <summary>
    /// Regras condicionais (<c>xs:assert</c>, XSD 1.1) que o esquema desse namespace traz e
    /// que este validador não verifica, porque o .NET só valida XSD 1.0.
    /// </summary>
    public int AssertionsNotChecked(string namespaceUri) => _assertionsNotChecked.GetValueOrDefault(namespaceUri);

    /// <summary>Esquemas oficiais embebidos. Vazio enquanto não for colocado nenhum XSD.</summary>
    public static SaftSchemaCatalog Official { get; } = FromEmbeddedResources();

    public bool IsEmpty => _byNamespace.Count == 0;

    /// <summary>Conjunto de esquemas para o namespace do ficheiro, ou nulo se não houver.</summary>
    public XmlSchemaSet? For(string namespaceUri) =>
        _byNamespace.TryGetValue(namespaceUri, out var set) ? set : null;

    /// <summary>Versão legível a partir do namespace (<c>…PT_1.04_01</c> → <c>1.04_01</c>).</summary>
    public static string? VersionFromNamespace(string? namespaceUri)
    {
        if (string.IsNullOrWhiteSpace(namespaceUri))
        {
            return null;
        }

        var marker = namespaceUri.LastIndexOf("PT_", StringComparison.Ordinal);
        return marker >= 0 ? namespaceUri[(marker + 3)..] : null;
    }

    private static SaftSchemaCatalog FromEmbeddedResources()
    {
        // Os esquemas da AT vêm em Windows-1252.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var assembly = typeof(SaftSchemaCatalog).Assembly;
        var map = new Dictionary<string, XmlSchemaSet>(StringComparer.Ordinal);
        var assertions = new Dictionary<string, int>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            // Um esquema que não carregue não pode deitar a aplicação abaixo: fica de fora,
            // com o motivo, e esse namespace é tratado como «sem esquema».
            try
            {
                var (set, targetNamespace, removed) = Load(stream);
                if (!string.IsNullOrWhiteSpace(targetNamespace))
                {
                    map[targetNamespace] = set;
                    assertions[targetNamespace] = removed;
                }
            }
            catch (Exception exception) when (exception is XmlException or XmlSchemaException or NotSupportedException)
            {
                problems.Add($"{resourceName}: {exception.Message}");
            }
        }

        return new SaftSchemaCatalog(map, assertions, problems);
    }

    /// <summary>
    /// Carrega um XSD oficial. O SAF-T 1.04_01 é um esquema XSD 1.1: traz regras
    /// <c>xs:assert</c> e um grupo <c>xs:all</c> com elementos repetíveis, que o validador
    /// do .NET (XSD 1.0) recusa ao compilar. Tiram-se ou relaxam-se essas regras — a
    /// estrutura, os tipos e as obrigatoriedades continuam a ser validados — e devolve-se
    /// quantas ficaram por verificar.
    /// </summary>
    public static (XmlSchemaSet Set, string? TargetNamespace, int AssertionsRemoved) Load(Stream xsd)
    {
        ArgumentNullException.ThrowIfNull(xsd);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        XDocument document;
        using (var reader = XmlReader.Create(xsd, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
        {
            document = XDocument.Load(reader);
        }

        var asserts = document.Descendants(Xsd + "assert").ToList();
        foreach (var assert in asserts)
        {
            assert.Remove();
        }

        // Outra construção só do XSD 1.1: um grupo `xs:all` com elementos repetíveis (nas
        // linhas de um movimento: débitos e créditos por qualquer ordem, pelo menos um de
        // cada). Em XSD 1.0 passa a «um ou mais destes, por qualquer ordem»; a exigência de
        // haver pelo menos um de cada fica por verificar e conta como regra não verificada.
        var relaxed = 0;
        foreach (var all in document.Descendants(Xsd + "all").ToList())
        {
            if (!all.Elements().Any(particle => particle.Attribute("maxOccurs") is { Value: not ("0" or "1") }))
            {
                continue;
            }

            all.Name = Xsd + "choice";
            all.SetAttributeValue("maxOccurs", "unbounded");
            foreach (var particle in all.Elements())
            {
                particle.Attribute("minOccurs")?.Remove();
                particle.Attribute("maxOccurs")?.Remove();
            }

            relaxed++;
        }

        var set = new XmlSchemaSet { XmlResolver = null };
        using var schemaReader = document.CreateReader();
        var schema = set.Add(targetNamespace: null, schemaDocument: schemaReader);
        set.Compile();
        return (set, schema?.TargetNamespace, asserts.Count + relaxed);
    }
}
