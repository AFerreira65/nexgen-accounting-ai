namespace NexGenAccountingAI.Application.Maps;

/// <summary>Uma linha de uma tabela do relato: as células já formatadas, o nível de indentação e o peso.</summary>
public sealed record ReportPackRow(IReadOnlyList<string> Cells, int Level = 0, bool Bold = false, bool IsTitle = false);

/// <summary>Uma tabela do relato: cabeçalho, linhas e que colunas são numéricas (alinhadas à direita).</summary>
public sealed record ReportPackTable(IReadOnlyList<string> Header, IReadOnlyList<ReportPackRow> Rows, IReadOnlyList<bool> Numeric);

/// <summary>Uma secção do relato: título, parágrafos de texto e, se houver, uma tabela e uma nota.</summary>
public sealed record ReportPackSection(
    string Title,
    IReadOnlyList<string> Paragraphs,
    ReportPackTable? Table = null,
    IReadOnlyList<string>? Notes = null);

/// <summary>
/// Um relato pronto a imprimir (F4B-04): capa, secções de texto e de mapas, rodapé. É o
/// que o gerador de PDF recebe — já sem contas nem contabilidade, só conteúdo.
/// </summary>
public sealed record ReportPack(
    string Title,
    string? Subtitle,
    string CompanyName,
    string CompanyTaxId,
    string Period,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportPackSection> Sections,
    string? Preparer = null,
    string? Notice = null);

/// <summary>O que o utilizador pede para uma secção do relato.</summary>
/// <param name="Kind"><c>texto</c>, <c>mapa</c> (guardado, por <paramref name="MapId"/>, ou modelo, por <paramref name="TemplateKey"/>), <c>balancete</c> ou <c>racios</c>.</param>
/// <param name="Text">O texto da secção, em parágrafos separados por linhas em branco.</param>
/// <param name="Depth">No balancete, até que nível (por omissão 2).</param>
/// <param name="ColumnSet">No balancete, o conjunto de colunas (por omissão o clássico).</param>
public sealed record ReportPackSectionRequest(
    string Kind,
    string? Title = null,
    string? Text = null,
    Guid? MapId = null,
    string? TemplateKey = null,
    int? Depth = null,
    string? ColumnSet = null,
    bool Enabled = true);

/// <summary>O pedido de um relato: título, período e secções.</summary>
public sealed record ReportPackRequest(
    string Title,
    string? Subtitle,
    int Year,
    int FromMonth,
    int ToMonth,
    bool IncludeClosing,
    IReadOnlyList<ReportPackSectionRequest> Sections,
    string? Preparer = null);

/// <summary>Os relatos prontos a pedir; o primeiro é o relatório para a banca.</summary>
public static class ReportPackPresets
{
    public const string Bank = "banca";

    public static IReadOnlyList<(string Key, string Label, string Description)> All { get; } =
    [
        (Bank, "Relatório para a banca", "Apresentação da empresa, balanço, demonstração de resultados, exploração mensal, rácios e balancete sintético — o dossier que um banco pede para analisar crédito."),
    ];

    public static ReportPackRequest? Build(string key, int year, int fromMonth, int toMonth) =>
        string.Equals(key, Bank, StringComparison.OrdinalIgnoreCase)
            ? new ReportPackRequest(
                "Relatório económico-financeiro",
                "Elementos para apreciação de crédito",
                year, fromMonth, toMonth, IncludeClosing: false,
                [
                    new("texto", "Apresentação", "A empresa, a atividade e o período em análise. Substitua este texto pela apresentação — ou peça à IA que a redija a partir dos números."),
                    new("mapa", "Balanço", TemplateKey: MapTemplates.BalanceSheet),
                    new("mapa", "Demonstração de resultados", TemplateKey: MapTemplates.IncomeStatement),
                    new("mapa", "Exploração mensal", TemplateKey: MapTemplates.MonthlyOperations),
                    new("racios", "Rácios económico-financeiros"),
                    new("balancete", "Balancete sintético", Depth: 2),
                ])
            : null;
}
