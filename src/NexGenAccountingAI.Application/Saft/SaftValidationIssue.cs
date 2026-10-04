using NexGenAccountingAI.Domain.Saft;

namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Uma ocorrência num relatório de validação de SAF-T. Vem da camada de
/// esquema (<see cref="RuleId"/> nulo, com <see cref="Line"/>/<see cref="Position"/>)
/// ou de uma regra analítica (com <see cref="RuleId"/>).
/// </summary>
/// <param name="RuleId">Identificador da regra analítica; nulo quando a ocorrência é do XSD.</param>
/// <param name="Severity">Gravidade da ocorrência.</param>
/// <param name="Scope">Âmbito: <c>Header</c>, <c>MasterFiles</c>, <c>GeneralLedgerEntries</c> ou <c>Ficheiro</c>.</param>
/// <param name="Message">Mensagem em pt-PT.</param>
/// <param name="Line">Linha no ficheiro, quando conhecida (ocorrências do XSD).</param>
/// <param name="Position">Coluna na linha, quando conhecida.</param>
/// <param name="Path">Caminho do elemento, quando conhecido.</param>
public sealed record SaftValidationIssue(
    string? RuleId,
    SaftValidationSeverity Severity,
    string Scope,
    string Message,
    int? Line = null,
    int? Position = null,
    string? Path = null);
