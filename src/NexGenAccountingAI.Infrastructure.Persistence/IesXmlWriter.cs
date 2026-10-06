using System.Globalization;
using System.Text;
using System.Xml;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Escreve a IES no formato XML da AT (F4C-04), pelo esquema «2027 e seguintes»
/// (<c>IES-DA.xsd</c>, namespace <c>IES-DA/2021</c>, a IES com SAF-T), que se usa a
/// partir de 2027 — o utilizador indicou que serve para entregar em 2027 os dados de
/// 2026. Leva o rosto (quadro 01 e NIF) e o Anexo A: opção do quadro 02-A e os
/// quadros 03 e 04, com os campos e as somas que <see cref="IesAnnexAService"/> apura.
/// Os outros quadros e anexos não são gerados.
/// <para>
/// <b>Não confirmado:</b> o valor do atributo <c>versao</c> (o esquema só diz que é
/// obrigatório e numérico; está 10 por extrapolação) e que o modelo de 2027 serve para o período de 2026.
/// </para>
/// </summary>
public static class IesXmlWriter
{
    public const string Namespace = "http://www.portaldasfinancas.gov.pt/de/schema/IES-DA/2021";

    /// <summary>
    /// O atributo <c>versao</c> do elemento raiz; o esquema não diz o valor. O pacote de 2024 traz «Versão 9» no
    /// seu histórico de alterações, por isso o do modelo de 2027 extrapola-se para 10. <b>Não confirmado</b>:
    /// a validação na AT diz se é aceite.
    /// </summary>
    public const int SchemaVersion = 10;

    // Os campos do Anexo A pela ordem do esquema; os que acabam em «-soma» são os totais.
    private static readonly string[] Quadro03 =
    [
        "A00001", "A00002", "A00003", "A00004", "A00005", "A00006", "A00007", "A00008", "A00009", "A00010", "A00011", "A00012",
        "A00013", "A00014", "A00015", "A00016", "A00017", "A00018-soma", "A00019", "A00020", "A00021-soma", "A00022", "A00023",
        "A00024-soma", "A00025", "A00026-soma",
    ];

    private static readonly string[] Quadro04 =
    [
        "A00101", "A00102", "A00103", "A00104", "A00105", "A00106", "A00107", "A00108", "A00109", "A00110", "A00111", "A00112-soma",
        "A00113", "A00114", "A00115", "A00116", "A00117", "A00118", "A00119", "A00120", "A00121", "A00122", "A00123", "A00124",
        "A00125-soma", "A00126-soma", "A00127", "A00128", "A00129", "A00130", "A00131", "A00132", "A00133", "A00134", "A00135",
        "A00136-soma", "A00137", "A00138", "A00139-soma", "A00140", "A00141", "A00142", "A00143", "A00144", "A00145-soma",
        "A00146", "A00147", "A00148", "A00149", "A00150", "A00151", "A00152", "A00153", "A00154", "A00155", "A00156-soma",
        "A00157-soma", "A00158-soma",
    ];

    public static byte[] Write(IesAnnexA annex, string taxRegistrationNumber, string? certifiedAccountantTaxNumber = null)
    {
        ArgumentNullException.ThrowIfNull(annex);
        var taxNumber = (taxRegistrationNumber ?? string.Empty).Trim();
        if (taxNumber.Length != 9 || !taxNumber.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException(
                $"O NIF da empresa ({taxNumber}) não tem nove dígitos: a declaração exige o NIF do sujeito passivo.");
        }

        var accountant = certifiedAccountantTaxNumber?.Trim();
        if (!string.IsNullOrEmpty(accountant) && (accountant.Length != 9 || !accountant.All(char.IsAsciiDigit)))
        {
            throw new InvalidOperationException($"O NIF do contabilista certificado ({accountant}) não tem nove dígitos.");
        }

        var values = annex.Fields.ToDictionary(field => field.Code, field => field.Amount, StringComparer.Ordinal);
        var option = annex.Norm switch { "N" => 1, "S" => 2, "P" => 3, "M" => 4, _ => 2 };

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            void Element(string name, string text) => writer.WriteElementString(name, Namespace, text);

            void Fields(string quadro, string[] codes)
            {
                foreach (var element in codes)
                {
                    var code = element.Split('-')[0];
                    if (values.TryGetValue(code, out var amount))
                    {
                        Element($"{quadro}-{element}", decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture));
                    }
                }
            }

            writer.WriteStartDocument(true);
            writer.WriteStartElement("IES-DA", Namespace);
            writer.WriteAttributeString("versao", SchemaVersion.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("nif", taxNumber);
            writer.WriteAttributeString("ano", annex.Year.ToString("0000", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(accountant))
            {
                writer.WriteAttributeString("nif-cc", accountant);
            }

            writer.WriteStartElement("Rosto", Namespace);
            Element("Q01-1-inicio", $"{annex.Year:0000}-01-01");
            Element("Q01-1-fim", $"{annex.Year:0000}-12-31");
            Element("Q01-ano", annex.Year.ToString("0000", CultureInfo.InvariantCulture));
            Element("Q03-nif", taxNumber);
            writer.WriteEndElement();

            writer.WriteStartElement("AnexoA", Namespace);
            // O atributo obrigatório diz o normativo: NIC, NCRF, NCRF-PE ou NC-ME.
            writer.WriteAttributeString("id", annex.Norm switch { "N" => "NIC", "P" => "NCRF-PE", "M" => "NC-ME", _ => "NCRF" });
            Element("Q02A-opcao", option.ToString(CultureInfo.InvariantCulture));
            Fields("Q03", Quadro03);
            Fields("Q04", Quadro04);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }
}
