using AccountingAI.Domain.Organizations;

namespace AccountingAI.Infrastructure.SqlServer;

public sealed record AccountTemplate(
    string Code,
    string Name,
    string? ParentCode = null,
    bool AllowsPosting = true);

public sealed record JournalTemplate(string Code, string Name);

public sealed record DocumentTypeTemplate(
    string Code,
    string Name,
    string DefaultJournalCode);

public sealed record AccountingTemplateSet(
    AccountingStandard Standard,
    string Code,
    string Name,
    string Version,
    string LegalSource,
    string TaxonomyReference,
    string TaxonomyName,
    string TaxonomyLegalSource,
    IReadOnlyList<AccountTemplate> Accounts,
    IReadOnlyList<JournalTemplate> Journals,
    IReadOnlyList<DocumentTypeTemplate> DocumentTypes);

public static class AccountingTemplateCatalog
{
    public const string LegalSource =
        "https://diariodarepublica.pt/dr/detalhe/portaria/218-2015-69861874";
    public const string TaxonomyLegalSource =
        "https://files.dre.pt/gratuitos/1s/2016/12/23100.pdf";

    private static readonly JournalTemplate[] DefaultJournals =
    [
        new("GERAL", "Diário de operações diversas"),
        new("COMPRAS", "Diário de compras"),
        new("VENDAS", "Diário de vendas"),
        new("CAIXA", "Diário de caixa"),
        new("BANCOS", "Diário de bancos"),
        new("ABERTURA", "Diário de abertura"),
        new("APURAMENTO", "Diário de apuramento de resultados"),
        new("ENCERRAMENTO", "Diário de encerramento")
    ];

    private static readonly DocumentTypeTemplate[] DefaultDocumentTypes =
    [
        new("AB", "Abertura do exercício", "ABERTURA"),
        new("FTF", "Fatura de fornecedor", "COMPRAS"),
        new("NCF", "Nota de crédito de fornecedor", "COMPRAS"),
        new("FTV", "Fatura a cliente", "VENDAS"),
        new("NCV", "Nota de crédito a cliente", "VENDAS"),
        new("RCB", "Recebimento", "BANCOS"),
        new("PGT", "Pagamento", "BANCOS"),
        new("TRF", "Transferência entre contas", "BANCOS"),
        new("OD", "Operação diversa", "GERAL"),
        new("AP", "Apuramento de resultados", "APURAMENTO"),
        new("ENC", "Encerramento do exercício", "ENCERRAMENTO")
    ];

    public static IReadOnlyList<AccountingTemplateSet> All { get; } =
    [
        Create(AccountingStandard.SncGeneral, "NCRF", "SNC — Regime geral"),
        Create(AccountingStandard.SncSmallEntities, "NCRF-PE", "SNC — Pequenas entidades"),
        Create(AccountingStandard.SncMicroEntities, "NCRF-ME", "SNC — Microentidades"),
        Create(AccountingStandard.SncNonProfit, "NCRF-ESNL", "SNC — Entidades do setor não lucrativo"),
        Create(AccountingStandard.IfrsEu, "IAS-IFRS-UE", "Normas internacionais adotadas pela UE")
    ];

    public static AccountingTemplateSet For(AccountingStandard standard) =>
        All.Single(item => item.Standard == standard);

    private static AccountingTemplateSet Create(
        AccountingStandard standard,
        string code,
        string name) => new(
            standard,
            code,
            name,
            $"PT-CC-218-2015-{code}-v1",
            LegalSource,
            TaxonomyReferenceFor(standard),
            TaxonomyNameFor(standard),
            TaxonomyLegalSource,
            CreateAccounts(standard),
            DefaultJournals,
            DefaultDocumentTypes);

    private static AccountTemplate[] CreateAccounts(AccountingStandard standard)
    {
        var isMicro = standard == AccountingStandard.SncMicroEntities;
        var isNonProfit = standard == AccountingStandard.SncNonProfit;

        // Regra: só as contas de 3 ou mais dígitos são de movimento; as de 1 e 2
        // dígitos são de integração (razão/agregadora). Exceção: 56 e 81, que o
        // apuramento e a abertura lançam diretamente.
        return
        [
            Node("1", "Meios financeiros líquidos"),
            Node("11", "Caixa", "1"),
            Leaf("111", "Caixa — geral", "11"),
            Node("12", "Depósitos à ordem", "1"),
            Leaf("121", "Depósito à ordem — geral", "12"),
            Node("14", isMicro ? "Outros" : "Outros instrumentos financeiros", "1"),
            Node("2", "Contas a receber e a pagar"),
            Node("21", isNonProfit ? "Clientes e utentes" : "Clientes", "2"),
            Leaf("211", "Clientes — conta corrente", "21"),
            Node("22", "Fornecedores", "2"),
            Leaf("221", "Fornecedores — conta corrente", "22"),
            Node("23", "Pessoal", "2"),
            Leaf("231", "Remunerações a pagar", "23"),
            Node("24", "Estado e outros entes públicos", "2"),
            Leaf("2432", "IVA — dedutível", "24"),
            Leaf("2433", "IVA — liquidado", "24"),
            Leaf("2434", "IVA — apuramento", "24"),
            Leaf("2435", "IVA — a pagar", "24"),
            Leaf("2436", "IVA — a recuperar", "24"),
            Node("25", "Financiamentos obtidos", "2"),
            Node("26", isNonProfit
                ? "Fundadores, patrocinadores, doadores, associados e membros"
                : "Acionistas e sócios", "2"),
            Node("27", "Outras contas a receber e a pagar", "2"),
            Leaf("2711", "Devedores diversos", "27"),
            Leaf("2721", "Credores diversos", "27"),
            Node("28", "Diferimentos", "2"),
            Node("29", "Provisões", "2"),
            Node("3", "Inventários e ativos biológicos"),
            Node("31", "Compras", "3"),
            Leaf("311", "Compras de mercadorias", "31"),
            Node("32", "Mercadorias", "3"),
            Leaf("321", "Mercadorias em armazém", "32"),
            Node("33", "Matérias-primas, subsidiárias e de consumo", "3"),
            Node("4", "Investimentos"),
            Node("41", "Investimentos financeiros", "4"),
            Node("43", "Ativos fixos tangíveis", "4"),
            Leaf("433", "Equipamento básico", "43"),
            Leaf("434", "Equipamento de transporte", "43"),
            Leaf("435", "Equipamento administrativo", "43"),
            Node("44", "Ativos intangíveis", "4"),
            Node("5", isNonProfit
                ? "Fundos patrimoniais"
                : "Capital, reservas e resultados transitados"),
            Node("51", isNonProfit ? "Fundos" : "Capital subscrito", "5"),
            Leaf("511", isNonProfit ? "Fundos — geral" : "Capital — geral", "51"),
            Leaf("56", "Resultados transitados", "5"),
            Node("58", "Excedentes de revalorização", "5"),
            Node("59", isNonProfit
                ? "Outras variações nos fundos patrimoniais"
                : "Outras variações no capital próprio", "5"),
            Node("6", "Gastos"),
            Node("61", "Custo das mercadorias vendidas e matérias consumidas", "6"),
            Leaf("611", "CMVMC — mercadorias", "61"),
            Node("62", "Fornecimentos e serviços externos", "6"),
            Leaf("621", "Subcontratos", "62"),
            Leaf("622", "Serviços especializados", "62"),
            Leaf("623", "Materiais", "62"),
            Leaf("624", "Energia e fluidos", "62"),
            Leaf("625", "Deslocações, estadas e transportes", "62"),
            Leaf("626", "Serviços diversos", "62"),
            Node("63", "Gastos com o pessoal", "6"),
            Leaf("631", "Remunerações dos órgãos sociais", "63"),
            Leaf("632", "Remunerações do pessoal", "63"),
            Leaf("635", "Encargos sobre remunerações", "63"),
            Node("64", "Gastos de depreciação e de amortização", "6"),
            Leaf("642", "Ativos fixos tangíveis", "64"),
            Node("65", "Perdas por imparidade", "6"),
            Node("67", "Provisões do período", "6"),
            Node("68", isNonProfit ? "Outros gastos e apoios concedidos" : "Outros gastos e perdas", "6"),
            Leaf("6881", "Outros gastos e perdas — diversos", "68"),
            Node("69", "Gastos e perdas de financiamento", "6"),
            Leaf("691", "Juros suportados", "69"),
            Node("7", "Rendimentos"),
            Node("71", "Vendas", "7"),
            Leaf("711", "Vendas de mercadorias", "71"),
            Node("72", "Prestações de serviços", "7"),
            Leaf("721", "Prestações de serviços", "72"),
            Node("75", isNonProfit
                ? "Subsídios, doações e legados à exploração"
                : "Subsídios à exploração", "7"),
            Node("76", "Reversões", "7"),
            Node("78", "Outros rendimentos e ganhos", "7"),
            Leaf("7881", "Outros rendimentos e ganhos — diversos", "78"),
            Node("79", "Juros, dividendos e outros rendimentos similares", "7"),
            Leaf("791", "Juros obtidos", "79"),
            Node("8", "Resultados"),
            Leaf("81", "Resultado líquido do período", "8")
        ];
    }

    private static AccountTemplate Node(string code, string name, string? parentCode = null) =>
        new(code, name, parentCode, false);

    private static AccountTemplate Leaf(string code, string name, string parentCode) =>
        new(code, name, parentCode, true);

    private static string TaxonomyReferenceFor(AccountingStandard standard) => standard switch
    {
        AccountingStandard.SncMicroEntities => "M",
        AccountingStandard.IfrsEu => "N",
        _ => "S"
    };

    private static string TaxonomyNameFor(AccountingStandard standard) => standard switch
    {
        AccountingStandard.SncMicroEntities => "Taxonomia M — SNC Microentidades",
        AccountingStandard.IfrsEu => "Referencial N — Taxonomia S",
        _ => "Taxonomia S — SNC base"
    };
}
