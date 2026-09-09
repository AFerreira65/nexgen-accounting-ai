namespace AccountingAI.Application.Reporting;

/// <summary>As chaves dos relatórios fixos que aceitam um modelo de impressão.</summary>
public static class ReportTemplateKeys
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "balancete",
        "razao",
        "livro-diario",
        "resultados",
        "balanco",
        "iva",
        "apuramento-iva",
        "contas-correntes",
        "analises",
    };

    public static bool IsKnown(string? reportKey) =>
        !string.IsNullOrWhiteSpace(reportKey) && Known.Contains(reportKey.Trim());
}
