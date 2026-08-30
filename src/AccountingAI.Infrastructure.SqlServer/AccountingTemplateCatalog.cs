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

        return
        [
            Root("1", "Meios financeiros líquidos"),
            Leaf("11", "Caixa", "1"),
            Leaf("12", "Depósitos à ordem", "1"),
            Leaf("14", isMicro ? "Outros" : "Outros instrumentos financeiros", "1"),
            Root("2", "Contas a receber e a pagar"),
            Leaf("21", isNonProfit ? "Clientes e utentes" : "Clientes", "2"),
            Leaf("22", "Fornecedores", "2"),
            Leaf("23", "Pessoal", "2"),
            Leaf("24", "Estado e outros entes públicos", "2"),
            Leaf("25", "Financiamentos obtidos", "2"),
            Leaf("26", isNonProfit
                ? "Fundadores, patrocinadores, doadores, associados e membros"
                : "Acionistas e sócios", "2"),
            Leaf("27", "Outras contas a receber e a pagar", "2"),
            Leaf("28", "Diferimentos", "2"),
            Leaf("29", "Provisões", "2"),
            Root("3", "Inventários e ativos biológicos"),
            Leaf("31", "Compras", "3"),
            Leaf("32", "Mercadorias", "3"),
            Leaf("33", "Matérias-primas, subsidiárias e de consumo", "3"),
            Root("4", "Investimentos"),
            Leaf("41", "Investimentos financeiros", "4"),
            Leaf("43", "Ativos fixos tangíveis", "4"),
            Leaf("44", "Ativos intangíveis", "4"),
            Root("5", isNonProfit
                ? "Fundos patrimoniais"
                : "Capital, reservas e resultados transitados"),
            Leaf("51", isNonProfit ? "Fundos" : "Capital subscrito", "5"),
            Leaf("56", "Resultados transitados", "5"),
            Leaf("58", "Excedentes de revalorização", "5"),
            Leaf("59", isNonProfit
                ? "Outras variações nos fundos patrimoniais"
                : "Outras variações no capital próprio", "5"),
            Root("6", "Gastos"),
            Leaf("61", "Custo das mercadorias vendidas e matérias consumidas", "6"),
            Leaf("62", "Fornecimentos e serviços externos", "6"),
            Leaf("63", "Gastos com o pessoal", "6"),
            Leaf("64", "Gastos de depreciação e de amortização", "6"),
            Leaf("65", "Perdas por imparidade", "6"),
            Leaf("67", "Provisões do período", "6"),
            Leaf("68", isNonProfit ? "Outros gastos e apoios concedidos" : "Outros gastos e perdas", "6"),
            Leaf("69", "Gastos e perdas de financiamento", "6"),
            Root("7", "Rendimentos"),
            Leaf("71", "Vendas", "7"),
            Leaf("72", "Prestações de serviços", "7"),
            Leaf("75", isNonProfit
                ? "Subsídios, doações e legados à exploração"
                : "Subsídios à exploração", "7"),
            Leaf("76", "Reversões", "7"),
            Leaf("78", "Outros rendimentos e ganhos", "7"),
            Leaf("79", "Juros, dividendos e outros rendimentos similares", "7"),
            Root("8", "Resultados"),
            Leaf("81", "Resultado líquido do período", "8")
        ];
    }

    private static AccountTemplate Root(string code, string name) =>
        new(code, name, null, false);

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
