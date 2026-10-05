using System.Globalization;
using System.Text;
using System.Xml;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O que a declaração periódica leva além dos valores do quadro 06.</summary>
/// <param name="TaxRegistrationNumber">NIF do sujeito passivo (nove dígitos).</param>
/// <param name="CertifiedAccountantTaxNumber">NIF do contabilista certificado, quando a empresa o tem.</param>
/// <param name="WithinDeadline">Dentro do prazo legal (<c>true</c>) ou fora dele.</param>
/// <param name="FiledRecapitulativeStatement">Se no período foi apresentada alguma declaração recapitulativa.</param>
public sealed record VatDeclarationFiling(
    string TaxRegistrationNumber,
    string? CertifiedAccountantTaxNumber,
    bool WithinDeadline,
    bool FiledRecapitulativeStatement);

/// <summary>
/// Escreve a declaração periódica de IVA no formato XML da AT (F4A-02), pelo esquema
/// oficial <c>dpiva-06.xsd</c> (namespace <c>http://www.at.gov.pt/schemas/dpiva</c>,
/// versão «06»). É o ficheiro que se carrega no Portal das Finanças e que o webservice
/// recebe.
/// <para>
/// <b>O que o esquema diz:</b> a estrutura, a ordem dos elementos e os nomes; cada
/// elemento do quadro 06 traz na documentação o número do campo do impresso, e é daí
/// que vem a correspondência abaixo. <b>O que o esquema não diz</b> e foi confirmado
/// num ficheiro exportado do Portal (de terceiros): os valores vão em cêntimos, o
/// período é «01» a «12» ou «03T», «06T», «09T», «12T», a localização da sede é 1
/// (Continente), 2 (Açores) ou 3 (Madeira), o prazo é 1 (dentro) ou 2 (fora) e as
/// perguntas de sim ou não usam «01» e «02». Falta a confirmação da própria AT, que
/// só a operação de validação do webservice dá.
/// </para>
/// <para>
/// Só leva o que a aplicação apura: o quadro 06. O quadro 06-A vai a zero e os anexos
/// (R, campos 40 e 41, clientes, fornecedores) não são gerados.
/// </para>
/// </summary>
public static class VatDeclarationXmlWriter
{
    public const string Namespace = "http://www.at.gov.pt/schemas/dpiva";
    public const string SchemaVersion = "06";

    /// <summary>Os elementos do apuramento (quadro 06) pela ordem do esquema, com o número do campo (0: sem campo).</summary>
    private static readonly (string Element, int Field)[] Settlement =
    [
        ("btBensUEArtigo15", 14), ("btBensUEArtigo22", 15), ("btBensUELiquidadoDeclarante", 12), ("btBensUETotal", 10),
        ("btImportDeclarante", 18), ("btOperacoesIsentasComDeducao", 8), ("btOperacoesIsentasSemDeducao", 9), ("btServicosUE", 16),
        ("btTaxaIntermedia", 5), ("btTaxaNormal", 3), ("btTaxaReduzida", 1), ("btTotal", 90), ("btTransmissoesUEIsentas", 7),
        ("excessoAReportar", 96), ("excessoAReportarPeriodoAnterior", 61), ("ivaAEntregar", 93), ("ivaARecuperar", 94),
        ("ivaBensUELiquidadoDeclarante", 13), ("ivaBensUETotal", 11), ("ivaDedutivelAnexoR1", 65), ("ivaDedutivelAnexoR2", 67),
        ("ivaDedutivelAnexosRTotal", 0), ("ivaDedutivelExistenciasTaxaIntermedia", 23), ("ivaDedutivelExistenciasTaxaNormal", 22),
        ("ivaDedutivelExistenciasTaxaReduzida", 21), ("ivaDedutivelImobilizado", 20), ("ivaDedutivelOutros", 24),
        ("ivaDedutivelTotal", 0), ("ivaFavorEstadoTotal", 92), ("ivaFavorSujPassivoTotal", 91), ("ivaImportDeclarante", 19),
        ("ivaLiquidadoAnexoR1", 66), ("ivaLiquidadoAnexoR2", 68), ("ivaServicosUE", 17), ("ivaTaxaIntermedia", 6),
        ("ivaTaxaNormal", 4), ("ivaTaxaReduzida", 2), ("reembolso", 95), ("regularizacoesFavorEstado", 41),
        ("regularizacoesFavorSujPassivoComunicadasCobranca", 81), ("regularizacoesFavorSujPassivoNaoComunicadasCobranca", 40),
    ];

    /// <summary>Totais que vão sempre no ficheiro, mesmo a zero (como no que o Portal exporta).</summary>
    private static readonly HashSet<string> AlwaysWritten = new(StringComparer.Ordinal)
    {
        "btBensUETotal", "btTotal", "excessoAReportar", "excessoAReportarPeriodoAnterior", "ivaAEntregar", "ivaARecuperar",
        "ivaBensUETotal", "ivaDedutivelAnexosRTotal", "ivaDedutivelOutros", "ivaDedutivelTotal", "ivaFavorEstadoTotal",
        "ivaFavorSujPassivoTotal", "reembolso",
    };

    public static byte[] Write(VatDeclaration declaration, VatDeclarationFiling filing)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(filing);

        var taxNumber = (filing.TaxRegistrationNumber ?? string.Empty).Trim();
        if (taxNumber.Length != 9 || !taxNumber.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException(
                $"O NIF da empresa ({taxNumber}) não tem nove dígitos: a declaração exige o NIF do sujeito passivo.");
        }

        var accountant = filing.CertifiedAccountantTaxNumber?.Trim();
        if (!string.IsNullOrEmpty(accountant) && (accountant.Length != 9 || !accountant.All(char.IsAsciiDigit)))
        {
            throw new InvalidOperationException($"O NIF do contabilista certificado ({accountant}) não tem nove dígitos.");
        }

        if (declaration.Period is null)
        {
            throw new InvalidOperationException("As datas não correspondem a um mês nem a um trimestre: a declaração é mensal ou trimestral.");
        }

        decimal Value(int field) => declaration.Fields.FirstOrDefault(item => item.Number == field)?.Amount ?? 0m;
        var negative = declaration.Fields.Where(field => field.Amount < 0m).Select(field => field.Number).ToList();
        if (negative.Count > 0)
        {
            throw new InvalidOperationException(
                $"Os campos {string.Join(", ", negative)} têm valor negativo, o que a declaração não aceita. Corrija os lançamentos ou a classificação de IVA das contas.");
        }

        if (new[] { 65, 66, 67, 68 }.Any(field => Value(field) != 0m))
        {
            throw new InvalidOperationException(
                "Há operações localizadas noutra região (campos 65 a 68), que exigem o Anexo R. A aplicação ainda não gera esse anexo, por isso o ficheiro seria recusado.");
        }

        // Somas que o esquema pede e que não têm número de campo no impresso.
        decimal Computed(string element) => element switch
        {
            "ivaDedutivelTotal" => Value(20) + Value(21) + Value(22) + Value(23) + Value(24),
            "ivaDedutivelAnexosRTotal" => Value(65) + Value(67),
            _ => 0m,
        };

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument(true);
            writer.WriteStartElement("dpiva", Namespace);
            writer.WriteAttributeString("version", SchemaVersion);
            writer.WriteStartElement("rosto", Namespace);

            writer.WriteStartElement("apuramento", Namespace);
            foreach (var (element, field) in Settlement)
            {
                var amount = field == 0 ? Computed(element) : Value(field);
                if (amount != 0m || AlwaysWritten.Contains(element))
                {
                    writer.WriteElementString(element, Namespace, Cents(amount));
                }
            }

            // Não apuramos o quadro 06-A (operações em que o adquirente liquida o imposto).
            writer.WriteElementString("temOperacoesAdquirenteComLiqImposto", Namespace, "02");
            writer.WriteEndElement();

            writer.WriteStartElement("desenvolvimento", Namespace);
            writer.WriteElementString("totalQuadro06A", Namespace, "0");
            writer.WriteEndElement();

            writer.WriteStartElement("inicio", Namespace);
            writer.WriteElementString("anoDeclaracao", Namespace, declaration.To.Year.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("apresentouDeclRecapitulativa", Namespace, filing.FiledRecapitulativeStatement ? "true" : "false");
            writer.WriteElementString("atividadesImobiliarias", Namespace, "false");
            writer.WriteElementString("localizacaoSede", Namespace, declaration.Seat switch
            {
                "Açores" or "Acores" or "Azores" => "2",
                "Madeira" => "3",
                _ => "1",
            });
            writer.WriteElementString("nif", Namespace, taxNumber);
            if (!string.IsNullOrEmpty(accountant))
            {
                writer.WriteElementString("nifCC", Namespace, accountant);
            }

            writer.WriteElementString("periodoDeclaracao", Namespace, declaration.Period);
            writer.WriteElementString("prazo", Namespace, filing.WithinDeadline ? "1" : "2");
            writer.WriteElementString("temAnexoRAcores", Namespace, "false");
            writer.WriteElementString("temAnexoRContinente", Namespace, "false");
            writer.WriteElementString("temAnexoRMadeira", Namespace, "false");
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>O valor em cêntimos, sem casas decimais.</summary>
    private static string Cents(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
