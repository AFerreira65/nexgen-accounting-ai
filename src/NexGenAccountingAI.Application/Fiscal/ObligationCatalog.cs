using System.Globalization;

namespace NexGenAccountingAI.Application.Fiscal;

/// <summary>Com que frequência a obrigação se repete.</summary>
public enum ObligationFrequency
{
    Mensal = 0,
    Trimestral = 1,
    Anual = 2,
}

/// <summary>
/// Uma obrigação declarativa ou de pagamento, com a regra do prazo e a fonte
/// legal em que foi confirmada. <see cref="MonthsAfterPeriod"/> conta a partir
/// do último mês do período (o mês, o trimestre ou o ano civil);
/// <see cref="Day"/> nulo é o último dia desse mês.
/// </summary>
public sealed record ObligationDefinition(
    string Code,
    string Name,
    ObligationFrequency Frequency,
    int MonthsAfterPeriod,
    int? Day,
    string Applicability,
    string Source);

/// <summary>Um período concreto de uma obrigação, com o prazo legal.</summary>
public sealed record ObligationOccurrence(ObligationDefinition Definition, string PeriodKey, string PeriodLabel, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate);

/// <summary>
/// As obrigações do calendário (F3-27A). Só entram prazos confirmados no texto
/// dos códigos publicado no Portal das Finanças (consultado em 2026-10-05); a
/// comunicação de faturas e a de inventários ficam de fora até se confirmar o
/// prazo em vigor. As datas são as legais: quando calham em dia não útil ou há
/// prorrogação, o prazo altera-se à mão em cada período.
/// </summary>
public static class ObligationCatalog
{
    public const string VatReturnMonthly = "IVA-DP-M";
    public const string VatPaymentMonthly = "IVA-PAG-M";
    public const string VatReturnQuarterly = "IVA-DP-T";
    public const string VatPaymentQuarterly = "IVA-PAG-T";
    public const string RecapitulativeMonthly = "RECAP-M";
    public const string RecapitulativeQuarterly = "RECAP-T";
    public const string Withholdings = "RET-FONTE";
    public const string MonthlyPayroll = "DMR";
    public const string Modelo10 = "MOD10";
    public const string Modelo22 = "MOD22";
    public const string Ies = "IES";
    public const string AdvancePayment1 = "PPC-1";
    public const string AdvancePayment2 = "PPC-2";
    public const string AdvancePayment3 = "PPC-3";
    public const string StampDuty = "IS-PAG";
    public const string FiscalDossier = "DOSSIER";
    public const string InvoiceCommunication = "COM-FAT";

    public const string NotIncluded =
        "Ficam de fora até se confirmar o prazo em vigor no texto legal: a comunicação dos inventários (Decreto-Lei n.º 198/2012, artigo 3.º-A) e a declaração do RCBE.";

    private const string CivaSource = "Texto publicado no Portal das Finanças, consultado em 2026-10-05";

    public static readonly IReadOnlyList<ObligationDefinition> All =
    [
        new(VatReturnMonthly, "Declaração periódica de IVA (mensal)", ObligationFrequency.Mensal, 2, 20,
            "IVA no regime mensal.", $"CIVA, artigo 41.º, n.º 1, alínea a), na redação da Lei n.º 12/2022 ({CivaSource})."),
        new(VatPaymentMonthly, "Pagamento do IVA (mensal)", ObligationFrequency.Mensal, 2, 25,
            "IVA no regime mensal, quando há imposto a entregar.", $"CIVA, artigo 27.º, n.º 1, alínea a) ({CivaSource})."),
        new(VatReturnQuarterly, "Declaração periódica de IVA (trimestral)", ObligationFrequency.Trimestral, 2, 20,
            "IVA no regime trimestral.", $"CIVA, artigo 41.º, n.º 1, alínea b), na redação da Lei n.º 12/2022 ({CivaSource})."),
        new(VatPaymentQuarterly, "Pagamento do IVA (trimestral)", ObligationFrequency.Trimestral, 2, 25,
            "IVA no regime trimestral, quando há imposto a entregar.", $"CIVA, artigo 27.º, n.º 1, alínea b) ({CivaSource})."),
        new(RecapitulativeMonthly, "Declaração recapitulativa (mensal)", ObligationFrequency.Mensal, 1, 20,
            "Quando há transmissões intracomunitárias de bens, serviços intracomunitários ou operações triangulares no mês.", $"RITI, artigo 30.º, n.º 1, alínea a) ({CivaSource})."),
        new(RecapitulativeQuarterly, "Declaração recapitulativa (trimestral)", ObligationFrequency.Trimestral, 1, 20,
            "IVA trimestral com operações intracomunitárias no trimestre (acima de 50 000 € passa a mensal — RITI, artigo 30.º, n.º 2).", $"RITI, artigo 30.º, n.º 1, alínea b) ({CivaSource})."),
        new(Withholdings, "Entrega das retenções na fonte", ObligationFrequency.Mensal, 1, 20,
            "Quando há retenções no mês (contas 242).", $"CIRS, artigo 98.º, n.º 3 ({CivaSource})."),
        new(MonthlyPayroll, "Declaração mensal de remunerações (AT)", ObligationFrequency.Mensal, 1, 10,
            "Quando há rendimentos do trabalho dependente pagos no mês (contas 63).", $"CIRS, artigo 119.º, n.º 1, alínea c), subalínea i) ({CivaSource})."),
        new(Modelo10, "Declaração Modelo 10", ObligationFrequency.Anual, 2, null,
            "Quando houve retenções no ano anterior sobre rendimentos fora da declaração mensal.", $"CIRS, artigo 119.º, n.º 1, alínea c), subalínea ii) ({CivaSource})."),
        new(Modelo22, "Modelo 22 e pagamento do IRC", ObligationFrequency.Anual, 5, null,
            "Sujeitos passivos de IRC.", $"CIRC, artigos 120.º, n.º 1, e 104.º, n.º 1, alínea b) ({CivaSource}). Último dia de maio, seja ou não útil."),
        new(Ies, "IES — Declaração anual de informação contabilística e fiscal", ObligationFrequency.Anual, 7, 15,
            "Sujeitos passivos de IRC.", $"CIRC, artigo 121.º, n.º 2 ({CivaSource}). 15 de julho, seja ou não útil."),
        new(AdvancePayment1, "1.º pagamento por conta do IRC", ObligationFrequency.Anual, 7 - 12, null,
            "Quando o IRC liquidado no ano anterior obriga a pagamentos por conta.", $"CIRC, artigo 104.º, n.º 1, alínea a) ({CivaSource}). Vence em julho."),
        new(AdvancePayment2, "2.º pagamento por conta do IRC", ObligationFrequency.Anual, 9 - 12, null,
            "Quando o IRC liquidado no ano anterior obriga a pagamentos por conta.", $"CIRC, artigo 104.º, n.º 1, alínea a) ({CivaSource}). Vence em setembro."),
        new(AdvancePayment3, "3.º pagamento por conta do IRC", ObligationFrequency.Anual, 12 - 12, 15,
            "Quando o IRC liquidado no ano anterior obriga a pagamentos por conta.", $"CIRC, artigo 104.º, n.º 1, alínea a) ({CivaSource}). 15 de dezembro."),
        new(StampDuty, "Pagamento do Imposto do Selo", ObligationFrequency.Mensal, 1, 20,
            "Quando há imposto do selo liquidado no mês (indique-o à mão: os lançamentos não o mostram com segurança).", $"Código do Imposto do Selo, artigo 44.º, n.º 1 ({CivaSource})."),
        new(FiscalDossier, "Processo de documentação fiscal (dossier fiscal)", ObligationFrequency.Anual, 7, 15,
            "Sujeitos passivos de IRC.", $"CIRC, artigo 130.º, n.º 1: constituído até ao termo do prazo da declaração anual (IES), 15 de julho ({CivaSource})."),
        new(InvoiceCommunication, "Comunicação dos elementos das faturas", ObligationFrequency.Mensal, 1, 5,
            "Quando há vendas ou prestações de serviços no mês (contas 71 e 72).",
            "Decreto-Lei n.º 198/2012, artigo 3.º: «até ao dia 05 do mês seguinte» para os documentos emitidos desde 1 de janeiro de 2023, segundo a resposta oficial da AT (Portal das Finanças, questões frequentes, FAQ 00978, consultada em 2026-10-05); o texto do artigo em vigor não foi lido."),
    ];

    public static ObligationDefinition Find(string code) =>
        All.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Obrigação desconhecida: {code}.", nameof(code));

    /// <summary>
    /// Os períodos de cada obrigação cujo prazo cai entre <paramref name="from"/> e
    /// <paramref name="to"/> (inclusive). Os pagamentos por conta referem-se ao
    /// próprio ano; as outras obrigações anuais, ao ano anterior ao do prazo.
    /// </summary>
    public static IReadOnlyList<ObligationOccurrence> Between(DateOnly from, DateOnly to, IEnumerable<ObligationDefinition>? definitions = null)
    {
        if (to < from)
        {
            throw new ArgumentException("A data final não pode ser anterior à inicial.", nameof(to));
        }

        var result = new List<ObligationOccurrence>();
        foreach (var definition in definitions ?? All)
        {
            // Os períodos candidatos: do ano anterior ao de «from» até ao ano de «to».
            for (var year = from.Year - 2; year <= to.Year + 1; year++)
            {
                foreach (var (start, end, key, label) in Periods(definition.Frequency, year))
                {
                    var due = DueDate(definition, end);
                    if (due >= from && due <= to)
                    {
                        result.Add(new ObligationOccurrence(definition, key, label, start, end, due));
                    }
                }
            }
        }

        return result.OrderBy(item => item.DueDate).ThenBy(item => item.Definition.Code, StringComparer.Ordinal).ToList();
    }

    public static DateOnly DueDate(ObligationDefinition definition, DateOnly periodEnd)
    {
        var month = new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(definition.MonthsAfterPeriod);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        return new DateOnly(month.Year, month.Month, definition.Day is { } day ? Math.Min(day, days) : days);
    }

    private static IEnumerable<(DateOnly Start, DateOnly End, string Key, string Label)> Periods(ObligationFrequency frequency, int year)
    {
        var culture = CultureInfo.GetCultureInfo("pt-PT");
        switch (frequency)
        {
            case ObligationFrequency.Mensal:
                for (var month = 1; month <= 12; month++)
                {
                    var start = new DateOnly(year, month, 1);
                    var name = culture.DateTimeFormat.GetMonthName(month);
                    yield return (start, start.AddMonths(1).AddDays(-1), $"{year}-{month:00}", $"{char.ToUpper(name[0], culture)}{name[1..]} de {year}");
                }

                break;
            case ObligationFrequency.Trimestral:
                for (var quarter = 1; quarter <= 4; quarter++)
                {
                    var start = new DateOnly(year, ((quarter - 1) * 3) + 1, 1);
                    yield return (start, start.AddMonths(3).AddDays(-1), $"{year}-T{quarter}", $"{quarter}.º trimestre de {year}");
                }

                break;
            default:
                yield return (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), year.ToString(CultureInfo.InvariantCulture), $"Exercício de {year}");
                break;
        }
    }
}
