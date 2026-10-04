namespace NexGenAccountingAI.Application.ManagementReports;

/// <summary>Uma entrada do catálogo de tipos de relatório pré-definidos.</summary>
public sealed record ManagementReportCatalogEntry(
    string Key,
    string Label,
    string Description,
    IReadOnlyList<string> Sections);

/// <summary>
/// O catálogo — em código, não editável — dos tipos de relatório de gestão
/// pré-definidos, com o rótulo pt-PT e as secções do dossiê que cada um usa.
/// As chaves de secção são: <c>balancete-homologo</c>, <c>resultados</c>,
/// <c>posicao</c>, <c>racios</c>, <c>cruzamento</c>, <c>movimentos</c>.
/// </summary>
public static class ManagementReportCatalog
{
    public static IReadOnlyList<ManagementReportCatalogEntry> Entries { get; } =
    [
        new(
            "sintese-mensal",
            "Síntese mensal",
            "Resultado do período, variação homóloga, principais desvios e alertas.",
            ["resultados", "balancete-homologo", "racios", "movimentos"]),
        new(
            "analise-rentabilidade",
            "Análise de rentabilidade",
            "Margens, peso dos gastos operacionais e com pessoal, evolução homóloga.",
            ["resultados", "racios", "balancete-homologo"]),
        new(
            "posicao-financeira",
            "Posição financeira",
            "Ativo, passivo e capital próprio no fim do período; liquidez e autonomia.",
            ["posicao", "racios"]),
        new(
            "revisao-iva",
            "Revisão de IVA",
            "IVA liquidado e dedutível do período e o cruzamento com o e-Fatura.",
            ["cruzamento", "balancete-homologo"]),
        new(
            "alertas-de-fecho",
            "Alertas de fecho",
            "Contas e movimentos que merecem revisão antes do fecho do período.",
            ["balancete-homologo", "movimentos", "cruzamento", "racios"]),
    ];

    /// <summary>A chave de catálogo (kebab-case) de um tipo.</summary>
    public static string KeyOf(ManagementReportKind kind) => kind switch
    {
        ManagementReportKind.SinteseMensal => "sintese-mensal",
        ManagementReportKind.AnaliseRentabilidade => "analise-rentabilidade",
        ManagementReportKind.PosicaoFinanceira => "posicao-financeira",
        ManagementReportKind.RevisaoIva => "revisao-iva",
        ManagementReportKind.AlertasDeFecho => "alertas-de-fecho",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Resolve uma chave de catálogo para um tipo; <c>null</c> se desconhecida.</summary>
    public static ManagementReportKind? Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "sintese-mensal" => ManagementReportKind.SinteseMensal,
        "analise-rentabilidade" => ManagementReportKind.AnaliseRentabilidade,
        "posicao-financeira" => ManagementReportKind.PosicaoFinanceira,
        "revisao-iva" => ManagementReportKind.RevisaoIva,
        "alertas-de-fecho" => ManagementReportKind.AlertasDeFecho,
        _ => null,
    };

    /// <summary>O rótulo pt-PT de um tipo.</summary>
    public static string LabelOf(ManagementReportKind kind) =>
        Entries.First(entry => entry.Key == KeyOf(kind)).Label;
}
