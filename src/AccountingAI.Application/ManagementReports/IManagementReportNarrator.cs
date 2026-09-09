namespace AccountingAI.Application.ManagementReports;

/// <summary>
/// O resultado de uma geração: o relatório redigido mais os metadados da chamada
/// ao modelo (para persistir e mostrar).
/// </summary>
public sealed record ManagementReportNarration(
    ManagementReportResult Result,
    string Model,
    int PromptTokens,
    int OutputTokens);

/// <summary>
/// Transforma um <see cref="ManagementReportDossier"/> num
/// <see cref="ManagementReportResult"/> redigido. A implementação real
/// (<c>ClaudeManagementReportNarrator</c>) vive em
/// <c>AccountingAI.Infrastructure.Ai</c> e é a única fronteira que conhece o SDK
/// (ADR-0006). Quando não há chave configurada, o registo cai no narrador nulo e
/// <see cref="IsConfigured"/> é <c>false</c>.
/// </summary>
public interface IManagementReportNarrator
{
    /// <summary>`true` quando há uma chave de API configurada.</summary>
    bool IsConfigured { get; }

    /// <summary>O nome do modelo configurado (ou <c>"—"</c> quando não há).</summary>
    string Model { get; }

    Task<ManagementReportNarration> GenerateAsync(
        ManagementReportRequest request,
        ManagementReportDossier dossier,
        CancellationToken cancellationToken = default);
}
