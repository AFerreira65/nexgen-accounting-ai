using System.Text.Json;
using NexGenAccountingAI.Application.Fiscal;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um campo do quadro 07 no catálogo oficial.</summary>
/// <param name="Group"><c>variacoes</c> (campos 702 a 707, 805 e 806), <c>acrescer</c> ou <c>deduzir</c>.</param>
/// <param name="Side">O efeito no lucro tributável: <c>acrescer</c> ou <c>deduzir</c>.</param>
public sealed record Modelo22CatalogField(int Number, string Group, string Side, string Label);

/// <summary>Um campo do Modelo 22 com o seu valor.</summary>
/// <param name="Code">O número do campo no impresso (<c>724</c>, <c>347-A</c>…) ou o nome de um parâmetro.</param>
/// <param name="Computed">Calculado pela aplicação; os restantes são indicados pelo utilizador.</param>
/// <param name="Suggested">Um valor que a contabilidade sugere para o campo, com a origem em <paramref name="SuggestionSource"/>; nunca é aplicado sozinho.</param>
public sealed record Modelo22Field(
    string Code, string Section, string Label, decimal Amount, bool Computed, string? Note = null, decimal? Suggested = null, string? SuggestionSource = null);

public sealed record Modelo22Input(string Field, decimal Amount, string? Note = null);

public sealed record Modelo22Return(
    int Year,
    bool SmallOrMediumEnterprise,
    decimal MunicipalSurtaxRate,
    IReadOnlyList<Modelo22Field> Fields,
    IReadOnlyList<string> Warnings,
    string Source,
    string Disclaimer);

/// <summary>
/// Prepara o apuramento do Modelo 22 de um exercício: quadro 07 (do resultado
/// líquido ao lucro tributável), quadro 09 (matéria coletável, regime geral) e
/// quadro 10 (cálculo do imposto). O resultado líquido vem da contabilidade;
/// os acréscimos, as deduções e os restantes valores são indicados pelo
/// utilizador e guardados por exercício; as somas seguem as fórmulas escritas
/// no impresso. As taxas vêm das tabelas fiscais do exercício. É um mapa de
/// trabalho: não é a declaração entregue nem foi validado pela AT.
/// </summary>
public sealed class Modelo22Service(AccountingDbContext context, FinancialStatementsService statements)
{
    public const string SmeField = "pme";

    public const string MunicipalSurtaxRateField = "derrama-municipal-taxa";

    /// <summary>O saldo de prejuízos fiscais reportáveis no início do exercício, quando o utilizador o indica em vez de o deixar transportar.</summary>
    public const string OpeningLossesField = "prejuizos-iniciais";

    /// <summary>O prefixo dos encargos sujeitos a tributação autónoma: <c>ta-</c> seguido da chave da tabela fiscal.</summary>
    public const string AutonomousPrefix = "ta-";

    /// <summary>
    /// A parte do lucro tributável que os prejuízos deduzidos não podem exceder
    /// em cada período: CIRC, artigo 52.º, n.º 2, na redação da Lei n.º
    /// 24-D/2022, de 30 de dezembro (texto do Portal das Finanças, consultado
    /// em 2026-10-04) — períodos com início em ou após 1 de janeiro de 2023.
    /// </summary>
    public const decimal LossDeductionLimit = 0.65m;

    private const int LossDeductionLimitFirstYear = 2023;

    // Até quantos exercícios se recua para transportar os prejuízos, quando o saldo inicial não é indicado.
    private const int LossCarryYears = 10;

    private const string SurchargeKey = "agravamento-prejuizo-fiscal";

    public const string Disclaimer =
        "Mapa de trabalho, não validado pela AT. Cobre o regime geral de um sujeito passivo residente: sem regimes de redução de taxa, " +
        "sem as taxas das Regiões Autónomas (campos 350 e 370), sem imposto a outras taxas (campo 349), sem grupos de sociedades e sem os anexos. " +
        "Dos limites legais só verifica o da dedução de prejuízos (65 % do lucro tributável); os prejuízos anteriores a 2023, que tinham prazo de reporte, " +
        "e os limites dos benefícios e da derrama municipal não são verificados.";

    private const string Q07 = "Quadro 07", Q09 = "Quadro 09", Q10 = "Quadro 10", QTA = "Tributações autónomas", Q11 = "Quadro 11", Rosto = "Rosto";

    // Os campos dos quadros 09 e 10 que o utilizador indica, com a designação do impresso.
    /// <summary>As rubricas de rendimentos da DR por naturezas que formam o campo 410 (a primeira é o volume de negócios).</summary>
    private static readonly string[] IncomeLines =
    [
        "Vendas e serviços prestados", "Subsídios à exploração",
        "Ganhos / perdas imputados de subsidiárias, associadas e empreendimentos conjuntos",
        "Variação nos inventários da produção", "Trabalhos para a própria entidade",
        "Aumentos / reduções de justo valor", "Outros rendimentos", "Juros e rendimentos similares obtidos",
    ];

    private static readonly (string Code, string Section, string Label)[] UserFields =
    [
        ("309", Q09, "Prejuízos fiscais deduzidos"),
        ("310", Q09, "Benefícios fiscais"),
        ("353", Q10, "Dupla tributação jurídica internacional (DTJI - art.º 91.º)"),
        ("375", Q10, "Dupla tributação económica internacional (art.º 91.º-A)"),
        ("355", Q10, "Benefícios fiscais"),
        ("470", Q10, "Adicional ao Imposto Municipal sobre Imóveis (art.º 135.º-J do CIMI)"),
        ("356", Q10, "Pagamento especial por conta (art.º 93.º)"),
        ("371", Q10, "Resultado da liquidação (art.º 92.º)"),
        ("359", Q10, "Retenções na fonte"),
        ("360", Q10, "Pagamentos por conta (art.º 105.º)"),
        ("374", Q10, "Pagamentos adicionais por conta (art.º 105.º-A)"),
        ("363", Q10, "IRC de períodos anteriores"),
        ("372", Q10, "Reposição de benefícios fiscais"),
        ("379", Q10, "Dupla tributação jurídica internacional (art.º 91.º) - Países com CDT e quando DTJI > 378"),
        ("365", Q10, "Tributações autónomas"),
        ("366", Q10, "Juros compensatórios"),
        ("369", Q10, "Juros de mora"),
        ("348", Q10, "Taxa do imposto a outras taxas (%)"),
        ("349", Q10, "Imposto a outras taxas"),
        ("350", Q10, "Imposto imputável à Região Autónoma dos Açores"),
        ("370", Q10, "Imposto imputável à Região Autónoma da Madeira"),
        // O rosto do impresso (F4C-02): assinalamentos guardados como números.
        ("tipo-sujeito-passivo", Rosto, "Quadro 03 — tipo de sujeito passivo (1 a 4)"),
        ("tipo-declaracao", Rosto, "Quadro 04 — tipo de declaração (1 a 6)"),
        ("nif-representante", Rosto, "Quadro 05 — NIF do representante legal"),
        ("nif-cc", Rosto, "Quadro 05 — NIF do contabilista certificado"),
        ("regime-reducao-taxa", Rosto, "Quadro 08.1 — campo assinalado (242 a 265) ou 0"),
        ("regime-geral", Rosto, "Quadro 08.2 — campo assinalado (246 a 268) ou 0"),
    ];

    private static readonly Lazy<(string Source, IReadOnlyList<Modelo22CatalogField> Fields)> Catalog = new(LoadCatalog);

    /// <summary>A fonte dos campos e das fórmulas.</summary>
    public static string Source => Catalog.Value.Source;

    /// <summary>Os campos do quadro 07 que o utilizador pode preencher, pela ordem do impresso.</summary>
    public static IReadOnlyList<Modelo22CatalogField> Quadro07Fields => Catalog.Value.Fields;

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<Modelo22Return?> BuildAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        // Os prejuízos reportáveis vêm dos exercícios anteriores: apura-se cada um, do mais antigo para o mais recente.
        var firstEntry = await context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId && entry.Status != JournalEntryStatus.Voided)
            .MinAsync(entry => (DateOnly?)entry.EntryDate, cancellationToken)
            .ConfigureAwait(false);
        var firstSaved = await context.IrcReturnValues
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .MinAsync(item => (int?)item.Year, cancellationToken)
            .ConfigureAwait(false);
        var first = Math.Max(year - LossCarryYears, Math.Min(firstEntry?.Year ?? year, firstSaved ?? year));
        var carried = 0m;
        for (var previous = first; previous < year; previous++)
        {
            var computed = await ComputeAsync(userId, companyId, previous, carried, cancellationToken).ConfigureAwait(false);
            if (computed is null)
            {
                return null;
            }

            carried = computed.Value.ClosingLosses;
        }

        return (await ComputeAsync(userId, companyId, year, carried, cancellationToken).ConfigureAwait(false))?.Return;
    }

    private async Task<(Modelo22Return Return, decimal ClosingLosses)?> ComputeAsync(
        Guid userId, Guid companyId, int year, decimal carriedLosses, CancellationToken cancellationToken)
    {
        var income = await statements
            .IncomeStatementAsync(userId, companyId, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken)
            .ConfigureAwait(false);
        if (income is null)
        {
            return null;
        }

        var saved = await context.IrcReturnValues
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Year == year)
            .ToDictionaryAsync(item => item.Field, item => (item.Amount, item.Note), cancellationToken)
            .ConfigureAwait(false);
        decimal Saved(string code) => saved.TryGetValue(code, out var value) ? decimal.Round(value.Amount, 2) : 0m;

        var suggestions = await SuggestionsAsync(companyId, year, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();
        var fields = new List<Modelo22Field>();
        decimal Computed(string code, string section, string label, decimal amount)
        {
            fields.Add(new Modelo22Field(code, section, label, amount, true));
            return amount;
        }

        // Quadro 07 — do resultado líquido ao lucro tributável.
        var net = Computed("701", Q07, "Resultado líquido do período", income.NetResult);
        var catalog = Quadro07Fields;
        decimal Sum(string group, string side) => catalog.Where(field => field.Group == group && field.Side == side).Sum(field => Saved(Code(field)));
        void User(IEnumerable<Modelo22CatalogField> group)
        {
            foreach (var field in group)
            {
                var code = Code(field);
                var suggestion = suggestions.GetValueOrDefault(field.Number);
                fields.Add(new Modelo22Field(
                    code, Q07, field.Label, Saved(code), false, saved.GetValueOrDefault(code).Note,
                    suggestion.Amount == 0m ? null : suggestion.Amount, suggestion.Source));
            }
        }

        User(catalog.Where(field => field.Group == "variacoes"));
        var field708 = Computed("708", Q07, "Soma (701 + 702 + 703 − 704 − 705 + 706 − 707 + 805 − 806)",
            net + Sum("variacoes", "acrescer") - Sum("variacoes", "deduzir"));
        User(catalog.Where(field => field.Group == "acrescer"));
        var field753 = Computed("753", Q07, "Soma (campos 708 a 752)", field708 + Sum("acrescer", "acrescer"));
        User(catalog.Where(field => field.Group == "deduzir"));
        var field776 = Computed("776", Q07, "Soma (campos 754 a 775)", Sum("deduzir", "deduzir"));
        var loss = Computed("777", Q07, "Prejuízo para efeitos fiscais (se 776 > 753)", Math.Max(0m, field776 - field753));
        var profit = Computed("778", Q07, "Lucro tributável (se 753 ≥ 776)", Math.Max(0m, field753 - field776));

        // Quadro 09 — matéria coletável, regime geral.
        void UserField(string code)
        {
            var definition = UserFields.Single(item => item.Code == code);
            fields.Add(new Modelo22Field(code, definition.Section, definition.Label, Saved(code), false, saved.GetValueOrDefault(code).Note));
        }

        Computed("301", Q09, "Prejuízo fiscal (transporte do quadro 07)", loss);
        Computed("302", Q09, "Lucro tributável (transporte do quadro 07)", profit);

        // Prejuízos reportáveis: o saldo indicado para o exercício ou, sem ele, o transportado dos anteriores.
        fields.Add(new Modelo22Field(
            OpeningLossesField, Q09, "Prejuízos fiscais reportáveis no início do exercício — indicar só para substituir o valor transportado",
            Saved(OpeningLossesField), false, saved.GetValueOrDefault(OpeningLossesField).Note));
        var openingLosses = Computed(
            "prejuizos-disponiveis", Q09, "Prejuízos fiscais reportáveis no início do exercício",
            saved.ContainsKey(OpeningLossesField) ? Saved(OpeningLossesField) : carriedLosses);
        decimal? lossLimit = year >= LossDeductionLimitFirstYear
            ? Computed("309-limite", Q09, $"Limite da dedução de prejuízos ({LossDeductionLimit:P0} do lucro tributável, art.º 52.º, n.º 2)",
                decimal.Round(profit * LossDeductionLimit, 2, MidpointRounding.AwayFromZero))
            : null;
        var deductible = Math.Min(openingLosses, lossLimit ?? profit);
        fields.Add(new Modelo22Field(
            "309", Q09, "Prejuízos fiscais deduzidos", Saved("309"), false, saved.GetValueOrDefault("309").Note,
            deductible > 0m && Saved("309") == 0m ? deductible : null, "prejuízos reportáveis, até ao limite"));
        if (Saved("309") > openingLosses)
        {
            warnings.Add($"O campo 309 ({Saved("309"):N2}) excede os prejuízos fiscais reportáveis no início do exercício ({openingLosses:N2}).");
        }

        if (lossLimit is { } limit && Saved("309") > limit)
        {
            warnings.Add($"O campo 309 ({Saved("309"):N2}) excede {LossDeductionLimit:P0} do lucro tributável ({limit:N2}), o máximo dedutível em cada período (CIRC, art.º 52.º, n.º 2).");
        }

        UserField("310");
        var deductions = Saved("309") + Saved("310");
        if (deductions > profit)
        {
            warnings.Add("Os prejuízos fiscais e os benefícios deduzidos no quadro 09 excedem o lucro tributável: a matéria coletável fica a zero.");
        }

        var taxable = Computed("311", Q09, "Matéria coletável (302 − 309 − 310)", Math.Max(0m, profit - deductions));
        var closingLosses = Computed(
            "prejuizos-a-reportar", Q09, "Prejuízos fiscais a reportar para os exercícios seguintes",
            Math.Max(0m, openingLosses + loss - Saved("309")));

        // Quadro 10 — cálculo do imposto, com as taxas do exercício.
        var sme = Saved(SmeField) != 0m;
        var municipalRate = saved.TryGetValue(MunicipalSurtaxRateField, out var rate) ? rate.Amount : 0m;
        var table = FiscalTableCatalog.For(year);
        decimal field347A = 0m, field347B = 0m, stateSurtax = 0m;
        if (table is null)
        {
            warnings.Add($"Não há tabelas fiscais para {year}: o imposto à taxa normal e a derrama estadual não foram calculados.");
        }
        else
        {
            var reduced = sme ? Math.Min(taxable, table.Irc.SmeThreshold) : 0m;
            field347A = decimal.Round(reduced * table.Irc.SmeRate, 2, MidpointRounding.AwayFromZero);
            field347B = decimal.Round((taxable - reduced) * table.Irc.GeneralRate, 2, MidpointRounding.AwayFromZero);
            stateSurtax = table.StateSurtaxOn(profit);
            if (table.CarriedForward)
            {
                warnings.Add($"As taxas de {year} ainda não estão nas tabelas fiscais: foram usadas as do último ano conhecido.");
            }

            Computed("347-A", Q10, $"Imposto à taxa do art.º 87.º, n.º 2 — PME, primeiros {table.Irc.SmeThreshold:N0} € de matéria coletável × {table.Irc.SmeRate:P0}", field347A);
            Computed("347-B", Q10, $"Imposto à taxa normal (art.º 87.º, n.º 1) × {table.Irc.GeneralRate:P0}", field347B);
        }

        // Tributações autónomas: os encargos indicados por tipo, às taxas do exercício.
        var autonomous = Saved("365");
        var autonomousComputed = false;
        if (table is { AutonomousTaxation.Count: > 0 })
        {
            var surcharge = loss > 0m ? table.AutonomousTaxation.FirstOrDefault(item => item.Key == SurchargeKey)?.Rate ?? 0m : 0m;
            var sum = 0m;
            foreach (var item in table.AutonomousTaxation.Where(item => item.Key != SurchargeKey))
            {
                var code = AutonomousPrefix + item.Key;
                fields.Add(new Modelo22Field(
                    code, QTA, $"{item.Description} — encargos sujeitos, a {item.Rate + surcharge:P1}", Saved(code), false, saved.GetValueOrDefault(code).Note));
                sum += decimal.Round(Saved(code) * (item.Rate + surcharge), 2, MidpointRounding.AwayFromZero);
                autonomousComputed |= Saved(code) != 0m;
            }

            Computed("ta-total", QTA, "Tributações autónomas (transporta para o campo 365 quando há encargos indicados)", sum);
            if (autonomousComputed)
            {
                if (Saved("365") != 0m)
                {
                    warnings.Add("Há encargos indicados nas tributações autónomas: o campo 365 passa a ser o total calculado e o valor que tinha sido indicado é ignorado.");
                }

                if (surcharge > 0m)
                {
                    warnings.Add($"Com prejuízo fiscal, as taxas de tributação autónoma foram agravadas em {surcharge * 100m:0} pontos percentuais (art.º 88.º, n.º 14); confirme se alguma exceção se aplica.");
                }

                autonomous = sum;
            }
        }

        UserField("348");
        UserField("349");
        UserField("350");
        UserField("370");
        var field351 = Computed("351", Q10, "Coleta (347-A + 347-B + 349 + 350 + 370)", field347A + field347B + Saved("349") + Saved("350") + Saved("370"));
        Computed("373", Q10, "Derrama estadual (art.º 87.º-A)", stateSurtax);
        var field378 = Computed("378", Q10, "Coleta total (351 + 373)", field351 + stateSurtax);
        foreach (var code in new[] { "353", "375", "355", "470", "356" })
        {
            UserField(code);
        }

        var claimed = Saved("353") + Saved("375") + Saved("355") + Saved("470") + Saved("356");
        if (claimed > field378)
        {
            warnings.Add("As deduções à coleta (campos 353, 375, 355, 470 e 356) excedem a coleta total: o campo 357 fica limitado ao campo 378.");
        }

        var field357 = Computed("357", Q10, "Total das deduções (353 + 375 + 355 + 356 + 470) ≤ 378", Math.Min(claimed, field378));
        var field358 = Computed("358", Q10, "Total do IRC liquidado (378 − 357)", field378 - field357);
        foreach (var code in new[] { "371", "359", "360", "374" })
        {
            UserField(code);
        }

        var balance = field358 + Saved("371") - Saved("359") - Saved("360") - Saved("374");
        Computed("361", Q10, "IRC a pagar (358 + 371 − 359 − 360 − 374) > 0", Math.Max(0m, balance));
        Computed("362", Q10, "IRC a recuperar (358 + 371 − 359 − 360 − 374) < 0", Math.Max(0m, -balance));
        UserField("363");
        UserField("372");
        var municipal = Computed(
            "364", Q10, $"Derrama municipal ({municipalRate:0.####} % do lucro tributável)",
            decimal.Round(profit * municipalRate / 100m, 2, MidpointRounding.AwayFromZero));
        UserField("379");
        if (autonomousComputed)
        {
            Computed("365", Q10, "Tributações autónomas", autonomous);
        }
        else
        {
            UserField("365");
        }

        UserField("366");
        UserField("369");

        var total = balance + Saved("363") + Saved("372") + municipal - Saved("379") + autonomous + Saved("366") + Saved("369");
        Computed("367", Q10, "Total a pagar [361 ou (−362) + 363 + 372 + 364 − 379 + 365 + 366 + 369] > 0", Math.Max(0m, total));
        Computed("368", Q10, "Total a recuperar [(−362) + 363 + 372 + 364 − 379 + 365 + 366 + 369] < 0", Math.Max(0m, -total));

        // Quadro 11 — outras informações, da demonstração de resultados; e o rosto.
        decimal Line(string label) => income.Lines.FirstOrDefault(line => line.Label == label)?.Amount ?? 0m;
        var turnover = Line("Vendas e serviços prestados");
        Computed("411", Q11, "Volume de negócios do período", turnover);
        Computed("410", Q11, "Total de rendimentos do período", turnover + IncomeLines.Skip(1).Sum(label => Math.Max(0m, Line(label))));
        foreach (var code in new[] { "tipo-sujeito-passivo", "tipo-declaracao", "nif-representante", "nif-cc", "regime-reducao-taxa", "regime-geral" })
        {
            UserField(code);
        }

        if (income.Excluded.Count > 0)
        {
            warnings.Add($"Há {income.Excluded.Count} conta(s) de gastos ou rendimentos fora da demonstração de resultados: o resultado líquido do campo 701 pode estar incompleto.");
        }

        if (suggestions.TryGetValue(724, out var estimate) && estimate.Amount != 0m && Saved("724") == 0m)
        {
            warnings.Add($"A contabilidade tem {estimate.Amount:N2} de imposto estimado ({estimate.Source}) e o campo 724 está a zero: o imposto do período acresce ao resultado líquido.");
        }

        if (profit > 0m && municipalRate == 0m)
        {
            warnings.Add("A taxa da derrama municipal está a zero: confirme a taxa do município (ofício-circulado anual da AT) e o Anexo A, se houver mais de um município.");
        }

        return (new Modelo22Return(year, sme, municipalRate, fields, warnings, Source, Disclaimer), closingLosses);
    }

    /// <summary>O ficheiro XML do Modelo 22 do exercício, pelo esquema oficial da AT (F4C-03).</summary>
    /// <returns><c>null</c> sem acesso à empresa. Lança <see cref="InvalidOperationException"/> quando a declaração não pode ir para ficheiro.</returns>
    public async Task<(byte[] Content, string FileName)?> BuildXmlAsync(
        Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var model = await BuildAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return null;
        }

        var taxNumber = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegistrationNumber)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return (Modelo22XmlWriter.Write(model, taxNumber), $"modelo22-{taxNumber}-{year}.xml");
    }

    /// <summary>
    /// Substitui os valores do exercício pelos indicados: os campos a zero
    /// deixam de estar guardados. Recusa campos que não sejam do utilizador.
    /// </summary>
    /// <returns>O apuramento recalculado; <c>null</c> sem acesso à empresa.</returns>
    public async Task<Modelo22Return?> SaveAsync(
        Guid userId, Guid companyId, int year, IReadOnlyList<Modelo22Input> values, CancellationToken cancellationToken = default)
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

        var allowed = Quadro07Fields.Select(Code)
            .Concat(UserFields.Select(field => field.Code))
            .Append(SmeField)
            .Append(MunicipalSurtaxRateField)
            .Append(OpeningLossesField)
            .Concat((FiscalTableCatalog.For(year)?.AutonomousTaxation ?? [])
                .Where(item => item.Key != SurchargeKey)
                .Select(item => AutonomousPrefix + item.Key))
            .ToHashSet(StringComparer.Ordinal);
        var incoming = new Dictionary<string, Modelo22Input>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var code = (value.Field ?? string.Empty).Trim().ToLowerInvariant();
            if (!allowed.Contains(code))
            {
                throw new ArgumentException($"O campo «{value.Field}» não existe ou é calculado pela aplicação.", nameof(values));
            }

            if (!incoming.TryAdd(code, value))
            {
                throw new ArgumentException($"O campo {code} está repetido.", nameof(values));
            }
        }

        if (incoming.TryGetValue(MunicipalSurtaxRateField, out var municipal) && municipal.Amount > 100m)
        {
            throw new ArgumentException("A taxa da derrama municipal é uma percentagem.", nameof(values));
        }

        var existing = await context.IrcReturnValues
            .Where(item => item.CompanyId == companyId && item.Year == year)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in existing)
        {
            if (incoming.Remove(item.Field, out var value) && value.Amount != 0m)
            {
                item.Set(value.Amount, value.Note);
            }
            else
            {
                context.IrcReturnValues.Remove(item);
            }
        }

        foreach (var (code, value) in incoming.Where(item => item.Value.Amount != 0m))
        {
            context.IrcReturnValues.Add(IrcReturnValue.Create(companyId, year, code, value.Amount, value.Note));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// O que a contabilidade sugere para alguns campos, pelas contas do SNC: o
    /// imposto estimado (8121) para o campo 724 e o imposto diferido (8122)
    /// para o 725 ou o 766, conforme o saldo.
    /// </summary>
    private async Task<Dictionary<int, (decimal Amount, string Source)>> SuggestionsAsync(Guid companyId, int year, CancellationToken cancellationToken)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var balances = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                (line.Account.Code.StartsWith("8121") || line.Account.Code.StartsWith("8122")) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                line.JournalEntry.Source != EntrySource.Closing &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")))
            .GroupBy(line => line.Account.Code.Substring(0, 4))
            .Select(group => new { Code = group.Key, Net = group.Sum(line => line.Debit - line.Credit) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var suggestions = new Dictionary<int, (decimal Amount, string Source)>();
        var current = balances.SingleOrDefault(item => item.Code == "8121")?.Net ?? 0m;
        if (current > 0m)
        {
            suggestions[724] = (current, "saldo da conta 8121");
        }

        var deferred = balances.SingleOrDefault(item => item.Code == "8122")?.Net ?? 0m;
        if (deferred != 0m)
        {
            suggestions[deferred > 0m ? 725 : 766] = (Math.Abs(deferred), "saldo da conta 8122");
        }

        return suggestions;
    }

    private static string Code(Modelo22CatalogField field) => field.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static (string Source, IReadOnlyList<Modelo22CatalogField> Fields) LoadCatalog()
    {
        using var stream = typeof(Modelo22Service).Assembly
            .GetManifestResourceStream("NexGenAccountingAI.Infrastructure.Persistence.Resources.modelo22-quadro07.json")
            ?? throw new InvalidOperationException("O recurso modelo22-quadro07.json não está embebido.");
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("O recurso modelo22-quadro07.json está vazio.");
        return (file.Source, file.Fields);
    }

    private sealed record CatalogFile(string Source, List<Modelo22CatalogField> Fields);
}
