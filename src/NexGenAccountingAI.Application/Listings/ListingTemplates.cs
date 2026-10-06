namespace NexGenAccountingAI.Application.Listings;

/// <summary>Um modelo guiado do sistema: uma listagem pronta para abrir, correr e, se quiser, guardar como sua.</summary>
public sealed record ListingTemplate(string Key, string Name, string Description, ListingDefinition Definition);

/// <summary>Os modelos guiados (F4D-03): as perguntas que mais se fazem à contabilidade, já desenhadas.</summary>
public static class ListingTemplates
{
    private static readonly ListingParameter Since = new("inicio", "Desde", "date", "inicio-ano");

    private static readonly ListingParameter Until = new("fim", "Até", "date", "fim-ano");

    private static ListingFilterGroup DateRange() =>
        new(ListingFilterGroup.And, [new ListingCondition("data", ListingOperators.Between, "@inicio", "@fim")]);

    public static IReadOnlyList<ListingTemplate> All { get; } =
    [
        new("extrato-conta", "Extrato de uma conta",
            "Os movimentos de uma conta (ou de todas as que começam por um prefixo) num período, com o débito, o crédito e o saldo de cada linha.",
            new ListingDefinition(
                ListingCatalog.JournalLines,
                ["data", "lancamento", "descricao", "terceiro", "debito", "credito", "saldo"],
                new ListingFilterGroup(ListingFilterGroup.And,
                [
                    new ListingCondition("conta", ListingOperators.StartsWith, "@conta"),
                    new ListingCondition("data", ListingOperators.Between, "@inicio", "@fim"),
                ]),
                [new ListingSort("data"), new ListingSort("lancamento")],
                null,
                [new ListingParameter("conta", "Conta (ou prefixo)", "text", "1"), Since, Until])),
        new("razao-por-conta", "Razão por conta",
            "Todos os movimentos do período, agrupados por conta, com os subtotais de cada uma.",
            new ListingDefinition(
                ListingCatalog.JournalLines,
                ["data", "lancamento", "descricao", "terceiro", "debito", "credito", "saldo"],
                DateRange(), [new ListingSort("data")], ["conta"], [Since, Until])),
        new("saldo-por-terceiro", "Saldo por terceiro",
            "O que cada cliente e fornecedor deve ou tem a haver, a partir dos movimentos das contas 21 e 22.",
            new ListingDefinition(
                ListingCatalog.JournalLines,
                ["data", "lancamento", "conta", "descricao", "debito", "credito", "saldo"],
                new ListingFilterGroup(ListingFilterGroup.And,
                [
                    new ListingCondition("data", ListingOperators.Between, "@inicio", "@fim"),
                ],
                [
                    new ListingFilterGroup(ListingFilterGroup.Or,
                    [
                        new ListingCondition("conta", ListingOperators.StartsWith, "21"),
                        new ListingCondition("conta", ListingOperators.StartsWith, "22"),
                    ]),
                ]),
                [new ListingSort("data")], ["terceiro"], [Since, new ListingParameter("fim", "Até", "date", "hoje")])),
        new("evolucao-mensal-contas", "Evolução mensal por conta",
            "O débito, o crédito e o saldo de cada conta em cada mês, agrupados por classe, conta e ano.",
            new ListingDefinition(
                ListingCatalog.Balances,
                ["conta", "nomeConta", "ano", "mes", "debito", "credito", "saldo"],
                null, [new ListingSort("conta"), new ListingSort("ano"), new ListingSort("mes")], ["classe", "conta"])),
        new("vendas-por-cliente", "Vendas por cliente",
            "As vendas do período por cliente, com o valor líquido, o IVA e o total, e se já estão contabilizadas.",
            new ListingDefinition(
                ListingCatalog.Sales,
                ["data", "documento", "contabilizado", "liquido", "iva", "total"],
                DateRange(), [new ListingSort("data")], ["cliente"], [Since, Until])),
        new("vendas-por-contabilizar", "Vendas por contabilizar",
            "Os documentos de venda que ainda não geraram lançamento.",
            new ListingDefinition(
                ListingCatalog.Sales,
                ["data", "documento", "cliente", "liquido", "iva", "total"],
                new ListingFilterGroup(ListingFilterGroup.And, [new ListingCondition("contabilizado", ListingOperators.Equal, "Não")]),
                [new ListingSort("data")])),
        new("compras-por-lancar", "Compras por lançar",
            "As faturas do e-Fatura que ainda não foram lançadas nem dispensadas, por fornecedor.",
            new ListingDefinition(
                ListingCatalog.Purchases,
                ["data", "documento", "base", "iva", "total"],
                new ListingFilterGroup(ListingFilterGroup.And, [new ListingCondition("situacao", ListingOperators.Equal, "Por lançar")]),
                [new ListingSort("data")], ["fornecedor"])),
        new("ativos-por-estado", "Ativos fixos por estado",
            "Os ativos fixos agrupados em uso, abatidos e inativos, com os valores de aquisição.",
            new ListingDefinition(
                ListingCatalog.Assets,
                ["codigo", "descricao", "aquisicao", "abate", "valorAquisicao", "valorResidual"],
                null, [new ListingSort("codigo")], ["estado"])),
        new("movimentos-por-conciliar", "Movimentos bancários por conciliar",
            "Os movimentos dos extratos que ainda não foram conciliados com a contabilidade.",
            new ListingDefinition(
                ListingCatalog.Bank,
                ["data", "descricao", "referencia", "valor"],
                new ListingFilterGroup(ListingFilterGroup.And, [new ListingCondition("conciliado", ListingOperators.Equal, "Não")]),
                [new ListingSort("data")], ["conta"])),
        new("documentos-por-ligar", "Documentos arquivados por ligar",
            "Os documentos do arquivo digital que ainda não estão ligados a nenhum lançamento.",
            new ListingDefinition(
                ListingCatalog.Archive,
                ["ficheiro", "nifEmissor", "documento", "dataDocumento", "total", "notas"],
                new ListingFilterGroup(ListingFilterGroup.And, [new ListingCondition("ligado", ListingOperators.Equal, "Não")]),
                [new ListingSort("ficheiro")])),
    ];

    public static ListingTemplate? Find(string? key) => All.FirstOrDefault(template => string.Equals(template.Key, key, StringComparison.Ordinal));
}
