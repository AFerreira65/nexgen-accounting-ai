using System.Globalization;
using System.Text;
using System.Xml;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Escreve o ficheiro «Mapas do dossier fiscal» (F3-14D) no esquema oficial da
/// AT — <c>DossierFiscal.xsd</c>, versão 1.02.00, namespace
/// <c>http://www.dgci.gov.pt/2012/DOSSIER-FISCAL</c>, publicado com o validador
/// em <c>info.portaldasfinancas.gov.pt/apps/saft-pt_df/</c> — e pela estrutura
/// de dados da Portaria n.º 92-A/2011, alterada pela Portaria n.º 94/2013.
/// <para>
/// Os elementos seguem a ordem do esquema. Os montantes não têm sinal (o sinal
/// do Modelo 31 vai nas colunas 7 e 12) e as taxas levam duas casas decimais
/// (<c>Coef6Type</c>). No Modelo 31 a natureza segue a codificação desse mapa
/// (3 propriedades de investimento, 4 biológicos), que é a inversa da do
/// Modelo 32. No grupo I do Modelo 30, as imparidades de inventários vão como
/// «AJUSTAMENTOS EM INVENTÁRIOS» e as de créditos como «OUTROS», com o nome da
/// conta: a contabilidade não distingue o contencioso da mora.
/// </para>
/// </summary>
public static class DossierMapsXmlWriter
{
    public const string Namespace = "http://www.dgci.gov.pt/2012/DOSSIER-FISCAL";

    public static byte[] Write(string taxRegistrationNumber, int year, DossierMaps maps, Modelo30Map? modelo30)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var nif = new string((taxRegistrationNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (nif.Length != 9 || nif[0] == '0')
        {
            throw new InvalidOperationException($"O NIF da empresa ('{taxRegistrationNumber}') não é um número de nove dígitos: o ficheiro exige-o no cabeçalho.");
        }

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("DossierFiscal", Namespace);
            writer.WriteStartElement("CabecalhoGeral", Namespace);
            Text(writer, "NIF", nif);
            Text(writer, "Exercicio", year.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            if (modelo30 is { Lines.Count: > 0 })
            {
                WriteModelo30(writer, modelo30);
            }

            if (maps.Modelo31.Count > 0)
            {
                writer.WriteStartElement("Modelo31", Namespace);
                foreach (var map in maps.Modelo31.Where(map => map.Nature != 0).Take(5))
                {
                    WriteModelo31(writer, map);
                }

                writer.WriteEndElement();
            }

            if (maps.Modelo32.Count > 0)
            {
                writer.WriteStartElement("Modelo32", Namespace);
                foreach (var map in maps.Modelo32.Where(map => map.Nature != 0).Take(12))
                {
                    WriteModelo32(writer, map);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteModelo30(XmlWriter writer, Modelo30Map map)
    {
        writer.WriteStartElement("Modelo30", Namespace);
        foreach (var (group, suffix, sums) in new[] { (1, "I", "Q02-Modelo30-Somas_I"), (2, "II", "Q02-Modelo30-Somas_II"), (3, "III", "Q02-Somas_III") })
        {
            var lines = map.Lines.Where(line => line.Group == group).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            writer.WriteStartElement($"Q02-Modelo30-Detalhe_{suffix}", Namespace);
            writer.WriteStartElement($"Q02-Modelo30-Linhas_{suffix}", Namespace);
            foreach (var line in lines)
            {
                writer.WriteStartElement($"Q02-Modelo30-Linha_{suffix}", Namespace);
                if (group == 1)
                {
                    var inventory = line.AccountCode.StartsWith('3');
                    Text(writer, "Q02-01_1", inventory ? "AJUSTAMENTOS EM INVENTÁRIOS" : "OUTROS");
                    if (!inventory)
                    {
                        Text(writer, "Q02-01_2", $"{line.AccountCode} {line.AccountName}");
                    }
                }
                else
                {
                    Text(writer, "Q02-01", $"{line.AccountCode} {line.AccountName}");
                }

                Amount(writer, "Q02-02", line.Opening);
                Amount(writer, "Q02-03", line.TaxedOpening);
                Amount(writer, "Q02-04", line.AcceptedOpening);
                Amount(writer, "Q02-05", line.TaxedUsed);
                Amount(writer, "Q02-06", line.AcceptedUsed);
                Amount(writer, "Q02-07", line.TaxedReversed);
                Amount(writer, "Q02-08", line.AcceptedReversed);
                Amount(writer, "Q02-09", line.Constituted);
                Amount(writer, "Q02-10", line.Closing);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteStartElement(sums, Namespace);
            Amount(writer, "Q02-Coluna02", lines.Sum(line => Positive(line.Opening)));
            Amount(writer, "Q02-Coluna03", lines.Sum(line => Positive(line.TaxedOpening)));
            Amount(writer, "Q02-Coluna04", lines.Sum(line => Positive(line.AcceptedOpening)));
            Amount(writer, "Q02-Coluna05", lines.Sum(line => Positive(line.TaxedUsed)));
            Amount(writer, "Q02-Coluna06", lines.Sum(line => Positive(line.AcceptedUsed)));
            Amount(writer, "Q02-Coluna07", lines.Sum(line => Positive(line.TaxedReversed)));
            Amount(writer, "Q02-Coluna08", lines.Sum(line => Positive(line.AcceptedReversed)));
            Amount(writer, "Q02-Coluna09", lines.Sum(line => Positive(line.Constituted)));
            Amount(writer, "Q02-Coluna10", lines.Sum(line => Positive(line.Closing)));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteModelo31(XmlWriter writer, Modelo31Map map)
    {
        writer.WriteStartElement("AnexoModelo31", Namespace);
        writer.WriteStartElement("Q01-Modelo31-Cabecalho", Namespace);
        // O Modelo 31 numera 3 propriedades de investimento e 4 biológicos; o 32, ao contrário.
        Text(writer, "Q01-Natureza", (map.Nature switch { 3 => 4, 4 => 3, _ => map.Nature }).ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
        writer.WriteStartElement("Q02-Modelo31-Detalhe", Namespace);
        writer.WriteStartElement("Q02-Modelo31-Linhas", Namespace);
        foreach (var line in map.Lines)
        {
            writer.WriteStartElement("Q02-Modelo31-Linha", Namespace);
            Text(writer, "Q02-01", line.Description);
            Amount(writer, "Q02-02", line.RealisationValue);
            Amount(writer, "Q02-03", line.TaxAcquisitionValue);
            Text(writer, "Q02-04", line.AcquisitionYear.ToString(CultureInfo.InvariantCulture));
            Amount(writer, "Q02-05", line.BookAcquisitionValue);
            Amount(writer, "Q02-06", line.BookDepreciation);
            Text(writer, "Q02-07", line.BookGain < 0m ? "-" : "+");
            Amount(writer, "Q02-08", Math.Abs(line.BookGain));
            Amount(writer, "Q02-09", line.UntaxedGain);
            Amount(writer, "Q02-10", line.AcceptedDepreciation);
            Text(writer, "Q02-11", Rate(line.Coefficient));
            Text(writer, "Q02-12", line.TaxGain < 0m ? "-" : "+");
            Amount(writer, "Q02-13", Math.Abs(line.TaxGain));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteStartElement("Q02-Modelo31-Somas", Namespace);
        Amount(writer, "Q02-Coluna02", map.Lines.Sum(line => line.RealisationValue));
        Text(writer, "Q02-Coluna08", Money(map.BookGain));
        Text(writer, "Q02-Coluna13", Money(map.TaxGain));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteModelo32(XmlWriter writer, Modelo32Map map)
    {
        writer.WriteStartElement("AnexoModelo32", Namespace);
        writer.WriteStartElement("Q01-Modelo32-Cabecalho", Namespace);
        Text(writer, "Q01-Natureza", map.Nature.ToString(CultureInfo.InvariantCulture));
        Text(writer, "Q01-Metodo", map.Method.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
        writer.WriteStartElement("Q02-Modelo32-Detalhe", Namespace);
        writer.WriteStartElement("Q02-Modelo32-Linhas", Namespace);
        foreach (var line in map.Lines)
        {
            writer.WriteStartElement("Q02-Modelo32-Linha", Namespace);
            Text(writer, "Q02-01", line.TaxTableCode ?? string.Empty);
            Text(writer, "Q02-02", line.Description);
            if (line.StartMonth is { } month)
            {
                Text(writer, "Q02-03", month.ToString(CultureInfo.InvariantCulture));
            }

            Text(writer, "Q02-04", line.StartYear.ToString(CultureInfo.InvariantCulture));
            Amount(writer, "Q02-05", line.BookValue);
            Amount(writer, "Q02-06", line.TaxValue);
            if (line.UsefulLifeYears is { } years)
            {
                Text(writer, "Q02-07", years.ToString(CultureInfo.InvariantCulture));
            }

            Amount(writer, "Q02-08", line.BookedInPeriod);
            Amount(writer, "Q02-09", line.AcceptedInPriorPeriods);
            // O esquema dá a escolha entre a taxa (10) e a taxa corrigida das quotas decrescentes (11).
            if (line.CorrectedRate is { } corrected)
            {
                Text(writer, "Q02-11", Rate(corrected));
            }
            else
            {
                Text(writer, "Q02-10", Rate(line.Rate));
            }
            Amount(writer, "Q02-12", line.TaxLimit);
            Amount(writer, "Q02-13", line.AcceptedImpairment);
            if (line.LostRates is { } lost)
            {
                Text(writer, "Q02-14", Rate(lost));
            }

            Amount(writer, "Q02-15", line.NotAccepted);
            Amount(writer, "Q02-16", line.Recovered);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteStartElement("Q02-Modelo32-Somas", Namespace);
        Amount(writer, "Q02-Coluna05", map.BookValue);
        Amount(writer, "Q02-Coluna06", map.TaxValue);
        Amount(writer, "Q02-Coluna08", map.BookedInPeriod);
        Amount(writer, "Q02-Coluna09", map.AcceptedInPriorPeriods);
        Amount(writer, "Q02-Coluna12", map.TaxLimit);
        Amount(writer, "Q02-Coluna13", map.AcceptedImpairment);
        Amount(writer, "Q02-Coluna15", map.NotAccepted);
        Amount(writer, "Q02-Coluna16", map.Recovered);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void Text(XmlWriter writer, string name, string value) => writer.WriteElementString(name, Namespace, value);

    /// <summary>Um montante sem sinal (<c>Montante16Type</c>): um valor negativo — dados inconsistentes — vai como zero.</summary>
    private static void Amount(XmlWriter writer, string name, decimal value) => Text(writer, name, Money(Positive(value)));

    private static decimal Positive(decimal value) => Math.Max(0m, value);

    private static string Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Rate(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}
