using AccountingAI.Application.Fiscal;
using AccountingAI.Application.Vat;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma conta que contribui para um campo da declaração.</summary>
public sealed record VatFormSource(string AccountCode, string AccountName, decimal Amount);

/// <summary>Um campo do quadro 06 da declaração periódica de IVA.</summary>
/// <param name="Column"><c>base</c>, <c>sujeito-passivo</c> (imposto a favor do sujeito passivo), <c>estado</c> (a favor do Estado) ou <c>apuramento</c>.</param>
/// <param name="Computed">Campo de controlo: calculado a partir de outros campos.</param>
/// <param name="Supported"><c>false</c> quando a aplicação não tem como apurar o campo — fica a zero e tem de ser preenchido à mão.</param>
public sealed record VatFormField(
    int Number, string Label, string Column, decimal Amount, bool Computed, bool Supported, IReadOnlyList<VatFormSource> Sources);

/// <param name="Period">O período como a declaração o identifica (<c>03</c>, <c>06T</c>…), ou <c>null</c> se as datas não formam um mês nem um trimestre.</param>
/// <param name="AccountingPayable">O valor a entregar segundo o apuramento contabilístico das contas 243.</param>
/// <param name="AccountingCredit">O excesso a reportar segundo o apuramento contabilístico.</param>
/// <param name="Difference">(campo 93 − campo 94) − (a entregar − a reportar) do apuramento contabilístico: zero quando a declaração e a contabilidade batem.</param>
public sealed record VatDeclaration(
    DateOnly From,
    DateOnly To,
    string? Period,
    string Seat,
    IReadOnlyList<VatFormField> Fields,
    IReadOnlyList<string> Warnings,
    decimal AccountingPayable,
    decimal AccountingCredit,
    decimal Difference,
    string Source,
    string Disclaimer);

/// <summary>
/// Preenche o quadro 06 da declaração periódica de IVA a partir da
/// contabilidade: cada linha vale pelo seu código de IVA ou pela classificação
/// da conta (o mesmo critério do resumo de IVA), e cada campo guarda as contas
/// que o formam, para conferência. Os campos e as somas de controlo seguem as
/// instruções de preenchimento publicadas pela AT (<see cref="Source"/>). Só lê.
/// Não é a declaração entregue nem foi validada pela AT: é um mapa para
/// conferir e transcrever.
/// </summary>
public sealed class VatDeclarationService(AccountingDbContext context, VatSummaryService summary, IVatReturnService returns)
{
    public const string Source =
        "Autoridade Tributária e Aduaneira — modelo da declaração periódica de IVA e instruções de preenchimento " +
        "(Portaria n.º 221/2017, de 21 de julho), consultados em 2026-10-04. A Portaria n.º 298/2026/1, de 16 de julho, " +
        "aprova um modelo novo para os períodos com início a partir de 1 de julho de 2027.";

    public const string Disclaimer =
        "Mapa de apoio ao preenchimento, não validado pela AT. Não apura " +
        "os campos 14, 15, 18 e 19, o quadro 06-A nem os anexos das regularizações dos campos 40 e 41; " +
        "as operações de outra região vão só em total para os campos 65 a 68 (o Anexo R não é preenchido).";

    private const string Base = "base", Taxpayer = "sujeito-passivo", State = "estado", Settlement = "apuramento";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<VatDeclaration?> BuildAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, decimal? priorPeriodCredit = null, CancellationToken cancellationToken = default)
    {
        // O apuramento contabilístico verifica o acesso e dá o excesso a reportar do período anterior.
        var settlement = await returns.BuildAsync(userId, companyId, from, to, priorPeriodCredit, cancellationToken).ConfigureAwait(false);
        var overview = await summary.BuildAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false);
        if (settlement is null || overview is null)
        {
            return null;
        }

        var taxRegion = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegion)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var seat = taxRegion switch
        {
            TaxRegion.Azores => VatRegion.Acores,
            TaxRegion.Madeira => VatRegion.Madeira,
            _ => VatRegion.Continente,
        };

        var parts = await summary.PartsAsync(companyId, from, to, cancellationToken).ConfigureAwait(false);
        var accountIds = parts.Select(part => part.AccountId).Distinct().ToList();
        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => new { account.Code, account.Name }, cancellationToken)
            .ConfigureAwait(false);

        var sources = new Dictionary<int, Dictionary<Guid, decimal>>();
        void Put(int field, VatSummaryPart part, decimal amount)
        {
            if (amount == 0m)
            {
                return;
            }

            if (!sources.TryGetValue(field, out var byAccount))
            {
                sources[field] = byAccount = [];
            }

            byAccount[part.AccountId] = byAccount.GetValueOrDefault(part.AccountId) + amount;
        }

        var otherRegions = parts
            .Where(part => part.Region != seat && part.Kind == "imposto" && !IsRegularisation(part.Nature))
            .Select(part => part.Region)
            .Distinct()
            .Order()
            .ToList();
        var selfAssessed = false;

        foreach (var part in parts)
        {
            var credit = part.Credit - part.Debit;
            var debit = part.Debit - part.Credit;
            var tax = part.Kind == "imposto";
            if (part.Kind is not ("base" or "imposto"))
            {
                continue;
            }

            // As regularizações são da totalidade da atividade, qualquer que seja a região.
            if (tax && part.Nature == VatOperationNature.RegularizacoesFavorSujeitoPassivo)
            {
                Put(40, part, debit);
                continue;
            }

            if (tax && part.Nature == VatOperationNature.RegularizacoesFavorEstado)
            {
                Put(41, part, credit);
                continue;
            }

            if (part.Region != seat)
            {
                // Outra região: só os totais de imposto do Anexo R (campos 65/66 e 67/68).
                var index = otherRegions.IndexOf(part.Region);
                if (tax && index >= 0)
                {
                    var deductible = IsDeductible(part.Nature);
                    Put((index == 0 ? 65 : 67) + (deductible ? 0 : 1), part, deductible ? debit : credit);
                }

                continue;
            }

            switch (part.Nature)
            {
                case VatOperationNature.TransmissoesInternas or VatOperationNature.PrestacoesServicosInternas:
                    PutByRate(part, tax ? credit : credit, tax);
                    break;
                case VatOperationNature.AutoliquidacaoOutras:
                    // O adquirente substitui-se ao fornecedor: base e imposto nos campos 1 a 6, e a base também no quadro 06-A.
                    selfAssessed = true;
                    PutByRate(part, tax ? credit : debit, tax);
                    break;
                case VatOperationNature.TransmissoesIntracomunitariasBens or VatOperationNature.PrestacoesServicosIntracomunitarias when !tax:
                    Put(7, part, credit);
                    break;
                // As operações triangulares vão à declaração recapitulativa com o tipo 4, mas aqui ao campo 8.
                case VatOperationNature.ExportacoesEIsentasComDeducao or VatOperationNature.OperacoesTriangulares when !tax:
                    Put(8, part, credit);
                    break;
                case VatOperationNature.OperacoesIsentasSemDeducao when !tax:
                    Put(9, part, credit);
                    break;
                case VatOperationNature.AquisicoesIntracomunitariasBens:
                    Put(tax ? 13 : 12, part, tax ? credit : debit);
                    break;
                case VatOperationNature.AquisicoesIntracomunitariasServicos:
                    Put(tax ? 17 : 16, part, tax ? credit : debit);
                    break;
                case VatOperationNature.IvaDedutivelImobilizado when tax:
                    Put(20, part, debit);
                    break;
                case VatOperationNature.IvaDedutivelExistencias when tax:
                    Put(part.Rate switch { VatRate.Reduzida => 21, VatRate.Intermedia => 23, _ => 22 }, part, debit);
                    break;
                case VatOperationNature.IvaDedutivelOutros when tax:
                    Put(24, part, debit);
                    break;
            }
        }

        void PutByRate(VatSummaryPart part, decimal amount, bool tax)
        {
            var field = part.Rate switch
            {
                VatRate.Reduzida => 1,
                VatRate.Intermedia => 5,
                VatRate.Normal => 3,
                _ => 0,
            };
            if (field != 0)
            {
                Put(field + (tax ? 1 : 0), part, amount);
            }
        }

        decimal Value(int field) => sources.TryGetValue(field, out var byAccount) ? decimal.Round(byAccount.Values.Sum(), 2) : 0m;

        var fields = new List<VatFormField>();
        void Field(int number, string label, string column, bool supported = true) => fields.Add(new VatFormField(
            number, label, column, Value(number), false, supported,
            sources.TryGetValue(number, out var byAccount)
                ? byAccount
                    .Select(item => new VatFormSource(accounts[item.Key].Code, accounts[item.Key].Name, decimal.Round(item.Value, 2)))
                    .OrderBy(item => item.AccountCode, StringComparer.Ordinal)
                    .ToList()
                : []));
        decimal Total(int number, string label, string column, decimal amount)
        {
            fields.Add(new VatFormField(number, label, column, amount, true, true, []));
            return amount;
        }

        Field(1, "Transmissões de bens e prestações de serviços — base tributável à taxa reduzida", Base);
        Field(2, "Imposto liquidado à taxa reduzida", State);
        Field(5, "Transmissões de bens e prestações de serviços — base tributável à taxa intermédia", Base);
        Field(6, "Imposto liquidado à taxa intermédia", State);
        Field(3, "Transmissões de bens e prestações de serviços — base tributável à taxa normal", Base);
        Field(4, "Imposto liquidado à taxa normal", State);
        Field(7, "Transmissões intracomunitárias de bens e prestações de serviços incluídas na declaração recapitulativa", Base);
        Field(8, "Operações que conferem direito à dedução (exportações, inversão do sujeito passivo e outras)", Base);
        Field(9, "Operações isentas que não conferem direito à dedução", Base);
        Field(12, "Aquisições intracomunitárias de bens com imposto liquidado pelo declarante — base", Base);
        Field(13, "Aquisições intracomunitárias de bens — imposto liquidado", State);
        Field(14, "Aquisições intracomunitárias de bens isentas", Base, supported: false);
        Field(15, "Aquisições intracomunitárias de bens com IVA pago junto de outras entidades", Base, supported: false);
        var field10 = Total(10, "Aquisições intracomunitárias de bens — total da base (12 + 14 + 15)", Base, Value(12) + Value(14) + Value(15));
        var field11 = Total(11, "Aquisições intracomunitárias de bens — total do imposto (13)", State, Value(13));
        Field(16, "Prestações de serviços de entidades de outros Estados membros — base", Base);
        Field(17, "Prestações de serviços de entidades de outros Estados membros — imposto liquidado", State);
        Field(18, "Importações de bens com pagamento do imposto na declaração — base", Base, supported: false);
        Field(19, "Importações de bens — imposto liquidado", State, supported: false);
        Field(20, "Imposto dedutível — ativos não correntes (imobilizado)", Taxpayer);
        Field(21, "Imposto dedutível — inventários à taxa reduzida", Taxpayer);
        Field(23, "Imposto dedutível — inventários à taxa intermédia", Taxpayer);
        Field(22, "Imposto dedutível — inventários à taxa normal", Taxpayer);
        Field(24, "Imposto dedutível — outros bens e serviços", Taxpayer);
        Field(40, "Regularizações a favor do sujeito passivo", Taxpayer);
        Field(41, "Regularizações a favor do Estado", State);
        var field61 = Total(61, "Excesso a reportar do período anterior", Taxpayer, settlement.PriorPeriodCredit);
        Field(65, "Anexo R (1.º) — imposto dedutível", Taxpayer);
        Field(66, "Anexo R (1.º) — imposto liquidado", State);
        Field(67, "Anexo R (2.º) — imposto dedutível", Taxpayer);
        Field(68, "Anexo R (2.º) — imposto liquidado", State);

        Total(90, "Total da base tributável (1 + 5 + 3 + 7 + 8 + 9 + 10 + 16 + 18)", Base,
            Value(1) + Value(5) + Value(3) + Value(7) + Value(8) + Value(9) + field10 + Value(16) + Value(18));
        var field91 = Total(91, "Total do imposto a favor do sujeito passivo", Taxpayer,
            Value(20) + Value(21) + Value(23) + Value(22) + Value(24) + Value(40) + field61 + Value(65) + Value(67));
        var field92 = Total(92, "Total do imposto a favor do Estado", State,
            Value(2) + Value(6) + Value(4) + field11 + Value(17) + Value(19) + Value(41) + Value(66) + Value(68));
        var field93 = Total(93, "Imposto a entregar ao Estado (92 − 91)", Settlement, Math.Max(0m, field92 - field91));
        var field94 = Total(94, "Imposto a recuperar (91 − 92)", Settlement, Math.Max(0m, field91 - field92));
        Total(95, "Reembolso pedido", Settlement, 0m);
        Total(96, "Excesso a reportar para o período seguinte (94 − 95)", Settlement, field94);

        var warnings = new List<string>();
        var period = PeriodOf(from, to);
        if (period is null)
        {
            warnings.Add("As datas não correspondem a um mês nem a um trimestre: a declaração é mensal ou trimestral.");
        }

        if (overview.Unclassified.Count > 0)
        {
            warnings.Add($"Há {overview.Unclassified.Count} conta(s) com movimento no período sem classificação de IVA: ficam fora da declaração. Veja o resumo de IVA.");
        }

        var rates = FiscalTableCatalog.For(to.Year)?.Vat.FirstOrDefault(item => item.Region == seat.ToString());
        if (rates is not null)
        {
            foreach (var (baseField, rate, name) in new[] { (1, rates.Reduced, "reduzida"), (5, rates.Intermediate, "intermédia"), (3, rates.Normal, "normal") })
            {
                var expected = decimal.Round(Value(baseField) * rate, 2);
                if (Math.Abs(expected - Value(baseField + 1)) > 1m)
                {
                    warnings.Add(
                        $"O imposto do campo {baseField + 1} ({Value(baseField + 1):N2}) não corresponde à taxa {name} ({rate:P0}) sobre a base do campo {baseField} " +
                        $"({Value(baseField):N2} → {expected:N2}): as instruções pedem correspondência rigorosa.");
                }
            }
        }

        if (selfAssessed || Value(12) != 0m || Value(16) != 0m)
        {
            warnings.Add("Há operações em que liquidou o imposto como adquirente: assinale o quadro 06-A e, nos casos de inversão do sujeito passivo, preencha os campos 97 a 102.");
        }

        if (otherRegions.Count > 0)
        {
            warnings.Add($"Há operações localizadas noutra região ({string.Join(", ", otherRegions)}): é preciso preencher o Anexo R; aqui só entram os totais nos campos 65 a 68.");
        }

        if (Value(7) != 0m)
        {
            warnings.Add("Há operações no campo 7: assinale o quadro 04 e entregue a declaração recapitulativa, cujo campo 19 deve coincidir com este valor.");
        }

        if (Value(40) != 0m || Value(41) != 0m)
        {
            warnings.Add("Os campos 40 e 41 obrigam aos anexos das regularizações, que a aplicação não preenche.");
        }

        var difference = (field93 - field94) - (settlement.NetPayable - settlement.CreditToCarryForward);
        if (difference != 0m)
        {
            warnings.Add(
                $"A declaração difere do apuramento contabilístico das contas 243 em {difference:N2}: há imposto lançado em contas ou códigos sem a classificação certa, ou campos que a aplicação não apura.");
        }

        return new VatDeclaration(
            from, to, period, seat.ToString(), fields, warnings,
            settlement.NetPayable, settlement.CreditToCarryForward, difference, Source, Disclaimer);
    }

    private static bool IsRegularisation(VatOperationNature nature) =>
        nature is VatOperationNature.RegularizacoesFavorSujeitoPassivo or VatOperationNature.RegularizacoesFavorEstado;

    private static bool IsDeductible(VatOperationNature nature) =>
        nature is VatOperationNature.IvaDedutivelImobilizado or VatOperationNature.IvaDedutivelExistencias or VatOperationNature.IvaDedutivelOutros;

    /// <summary>O período como a declaração o identifica: <c>01</c>…<c>12</c> para um mês, <c>03T</c>…<c>12T</c> para um trimestre.</summary>
    public static string? PeriodOf(DateOnly from, DateOnly to)
    {
        if (from.Day != 1 || from.Year != to.Year || to != new DateOnly(to.Year, to.Month, DateTime.DaysInMonth(to.Year, to.Month)))
        {
            return null;
        }

        if (from.Month == to.Month)
        {
            return $"{to.Month:00}";
        }

        return to.Month % 3 == 0 && from.Month == to.Month - 2 ? $"{to.Month:00}T" : null;
    }
}
