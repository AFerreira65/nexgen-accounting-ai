using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>
/// Valida um XML contra um esquema oficial quando a variável de ambiente indica
/// o ficheiro: os XSD da AT não estão no repositório (ver
/// <c>Infrastructure.Files/Schemas/README.md</c>). Sem a variável, não faz nada.
/// </summary>
internal static class XsdValidation
{
    public static void ValidateIfConfigured(string xml, string environmentVariable, string targetNamespace)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        // Os esquemas da AT vêm em Windows-1252.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var schemas = new XmlSchemaSet();
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
}
