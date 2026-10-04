namespace NexGenAccountingAI.Application.ManagementReports;

/// <summary>
/// Os tipos de relatório de gestão pré-definidos. Um pedido «a pedido» (pergunta
/// livre) não usa nenhum destes — leva <see cref="ManagementReportRequest.Question"/>.
/// </summary>
public enum ManagementReportKind
{
    /// <summary>Síntese mensal: resultado, variação homóloga, desvios e alertas.</summary>
    SinteseMensal = 0,

    /// <summary>Análise de rentabilidade: margens, peso dos gastos, evolução.</summary>
    AnaliseRentabilidade = 1,

    /// <summary>Posição financeira: ativo, passivo, capital próprio, liquidez.</summary>
    PosicaoFinanceira = 2,

    /// <summary>Revisão de IVA: liquidado, dedutível, cruzamento com o e-Fatura.</summary>
    RevisaoIva = 3,

    /// <summary>Alertas de fecho: contas e movimentos que merecem revisão antes do fecho.</summary>
    AlertasDeFecho = 4,
}
