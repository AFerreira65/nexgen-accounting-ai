namespace NexGenAccountingAI.Application.ManagementReports;

/// <summary>
/// Uma ligação a um elemento de origem citado numa secção da resposta.
/// <see cref="Type"/> é <c>"conta"</c> ou <c>"movimento"</c>; <see cref="Code"/>
/// é o código da conta ou a referência do lançamento.
/// </summary>
public sealed record ManagementReportReference(string Type, string Code);

/// <summary>Uma secção redigida da resposta, com as suas ligações à origem.</summary>
public sealed record ManagementReportSection(
    string Title,
    string Body,
    IReadOnlyList<ManagementReportReference> References);

/// <summary>Um indicador quantificado que a resposta destaca.</summary>
public sealed record ManagementReportIndicator(string Name, string Value, string? Comment);

/// <summary>
/// O resultado estruturado de um relatório de gestão. É o contrato de saída do
/// <c>IManagementReportNarrator</c> (F2-05B); <see cref="AiNotice"/> é preenchido
/// sempre do nosso lado, nunca pelo modelo.
/// </summary>
public sealed record ManagementReportResult(
    string Summary,
    IReadOnlyList<ManagementReportSection> Sections,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<ManagementReportIndicator> Indicators,
    string AiNotice)
{
    /// <summary>
    /// O aviso fixo (pt-PT) acrescentado a todos os relatórios gerados por IA.
    /// </summary>
    public const string Notice =
        "Texto gerado por IA a partir de dados reconciliados; é um apoio à análise " +
        "e não substitui a revisão nem as peças contabilísticas oficiais.";
}
