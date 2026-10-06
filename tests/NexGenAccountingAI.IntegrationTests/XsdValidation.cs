using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>
/// Valida um XML contra um esquema oficial quando a variável de ambiente indica
/// o ficheiro: os XSD da AT não estão no repositório (ver
/// <c>Infrastructure.Files/Schemas/README.md</c>). Sem a variável, procura o ficheiro
/// na pasta <c>Schemas</c>, onde <c>scripts/get-official-schemas.ps1</c> os deixa; se
/// também não estiver lá, não faz nada.
/// </summary>
internal static class XsdValidation
{
    public static void ValidateIfConfigured(string xml, string environmentVariable, string targetNamespace, string? fallbackFileName = null)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            // Sem a variável, usa o que `scripts/get-official-schemas.ps1` descarregou.
            path = fallbackFileName is null ? null : FindInSchemasFolder(fallbackFileName);
            if (path is null)
            {
                return;
            }
        }

        // Os esquemas da AT vêm em Windows-1252.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Alguns esquemas (Modelo 22) incluem outros ficheiros da mesma pasta.
        var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        using (var schemaReader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            schemas.Add(targetNamespace, schemaReader);
        }

        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        settings.ValidationEventHandler += (_, args) => errors.Add($"{args.Severity}: {args.Message} (linha {args.Exception?.LineNumber})");
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
        }

        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }

    private static string? FindInSchemasFolder(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "NexGenAccountingAI.Infrastructure.Files", "Schemas", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
