using System.Reflection;
using System.Xml;
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

    private readonly IReadOnlyDictionary<string, XmlSchemaSet> _byNamespace;

    public SaftSchemaCatalog(IReadOnlyDictionary<string, XmlSchemaSet> byNamespace)
    {
        ArgumentNullException.ThrowIfNull(byNamespace);
        _byNamespace = byNamespace;
    }

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
        var assembly = typeof(SaftSchemaCatalog).Assembly;
        var map = new Dictionary<string, XmlSchemaSet>(StringComparer.Ordinal);

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

            var set = new XmlSchemaSet { XmlResolver = null };
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var schema = set.Add(targetNamespace: null, schemaDocument: reader);
            set.Compile();

            if (!string.IsNullOrWhiteSpace(schema?.TargetNamespace))
            {
                map[schema.TargetNamespace] = set;
            }
        }

        return new SaftSchemaCatalog(map);
    }
}
