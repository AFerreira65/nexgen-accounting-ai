using System.Globalization;
using System.Text;
using System.Xml;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Escreve o Modelo 22 no formato XML da AT (F4C-03), pelo esquema oficial
/// <c>Modelo22IRCv2026.xsd</c> (namespace <c>dm22irccli-net</c>). Leva o rosto
/// (quadros 01 a 05), o quadro 07, o 08, o 09 (regime geral), o 10 e o 11 que a
/// aplicação apura ou que o utilizador preencheu no impresso. Os anexos não são
/// gerados. Os valores vão em euros com duas casas decimais (o esquema usa
/// <c>xs:decimal</c>), ao contrário da DP de IVA, que vai em cêntimos.
/// <para>
/// O esquema fixa a estrutura e a ordem; o que ele não diz (por exemplo, a que
/// serviço de finanças corresponde o quadro 02) fica por preencher. A validação
/// final só a AT a dá.
/// </para>
/// </summary>
public static class Modelo22XmlWriter
{
    public const string Namespace = "dm22irccli-net";

    /// <summary>O atributo <c>version</c> fixo do elemento raiz no esquema de 2026.</summary>
    public const string SchemaVersion = "v2026";

    /// <summary>Os campos do quadro 07 pela ordem do esquema.</summary>
    private static readonly int[] Quadro07 = 
    [
        701, 702, 703, 805, 704, 705, 806, 706, 707, 708, 709, 710, 711, 782, 712, 713,
        714, 715, 717, 721, 724, 725, 716, 731, 726, 783, 728, 727, 729, 730, 732, 733,
        784, 734, 735, 780, 785, 802, 746, 737, 786, 718, 719, 720, 722, 723, 736, 738,
        739, 740, 741, 742, 743, 787, 744, 745, 747, 748, 749, 788, 750, 789, 790, 751,
        803, 779, 797, 799, 804, 752, 753, 754, 755, 756, 757, 791, 758, 759, 760, 761,
        762, 763, 781, 764, 765, 766, 792, 767, 768, 769, 770, 793, 771, 794, 772, 795,
        773, 796, 774, 800, 801, 798, 775, 776, 777, 778,
    ];

    /// <summary>Os campos do quadro 09 (regime geral) que a aplicação preenche, pela ordem do esquema.</summary>
    private static readonly string[] Quadro09 = ["301", "302", "303", "309", "310", "311"];

    /// <summary>O quadro 10 pela ordem do esquema (347-A e 347-B sem hífen).</summary>
    private static readonly string[] Quadro10 =
    [
        "347A", "347B", "348", "349", "350", "370", "351", "373", "378", "353", "375", "355", "470", "356", "357", "358", "371",
        "359", "360", "374", "361", "362", "363", "372", "364", "379", "365", "366", "369", "367", "368",
    ];

    private static readonly string[] Quadro11 = ["410", "411"];

    // Os campos assinalados do quadro 08, pela ordem do esquema.
    private static readonly int[] Quadro08Reduction = [242, 245, 269, 270, 248, 260, 265, 247];
    private static readonly int[] Quadro08General = [246, 249, 262, 263, 266, 267, 268, 264];

    // O resultado líquido (701) e as somas 708 e 753 admitem sinal; os restantes campos não.
    private static readonly HashSet<string> SignedFields = new(StringComparer.Ordinal) { "701", "708", "753" };

    public static byte[] Write(Modelo22Return model, string taxRegistrationNumber)
    {
        ArgumentNullException.ThrowIfNull(model);
        var taxNumber = (taxRegistrationNumber ?? string.Empty).Trim();
        if (taxNumber.Length != 9 || !taxNumber.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException(
                $"O NIF da empresa ({taxNumber}) não tem nove dígitos: a declaração exige o NIF do sujeito passivo.");
        }

        var amounts = model.Fields.ToDictionary(field => field.Code, field => field.Amount, StringComparer.Ordinal);
        // O campo 303 do impresso é o saldo de prejuízos reportáveis que a aplicação transporta.
        if (!amounts.ContainsKey("303") && amounts.TryGetValue("prejuizos-disponiveis", out var available))
        {
            amounts["303"] = available;
        }

        decimal Value(string code) => amounts.GetValueOrDefault(code);

        string? Nif(string code, string quadro)
        {
            var value = Value(code);
            if (value == 0m)
            {
                return null;
            }

            var text = decimal.ToInt64(value).ToString(CultureInfo.InvariantCulture);
            if (text.Length != 9)
            {
                throw new InvalidOperationException($"O NIF do {quadro} ({text}) não tem nove dígitos.");
            }

            return text;
        }

        string? Choice(string code, int max)
        {
            var value = decimal.ToInt32(Value(code));
            return value >= 1 && value <= max ? value.ToString(CultureInfo.InvariantCulture) : null;
        }

        var negative = amounts
            .Where(item => item.Value < 0m && !SignedFields.Contains(item.Key) && item.Key.All(char.IsAsciiDigit))
            .Select(item => item.Key)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
        if (negative.Count > 0)
        {
            throw new InvalidOperationException(
                $"Os campos {string.Join(", ", negative)} têm valor negativo, o que o Modelo 22 não aceita. Corrija os valores indicados.");
        }

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            void Element(string name, string? text)
            {
                if (text is not null)
                {
                    writer.WriteElementString(name, Namespace, text);
                }
            }

            void Money(string name, decimal value, bool always = false)
            {
                if (value != 0m || always)
                {
                    Element(name, decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture));
                }
            }

            void Empty(string name)
            {
                writer.WriteStartElement(name, Namespace);
                writer.WriteEndElement();
            }

            writer.WriteStartDocument(true);
            writer.WriteStartElement("Modelo22IRCv2026", Namespace);
            writer.WriteAttributeString("version", SchemaVersion);
            writer.WriteStartElement("Rosto", Namespace);

            writer.WriteStartElement("Quadro01", Namespace);
            Element("Q01C1De", $"{model.Year:0000}-01-01");
            Element("Q01C1A", $"{model.Year:0000}-12-31");
            Element("Q01C2", model.Year.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            Empty("Quadro02");

            writer.WriteStartElement("Quadro03", Namespace);
            Element("Q03S2C2", taxNumber);
            Element("Q03S3TipoSujeito", Choice("tipo-sujeito-passivo", 4));
            writer.WriteEndElement();

            writer.WriteStartElement("Quadro04", Namespace);
            Element("Q04S1TipoDeclaracao", Choice("tipo-declaracao", 6));
            writer.WriteEndElement();

            writer.WriteStartElement("Quadro05", Namespace);
            Element("Q05C1", Nif("nif-representante", "representante legal"));
            Element("Q05C2", Nif("nif-cc", "contabilista certificado"));
            writer.WriteEndElement();

            Empty("Quadro07Anterior2010");

            writer.WriteStartElement("Quadro07", Namespace);
            foreach (var number in Quadro07)
            {
                var code = number.ToString(CultureInfo.InvariantCulture);
                Money("Q07C" + code, Value(code), always: number is 701 or 708 or 753 or 776 or 777 or 778);
            }

            writer.WriteEndElement();

            writer.WriteStartElement("Quadro08", Namespace);
            var reduction = decimal.ToInt32(Value("regime-reducao-taxa"));
            var general = decimal.ToInt32(Value("regime-geral"));
            foreach (var number in Quadro08Reduction.Where(number => number == reduction))
            {
                Element("Q08S1C" + number.ToString(CultureInfo.InvariantCulture), "true");
            }

            foreach (var number in Quadro08General.Where(number => number == general))
            {
                Element("Q08S2C" + number.ToString(CultureInfo.InvariantCulture), "true");
            }

            writer.WriteEndElement();

            writer.WriteStartElement("Quadro09", Namespace);
            foreach (var code in Quadro09)
            {
                Money("Q09C" + code, Value(code), always: code is "301" or "302" or "311");
            }

            writer.WriteEndElement();

            writer.WriteStartElement("Quadro10", Namespace);
            foreach (var code in Quadro10)
            {
                var key = code is "347A" or "347B" ? code.Insert(3, "-") : code;
                Money("Q10C" + code, Value(key), always: code is "351" or "378" or "358" or "367" or "368");
            }

            writer.WriteEndElement();

            writer.WriteStartElement("Quadro11", Namespace);
            foreach (var code in Quadro11)
            {
                Money("Q11C" + code, Value(code));
            }

            writer.WriteEndElement();

            Empty("Quadro11A");
            Empty("Quadro11B");
            Empty("Quadro12");
            Empty("Quadro13");
            Empty("Quadro13A");
            Empty("Quadro14");

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }
}
