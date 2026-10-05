using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha do Modelo 32, pelas colunas do impresso (Portaria n.º 94/2013).</summary>
public sealed record Modelo32Line(
    string AssetCode,
    string? TaxTableCode,
    string Description,
    int? StartMonth,
    int StartYear,
    decimal BookValue,
    decimal TaxValue,
    int? UsefulLifeYears,
    decimal BookedInPeriod,
    decimal AcceptedInPriorPeriods,
    decimal Rate,
    decimal? CorrectedRate,
    decimal TaxLimit,
    decimal AcceptedImpairment,
    decimal? LostRates,
    decimal NotAccepted,
    decimal Recovered,
    bool RateFromAccounting);

/// <summary>Um Modelo 32 por natureza dos ativos (quadro 01) e método — aqui sempre quotas constantes.</summary>
public sealed record Modelo32Map(int Nature, string NatureLabel, int Method, IReadOnlyList<Modelo32Line> Lines)
{
    public decimal BookValue => Lines.Sum(line => line.BookValue);

    public decimal TaxValue => Lines.Sum(line => line.TaxValue);

    public decimal BookedInPeriod => Lines.Sum(line => line.BookedInPeriod);

    public decimal AcceptedInPriorPeriods => Lines.Sum(line => line.AcceptedInPriorPeriods);

    public decimal TaxLimit => Lines.Sum(line => line.TaxLimit);

    public decimal AcceptedImpairment => Lines.Sum(line => line.AcceptedImpairment);

    public decimal NotAccepted => Lines.Sum(line => line.NotAccepted);

    public decimal Recovered => Lines.Sum(line => line.Recovered);
}

/// <summary>Uma linha do Modelo 31, pelas colunas do impresso (Portaria n.º 92-A/2011).</summary>
public sealed record Modelo31Line(
    string AssetCode,
    string Description,
    DateOnly DisposalDate,
    decimal RealisationValue,
    decimal TaxAcquisitionValue,
    int AcquisitionYear,
    decimal BookAcquisitionValue,
    decimal BookDepreciation,
    decimal BookGain,
    decimal UntaxedGain,
    decimal AcceptedDepreciation,
    decimal Coefficient,
    bool CoefficientIndicated,
    decimal TaxGain);

public sealed record Modelo31Map(int Nature, string NatureLabel, IReadOnlyList<Modelo31Line> Lines)
{
    public decimal BookGain => Lines.Sum(line => line.BookGain);

    public decimal TaxGain => Lines.Sum(line => line.TaxGain);
}

/// <summary>
/// Os mapas de modelo oficial do dossier fiscal de um exercício que saem do
/// cadastro dos ativos: o Modelo 32 e o Modelo 31, com os avisos do que a
/// aplicação não sabe e o utilizador tem de confirmar.
/// </summary>
public sealed record DossierMaps(
    int Year,
    IReadOnlyList<Modelo32Map> Modelo32,
    decimal LedgerDepreciation,
    int FullyDepreciatedOmitted,
    IReadOnlyList<Modelo31Map> Modelo31,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Sources)
{
    public decimal Modelo32Booked => Modelo32.Sum(map => map.BookedInPeriod);

    public decimal Modelo32NotAccepted => Modelo32.Sum(map => map.NotAccepted);

    public decimal Modelo31BookGain => Modelo31.Sum(map => map.BookGain);

    public decimal Modelo31TaxGain => Modelo31.Sum(map => map.TaxGain);
}

/// <summary>
/// O Modelo 32 (mapa de depreciações e amortizações, Portaria n.º 94/2013) e o
/// Modelo 31 (mapa de mais-valias e menos-valias, Portaria n.º 92-A/2011) de um
/// exercício, a partir do cadastro de ativos fixos (F3-14B).
/// <para>
/// Modelo 32: uma linha por ativo em uso no exercício, por natureza (a da conta
/// do ativo: 43 tangíveis, 44 intangíveis, 42 propriedades de investimento, 37
/// biológicos não consumíveis), sempre pelo método das quotas constantes — o
/// único da aplicação. Coluna 8 é a depreciação calculada do exercício (a do
/// lançamento do F3-12B); coluna 12 o limite fiscal pela taxa máxima indicada no
/// ativo (sem ela, a da contabilidade); coluna 15 = (8) − [(12) + (13)]; coluna
/// 9 o aceite em exercícios anteriores. As perdas por imparidade (13), a
/// recuperação (16) e as taxas perdidas (14) não se calculam.
/// </para>
/// <para>
/// Modelo 31: uma linha por ativo alienado ou abatido no exercício pela
/// aplicação (F3-12C); coluna 8 = (2) − [(5) − (6)] e coluna 13 =
/// (2) − [((3) − (9) − (10)) × (11)], com o coeficiente de desvalorização
/// indicado (artigo 47.º do CIRC) ou 1.
/// </para>
/// </summary>
public sealed class DossierMapsService(AccountingDbContext context)
{
    public const string Modelo32Source = "Portaria n.º 94/2013, de 4 de março, anexo I (impresso e instruções do Modelo 32) — Diário da República n.º 44, 1.ª série.";

    public const string Modelo31Source = "Portaria n.º 92-A/2011, de 28 de fevereiro (impresso e instruções do Modelo 31) — Diário da República n.º 41, 1.ª série, suplemento.";

    public async Task<DossierMaps?> BuildAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 9999)
        {
            throw new ArgumentException("Indique um exercício válido.", nameof(year));
        }

        var hasAccess = await context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var assets = await context.FixedAssets
            .AsNoTracking()
            .Include(item => item.AssetAccount)
            .Where(item => item.CompanyId == companyId && item.StartOfUseDate.Year <= year)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var warnings = new List<string>();
        var omitted = 0;
        var lines32 = new List<(int Nature, Modelo32Line Line)>();
        foreach (var asset in assets)
        {
            if (asset.DisposalDate is { } disposal && disposal.Year < year)
            {
                continue;
            }

            var booked = asset.DepreciationFor(year);
            if (booked == 0m && asset.AccumulatedAtEndOf(year - 1) >= asset.DepreciableValue)
            {
                // Totalmente depreciado: a alínea a) das instruções permite não o incluir.
                omitted++;
                continue;
            }

            var limit = asset.FiscalLimitFor(year);
            lines32.Add((NatureOf(asset), new Modelo32Line(
                asset.Code,
                asset.TaxTableCode,
                asset.Description,
                asset.ByMonths ? asset.StartOfUseDate.Month : null,
                asset.StartOfUseDate.Year,
                asset.AcquisitionValue,
                asset.AcquisitionValue,
                null,
                booked,
                asset.FiscalAcceptedAtEndOf(year - 1),
                asset.TaxRate ?? asset.AnnualRate,
                null,
                limit,
                0m,
                null,
                Math.Max(0m, booked - limit),
                0m,
                asset.TaxRate is null)));
        }

        var withoutRate = lines32.Where(item => item.Line.RateFromAccounting).Select(item => item.Line.AssetCode).ToList();
        if (withoutRate.Count > 0)
        {
            warnings.Add($"Sem taxa máxima fiscal indicada — o limite (12) usa a taxa da contabilidade e nada fica por aceitar: {string.Join(", ", withoutRate)}. Indique a taxa das tabelas do DR n.º 25/2009 na ficha do ativo.");
        }

        var withoutCode = lines32.Where(item => item.Line.TaxTableCode is null).Select(item => item.Line.AssetCode).ToList();
        if (withoutCode.Count > 0)
        {
            warnings.Add($"Sem código das tabelas do DR n.º 25/2009 (coluna 1): {string.Join(", ", withoutCode)}.");
        }

        var unknownNature = lines32.Where(item => item.Nature == 0).Select(item => item.Line.AssetCode).ToList();
        if (unknownNature.Count > 0)
        {
            warnings.Add($"Sem conta do ativo em 37, 42, 43 ou 44 — a natureza não se determina: {string.Join(", ", unknownNature)}.");
        }

        var modelo32 = lines32
            .GroupBy(item => item.Nature)
            .OrderBy(group => group.Key == 0 ? 99 : group.Key)
            .Select(group => new Modelo32Map(group.Key, NatureLabel(group.Key), 1, group.Select(item => item.Line).ToList()))
            .ToList();

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var ledgerDepreciation = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Closing &&
                line.JournalEntry.EntryDate >= yearStart &&
                line.JournalEntry.EntryDate <= yearEnd &&
                line.Account.Code.StartsWith("64"))
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;
        var booked32 = modelo32.Sum(map => map.BookedInPeriod);
        if (ledgerDepreciation != booked32)
        {
            warnings.Add($"As depreciações do mapa ({booked32:N2}) não batem com os gastos lançados nas contas 64 ({ledgerDepreciation:N2}): a coluna 8 deve ser o contabilizado.");
        }

        var disposed = assets.Where(asset => asset.DisposalDate is { } date && date.Year == year).ToList();
        var lines31 = new List<(int Nature, Modelo31Line Line)>();
        foreach (var asset in disposed)
        {
            if (asset.DisposalPrice is not { } price)
            {
                warnings.Add($"O ativo {asset.Code} tem data de alienação mas a saída não foi lançada pela aplicação: o preço não é conhecido e fica fora do Modelo 31.");
                continue;
            }

            var date = asset.DisposalDate!.Value;
            var bookDepreciation = asset.AccumulatedAtEndOf(year);
            var accepted = asset.FiscalAcceptedAtEndOf(year);
            var coefficient = asset.MonetaryCoefficient ?? 1m;
            var taxGain = decimal.Round(price - ((asset.AcquisitionValue - accepted) * coefficient), 2, MidpointRounding.AwayFromZero);
            lines31.Add((NatureOf(asset), new Modelo31Line(
                asset.Code, asset.Description, date, price, asset.AcquisitionValue, asset.AcquisitionDate.Year, asset.AcquisitionValue,
                bookDepreciation, price - (asset.AcquisitionValue - bookDepreciation), 0m, accepted, coefficient,
                asset.MonetaryCoefficient is not null, taxGain)));
        }

        var withoutCoefficient = lines31.Where(item => !item.Line.CoefficientIndicated && CoefficientMayApply(item.Line)).Select(item => item.Line.AssetCode).ToList();
        if (withoutCoefficient.Count > 0)
        {
            warnings.Add($"Sem coeficiente de desvalorização da moeda (coluna 11) — conta como 1: {string.Join(", ", withoutCoefficient)}. Indique o da portaria do ano, quando aplicável (artigo 47.º do CIRC).");
        }

        var modelo31 = lines31
            .GroupBy(item => item.Nature)
            .OrderBy(group => group.Key == 0 ? 99 : group.Key)
            .Select(group => new Modelo31Map(group.Key, NatureLabel(group.Key), group.Select(item => item.Line).ToList()))
            .ToList();

        return new DossierMaps(year, modelo32, decimal.Round(ledgerDepreciation, 2), omitted, modelo31, warnings, [Modelo32Source, Modelo31Source]);

        // Não se avisa nos bens adquiridos e vendidos no mesmo ano, em que o coeficiente não muda o resultado.
        static bool CoefficientMayApply(Modelo31Line line) => line.DisposalDate.Year > line.AcquisitionYear;
    }

    /// <summary>A natureza do quadro 01 pela conta do ativo: 1 tangíveis (43), 2 intangíveis (44), 3 biológicos (37), 4 propriedades de investimento (42).</summary>
    private static int NatureOf(FixedAsset asset)
    {
        var code = asset.AssetAccount?.Code ?? string.Empty;
        return code.StartsWith("43", StringComparison.Ordinal) ? 1
            : code.StartsWith("44", StringComparison.Ordinal) ? 2
            : code.StartsWith("37", StringComparison.Ordinal) ? 3
            : code.StartsWith("42", StringComparison.Ordinal) ? 4
            : 0;
    }

    private static string NatureLabel(int nature) => nature switch
    {
        1 => "Ativos fixos tangíveis",
        2 => "Ativos intangíveis",
        3 => "Ativos biológicos não consumíveis",
        4 => "Propriedades de investimento",
        _ => "Natureza por determinar",
    };
}
