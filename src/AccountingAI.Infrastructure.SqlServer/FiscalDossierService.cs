using AccountingAI.Domain.Accounting;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>Um elemento do dossier fiscal de um exercício.</summary>
/// <param name="Number">O número na lista da portaria.</param>
/// <param name="Support"><c>aplicacao</c> (há um mapa na aplicação), <c>parcial</c> (há um mapa de apoio, não o de modelo oficial) ou <c>externo</c> (documento que não nasce da contabilidade).</param>
/// <param name="Link">O ecrã da aplicação que ajuda a preparar o elemento, quando existe.</param>
/// <param name="Evidence">O que a contabilidade do exercício mostra sobre o elemento (por exemplo, o movimento das contas que o tornam necessário).</param>
/// <param name="Status"><c>pendente</c>, <c>pronto</c> ou <c>nao-aplicavel</c>.</param>
public sealed record FiscalDossierElement(
    int Number, string Title, string Support, string? Link, string? LinkLabel, string? Evidence, string Status, string? Note);

public sealed record FiscalDossier(int Year, IReadOnlyList<FiscalDossierElement> Elements, int Pending, string Source, string Disclaimer);

/// <summary>
/// O índice do dossier fiscal — o processo de documentação fiscal do artigo
/// 130.º do CIRC — de um exercício: os elementos da lista oficial, o que a
/// contabilidade mostra sobre cada um, o ecrã da aplicação que ajuda a
/// prepará-lo e o estado que o utilizador lhe deu. A aplicação não gera os
/// mapas de modelo oficial (modelos 30, 31 e 32): diz onde estão os dados.
/// </summary>
public sealed class FiscalDossierService(AccountingDbContext context, FixedAssetService assets, Modelo22Service modelo22)
{
    public const string Source =
        "Portaria n.º 92-A/2011, de 28 de fevereiro, anexo I, na redação da Portaria n.º 51/2018, de 16 de fevereiro " +
        "(períodos de tributação iniciados em ou após 1 de janeiro de 2017) — elementos aplicáveis ao IRC; " +
        "texto reproduzido em informador.pt, consultado em 2026-10-04, por conferir no Diário da República.";

    public const string Disclaimer =
        "Índice de trabalho. A aplicação não produz os mapas de modelo oficial nem arquiva documentos: mostra o que a contabilidade indica " +
        "sobre cada elemento e guarda o estado que lhe der. O dossier tem de estar constituído até ao fim do prazo da IES.";

    private static readonly (int Number, string Title, string Support, string? Link, string? LinkLabel)[] Elements =
    [
        (1, "Relatório de gestão, parecer do conselho fiscal e documento de certificação legal de contas quando legalmente exigidos", "externo", null, null),
        (2, "Documentos, certificados e comunicações relativos a créditos cujo imposto foi deduzido (artigo 78.º do CIVA)", "externo", "/declaracao-iva", "Declaração de IVA (campo 40)"),
        (3, "Mapa, de modelo oficial, de provisões, perdas por imparidade em créditos e ajustamentos em inventários", "externo", null, null),
        (4, "Mapa, de modelo oficial, das mais-valias e menos-valias", "externo", null, null),
        (5, "Mapa, de modelo oficial, das depreciações e amortizações", "parcial", "/mapa-depreciacoes", "Mapa de depreciações"),
        (6, "Mapas, de modelo oficial, das depreciações de bens reavaliados ao abrigo de diploma legal", "externo", null, null),
        (7, "Mapas, de modelo oficial, da reavaliação efetuada nos termos do Decreto-Lei n.º 66/2016, de 3 de novembro", "externo", null, null),
        (8, "Mapa do apuramento do lucro tributável por regimes de tributação", "parcial", "/modelo-22", "Modelo 22 (quadro 07)"),
        (9, "Mapa de controlo de prejuízos no Regime Especial de Tributação de Grupos de Sociedades (artigo 71.º do CIRC)", "externo", null, null),
        (10, "Mapa de controlo da dedução de prejuízos fiscais (artigo 52.º do CIRC)", "parcial", "/modelo-22", "Modelo 22 (quadro 09)"),
        (11, "Mapa de reporte dos gastos de financiamento líquidos de períodos de tributação anteriores (artigos 67.º e 75.º-A do CIRC)", "externo", null, null),
        (12, "Outros documentos mencionados nos códigos, legislação complementar e instruções administrativas que devam integrar o processo de documentação fiscal", "externo", null, null),
    ];

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<FiscalDossier?> BuildAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var schedule = await assets.ScheduleAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        var irc = await modelo22.BuildAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        if (schedule is null || irc is null)
        {
            return null;
        }

        // O movimento do exercício (sem o apuramento) nas contas que tornam cada mapa necessário.
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var movements = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                (line.Account.Code.StartsWith("65") || line.Account.Code.StartsWith("67") || line.Account.Code.StartsWith("76") ||
                 line.Account.Code.StartsWith("6871") || line.Account.Code.StartsWith("7871") ||
                 line.Account.Code.StartsWith("69") || line.Account.Code.StartsWith("79")) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.JournalEntry.Source != EntrySource.Closing &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")))
            .GroupBy(line => line.Account.Code)
            .Select(group => new { Code = group.Key, Net = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        decimal Net(params string[] prefixes) => movements
            .Where(item => prefixes.Any(prefix => item.Code.StartsWith(prefix, StringComparison.Ordinal)))
            .Sum(item => item.Net);
        decimal Irc(string code) => irc.Fields.FirstOrDefault(field => field.Code == code)?.Amount ?? 0m;

        var impairments = Net("65");
        var provisions = Net("67");
        var reversals = -Net("76");
        var losses = Net("6871");
        var gains = -Net("7871");
        var financing = Net("69") + Net("79");
        var evidence = new Dictionary<int, string?>
        {
            [3] = impairments == 0m && provisions == 0m && reversals == 0m
                ? "Sem movimento nas contas 65, 67 e 76 no exercício."
                : $"Perdas por imparidade (65): {impairments:N2}; provisões (67): {provisions:N2}; reversões (76): {reversals:N2}.",
            [4] = losses == 0m && gains == 0m
                ? "Sem movimento nas contas 6871 e 7871 no exercício."
                : $"Menos-valias (6871): {losses:N2}; mais-valias (7871): {gains:N2}.",
            [5] = $"{schedule.Rows.Count} ativo(s) no mapa; depreciações calculadas: {schedule.TotalDepreciation:N2}; lançadas nas contas 64: {schedule.LedgerDepreciation:N2}.",
            [8] = $"Resultado líquido: {Irc("701"):N2}; lucro tributável: {Irc("778"):N2}; prejuízo fiscal: {Irc("777"):N2}. Só há um regime de tributação na aplicação.",
            [10] = $"Prejuízos reportáveis no início: {Irc("prejuizos-disponiveis"):N2}; deduzidos (campo 309): {Irc("309"):N2}; a reportar: {Irc("prejuizos-a-reportar"):N2}.",
            [11] = $"Gastos de financiamento líquidos do exercício (contas 69 menos 79): {financing:N2}.",
        };

        var saved = await context.FiscalDossierItems
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Year == year)
            .ToDictionaryAsync(item => item.Element, cancellationToken)
            .ConfigureAwait(false);

        var elements = Elements
            .Select(element =>
            {
                var item = saved.GetValueOrDefault(element.Number);
                return new FiscalDossierElement(
                    element.Number, element.Title, element.Support, element.Link, element.LinkLabel, evidence.GetValueOrDefault(element.Number),
                    StatusCode(item?.Status ?? FiscalDossierStatus.Pending), item?.Note);
            })
            .ToList();
        return new FiscalDossier(year, elements, elements.Count(element => element.Status == "pendente"), Source, Disclaimer);
    }

    /// <summary>Guarda o estado e a nota de um elemento num exercício.</summary>
    /// <returns>O dossier atualizado; <c>null</c> sem acesso à empresa.</returns>
    public async Task<FiscalDossier?> SetAsync(
        Guid userId, Guid companyId, int year, int element, string status, string? note, CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.Companies
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

        if (Elements.All(item => item.Number != element))
        {
            throw new ArgumentException("O elemento do dossier não existe.", nameof(element));
        }

        var parsed = status switch
        {
            "pendente" => FiscalDossierStatus.Pending,
            "pronto" => FiscalDossierStatus.Ready,
            "nao-aplicavel" => FiscalDossierStatus.NotApplicable,
            _ => throw new ArgumentException("O estado tem de ser «pendente», «pronto» ou «nao-aplicavel».", nameof(status)),
        };

        var existing = await context.FiscalDossierItems
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Year == year && item.Element == element, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            context.FiscalDossierItems.Add(FiscalDossierItem.Create(companyId, year, element, parsed, note));
        }
        else
        {
            existing.Set(parsed, note);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
    }

    private static string StatusCode(FiscalDossierStatus status) => status switch
    {
        FiscalDossierStatus.Ready => "pronto",
        FiscalDossierStatus.NotApplicable => "nao-aplicavel",
        _ => "pendente",
    };
}
