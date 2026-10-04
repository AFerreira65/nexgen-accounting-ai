using NexGenAccountingAI.Domain.Saft;

namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Resultado da validação de um ficheiro SAF-T. Nesta fase cobre a camada de
/// esquema (XSD); as regras analíticas juntam as suas ocorrências à mesma
/// lista num incremento seguinte.
/// </summary>
/// <param name="Version">Versão declarada no <c>AuditFileVersion</c>, ou derivada do namespace; nula se ilegível.</param>
/// <param name="SchemaAvailable">Verdadeiro se havia um esquema oficial para a versão do ficheiro.</param>
/// <param name="SchemaValid">Verdadeiro se o esquema estava disponível e não houve ocorrências de erro do XSD.</param>
/// <param name="Issues">Ocorrências encontradas, já limitadas.</param>
/// <param name="Truncated">Verdadeiro se o número de ocorrências excedeu o limite e a lista foi cortada.</param>
public sealed record SaftValidationReport(
    string? Version,
    bool SchemaAvailable,
    bool SchemaValid,
    IReadOnlyList<SaftValidationIssue> Issues,
    bool Truncated)
{
    public int ErrorCount => Issues.Count(issue => issue.Severity == SaftValidationSeverity.Error);

    public int WarningCount => Issues.Count(issue => issue.Severity == SaftValidationSeverity.Warning);

    public int InformationCount => Issues.Count(issue => issue.Severity == SaftValidationSeverity.Information);
}
