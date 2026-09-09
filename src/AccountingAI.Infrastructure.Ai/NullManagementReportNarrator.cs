using AccountingAI.Application.ManagementReports;

namespace AccountingAI.Infrastructure.Ai;

/// <summary>
/// O narrador para quando não há chave de API: devolve sempre um resultado
/// explícito «IA não configurada», sem chamar nada. É também para onde o
/// <see cref="ClaudeManagementReportNarrator"/> encaminha se arrancar sem chave.
/// </summary>
public sealed class NullManagementReportNarrator : IManagementReportNarrator
{
    internal static readonly ManagementReportNarration NotConfigured = new(
        new ManagementReportResult(
            "Os relatórios de gestão com IA não estão configurados.",
            [],
            [
                "Defina a chave da API em Ai:ApiKey (user-secrets) ou na variável de ambiente " +
                "ANTHROPIC_API_KEY para gerar relatórios com IA.",
            ],
            [],
            ManagementReportResult.Notice),
        "—",
        0,
        0);

    public bool IsConfigured => false;

    public Task<ManagementReportNarration> GenerateAsync(
        ManagementReportRequest request,
        ManagementReportDossier dossier,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NotConfigured);
}
