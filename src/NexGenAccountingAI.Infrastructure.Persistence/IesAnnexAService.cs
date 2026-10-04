using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um campo do Anexo A da IES com o valor do período.</summary>
/// <param name="Code">O código do campo no impresso (<c>A00001</c>…).</param>
/// <param name="Quadro"><c>Q03</c> (demonstração dos resultados por naturezas) ou <c>Q04</c> (balanço).</param>
/// <param name="Section">A secção do balanço; vazio no quadro 03 e nos totais gerais.</param>
/// <param name="Computed">Soma ou subtotal do impresso.</param>
public sealed record IesField(string Code, string Quadro, string Section, string Label, decimal Amount, bool Computed);

/// <param name="Norm">O normativo do quadro 02-A: <c>S</c> (NCRF), <c>P</c> (NCRF-PE), <c>M</c> (NC-ME) ou <c>N</c> (NIC).</param>
public sealed record IesAnnexA(
    int Year, string Norm, IReadOnlyList<IesField> Fields, IReadOnlyList<string> Warnings, string Source, string Disclaimer);

/// <summary>
/// Preenche os quadros 03 e 04 do Anexo A da IES — a demonstração dos
/// resultados por naturezas e o balanço do período N — a partir das
/// demonstrações financeiras que a aplicação já calcula
/// (<see cref="FinancialStatementsService"/>): cada rubrica vai para o campo
/// do impresso com a mesma designação. Só lê. É um mapa para conferir e
/// transcrever: não gera o ficheiro nem foi validado pela AT.
/// </summary>
public sealed class IesAnnexAService(AccountingDbContext context, FinancialStatementsService statements)
{
    public const string Source =
        "Autoridade Tributária e Aduaneira — IES / Declaração Anual, Anexo A (modelo em vigor para declarações do período de 2019 e seguintes), " +
        "consultado em 2026-10-04.";

    public const string Disclaimer =
        "Mapa de apoio ao preenchimento, não validado pela AT. Cobre os quadros 03 e 04 do Anexo A (período N). Não preenche a folha de rosto, " +
        "a demonstração das alterações no capital próprio (quadro 04-A), os fluxos de caixa, o anexo, os restantes quadros nem os outros anexos, " +
        "e não gera o ficheiro da declaração. Nos campos de gastos o valor vai positivo, como o impresso os subtrai; " +
        "o sinal das ações próprias e dos dividendos antecipados está por conferir.";

    // Quadro 03: a rubrica da demonstração de resultados, o número do campo e se o impresso a subtrai.
    private static readonly (string Line, int Field, bool Subtracted)[] IncomeFields =
    [
        ("Vendas e serviços prestados", 1, false),
        ("Subsídios à exploração", 2, false),
        ("Ganhos / perdas imputados de subsidiárias, associadas e empreendimentos conjuntos", 3, false),
        ("Variação nos inventários da produção", 4, false),
        ("Trabalhos para a própria entidade", 5, false),
        ("Custo das mercadorias vendidas e das matérias consumidas", 6, true),
        ("Fornecimentos e serviços externos", 7, true),
        ("Gastos com o pessoal", 8, true),
        ("Imparidades (Perdas/Reversões)", 9, true),
        ("Imparidade / Ajustamentos de inventários (perdas/reversões)", 10, true),
        ("Imparidade de dívidas a receber (perdas/reversões)", 11, true),
        ("Provisões (aumentos/reduções)", 12, true),
        ("Imparidade de investimentos não depreciáveis/amortizáveis (perdas/reversões)", 13, true),
        ("Outras Imparidades (Perdas/Reversões)", 14, true),
        ("Aumentos / reduções de justo valor", 15, false),
        ("Outros rendimentos", 16, false),
        ("Outros gastos", 17, true),
        ("Gastos / reversões de depreciação e de amortização", 19, true),
        ("Imparidade de investimentos depreciáveis / amortizáveis (perdas/reversões)", 20, true),
        ("Juros e rendimentos similares obtidos", 22, false),
        ("Juros e gastos similares suportados", 23, true),
        ("Imposto sobre o rendimento do período", 25, true),
    ];

    // Quadro 04: a secção e a rubrica do balanço, e o número do campo (A001xx).
    private static readonly (string Section, string Line, int Field)[] BalanceFields =
    [
        ("Ativo não corrente", "Ativos fixos tangíveis", 101),
        ("Ativo não corrente", "Propriedades de investimento", 102),
        ("Ativo não corrente", "Goodwill", 103),
        ("Ativo não corrente", "Ativos intangíveis", 104),
        ("Ativo não corrente", "Ativos biológicos", 105),
        ("Ativo não corrente", "Participações financeiras - método da equivalência patrimonial", 106),
        ("Ativo não corrente", "Outros investimentos financeiros", 107),
        ("Ativo não corrente", "Créditos a receber", 108),
        ("Ativo não corrente", "Ativos por Impostos diferidos", 109),
        ("Ativo não corrente", "Investimentos financeiros", 110),
        ("Ativo não corrente", "Créditos e outros ativos não correntes", 111),
        ("Ativo corrente", "Inventários", 113),
        ("Ativo corrente", "Ativos biológicos", 114),
        ("Ativo corrente", "Clientes", 115),
        ("Ativo corrente", "Estado e outros entes públicos", 116),
        ("Ativo corrente", "Capital subscrito e não realizado", 117),
        ("Ativo corrente", "Outros créditos a receber", 118),
        ("Ativo corrente", "Diferimentos", 119),
        ("Ativo corrente", "Ativos financeiros detidos para negociação", 120),
        ("Ativo corrente", "Outros ativos financeiros", 121),
        ("Ativo corrente", "Ativos não correntes detidos para venda", 122),
        ("Ativo corrente", "Outros ativos correntes", 123),
        ("Ativo corrente", "Caixa e depósitos bancários", 124),
        ("Capital próprio", "Capital Subscrito", 127),
        ("Capital próprio", "Ações (quotas) próprias", 128),
        ("Capital próprio", "Outros instrumentos de capital próprio", 129),
        ("Capital próprio", "Prémios de emissão", 130),
        ("Capital próprio", "Reservas legais", 131),
        ("Capital próprio", "Outras reservas", 132),
        ("Capital próprio", "Resultados transitados", 133),
        ("Capital próprio", "Excedentes de revalorização", 134),
        ("Capital próprio", "Ajustamentos / Outras variações no capital próprio", 135),
        ("Capital próprio", "Resultado líquido do período", 137),
        ("Capital próprio", "Dividendos antecipados", 138),
        ("Passivo não corrente", "Provisões", 140),
        ("Passivo não corrente", "Financiamentos obtidos", 141),
        ("Passivo não corrente", "Responsabilidades por benefícios pós-emprego", 142),
        ("Passivo não corrente", "Passivos por impostos diferidos", 143),
        ("Passivo não corrente", "Outras dívidas a pagar", 144),
        ("Passivo corrente", "Fornecedores", 146),
        ("Passivo corrente", "Adiantamentos de clientes", 147),
        ("Passivo corrente", "Estado e outros entes públicos", 148),
        ("Passivo corrente", "Financiamentos obtidos", 149),
        ("Passivo corrente", "Outras dívidas a pagar", 150),
        ("Passivo corrente", "Diferimentos", 151),
        ("Passivo corrente", "Passivos financeiros detidos para negociação", 152),
        ("Passivo corrente", "Outros passivos financeiros", 153),
        ("Passivo corrente", "Passivos não correntes detidos para venda", 154),
        ("Passivo corrente", "Outros passivos correntes", 155),
    ];

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IesAnnexA?> BuildAsync(Guid userId, Guid companyId, int year, CancellationToken cancellationToken = default)
    {
        var end = new DateOnly(year, 12, 31);
        var balance = await statements.BalanceSheetAsync(userId, companyId, end, cancellationToken).ConfigureAwait(false);
        var income = await statements.IncomeStatementAsync(userId, companyId, new DateOnly(year, 1, 1), end, cancellationToken).ConfigureAwait(false);
        if (balance is null || income is null)
        {
            return null;
        }

        var standard = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.AccountingStandard)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var norm = standard switch
        {
            AccountingStandard.SncSmallEntities => "P",
            AccountingStandard.SncMicroEntities => "M",
            AccountingStandard.IfrsEu => "N",
            _ => "S",
        };

        var warnings = new List<string>();
        var fields = new List<IesField>();
        var values = new Dictionary<int, decimal>();
        static string Code(int field) => $"A{field:00000}";

        // Quadro 03 — os gastos vão positivos: o impresso subtrai-os.
        var incomeLines = income.Lines.Where(line => !line.IsTotal).ToDictionary(line => line.Label, line => line.Amount, StringComparer.Ordinal);
        foreach (var (line, field, subtracted) in IncomeFields)
        {
            if (incomeLines.Remove(line, out var amount) && amount != 0m)
            {
                values[field] = subtracted ? -amount : amount;
                fields.Add(new IesField(Code(field), "Q03", string.Empty, line, values[field], false));
            }
        }

        foreach (var (label, amount) in incomeLines.Where(item => item.Value != 0m))
        {
            warnings.Add($"A rubrica «{label}» da demonstração de resultados ({amount:N2}) não tem campo no quadro 03: os subtotais do impresso não vão bater.");
        }

        decimal V(int field) => values.GetValueOrDefault(field);
        decimal Range(int from, int to) => Enumerable.Range(from, to - from + 1).Sum(V);
        void Total(string quadro, string section, int field, string label, decimal amount)
        {
            values[field] = amount;
            fields.Add(new IesField(Code(field), quadro, section, label, amount, true));
        }

        Total("Q03", string.Empty, 18, "Resultado antes de depreciações, gastos de financiamento e impostos (1 + 2 + … + 5 − 6 − … − 14 + 15 + 16 − 17)",
            Range(1, 5) - Range(6, 14) + V(15) + V(16) - V(17));
        Total("Q03", string.Empty, 21, "Resultado operacional (antes de gastos de financiamento e impostos) (18 − 19 − 20)", V(18) - V(19) - V(20));
        Total("Q03", string.Empty, 24, "Resultado antes de impostos (21 + 22 − 23)", V(21) + V(22) - V(23));
        Total("Q03", string.Empty, 26, "Resultado líquido do período (24 − 25)", V(24) - V(25));
        if (V(26) != income.NetResult)
        {
            warnings.Add($"O resultado líquido do quadro 03 ({V(26):N2}) difere do da demonstração de resultados ({income.NetResult:N2}).");
        }

        // Quadro 04.
        var balanceLines = balance.Lines
            .Where(line => !line.IsTotal)
            .ToDictionary(line => (line.Section, line.Label), line => line.Amount);
        foreach (var (section, line, field) in BalanceFields)
        {
            if (balanceLines.Remove((section, line), out var amount) && amount != 0m)
            {
                values[field] = amount;
                fields.Add(new IesField(Code(field), "Q04", section, line, amount, false));
            }
        }

        foreach (var (key, amount) in balanceLines.Where(item => item.Value != 0m))
        {
            warnings.Add($"A rubrica «{key.Label}» ({key.Section}, {amount:N2}) não tem campo no quadro 04: as somas do impresso não vão bater com o balanço.");
        }

        Total("Q04", "Ativo não corrente", 112, "Soma do ativo não corrente", Range(101, 111));
        Total("Q04", "Ativo corrente", 125, "Soma do ativo corrente", Range(113, 124));
        Total("Q04", string.Empty, 126, "Total do ativo", V(112) + V(125));
        Total("Q04", "Capital próprio", 136, "Soma (antes do resultado líquido)", Range(127, 135));
        Total("Q04", "Capital próprio", 139, "Total do capital próprio", V(136) + V(137) + V(138));
        Total("Q04", "Passivo não corrente", 145, "Soma do passivo não corrente", Range(140, 144));
        Total("Q04", "Passivo corrente", 156, "Soma do passivo corrente", Range(146, 155));
        Total("Q04", string.Empty, 157, "Total do passivo", V(145) + V(156));
        Total("Q04", string.Empty, 158, "Total do capital próprio e do passivo", V(139) + V(157));

        if (V(126) != V(158))
        {
            warnings.Add($"O total do ativo ({V(126):N2}) difere do total do capital próprio e do passivo ({V(158):N2}) em {V(126) - V(158):N2}: a IES não aceita um balanço que não fecha.");
        }

        if (V(26) != V(137))
        {
            warnings.Add($"O resultado líquido do quadro 03 ({V(26):N2}) difere do resultado líquido do balanço ({V(137):N2}).");
        }

        var excluded = balance.Excluded.Count + income.Excluded.Count;
        if (excluded > 0)
        {
            warnings.Add($"Há {excluded} conta(s) com saldo fora das demonstrações financeiras: corrija a taxonomia antes de preencher a IES.");
        }

        if (norm == "N")
        {
            warnings.Add("A empresa usa as normas internacionais: as rubricas foram montadas pelo modelo geral do SNC e podem não corresponder às do impresso.");
        }

        return new IesAnnexA(
            year, norm,
            fields.OrderBy(field => field.Quadro, StringComparer.Ordinal).ThenBy(field => field.Code, StringComparer.Ordinal).ToList(),
            warnings, Source, Disclaimer);
    }
}
