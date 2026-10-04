namespace NexGenAccountingAI.Domain.Saft;

/// <summary>
/// Registo persistido de uma execução da validação de um SAF-T: o resumo e as
/// ocorrências (em JSON), para o histórico por empresa. É um valor de leitura;
/// não representa o ficheiro nem a empresa.
/// </summary>
public sealed class SaftValidationRun
{
    private SaftValidationRun()
    {
    }

    private SaftValidationRun(
        Guid id,
        Guid? companyId,
        Guid userId,
        string fileName,
        string? auditFileVersion,
        bool schemaAvailable,
        bool schemaValid,
        int errorCount,
        int warningCount,
        int informationCount,
        bool truncated,
        string issuesJson,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        UserId = userId;
        FileName = fileName;
        AuditFileVersion = auditFileVersion;
        SchemaAvailable = schemaAvailable;
        SchemaValid = schemaValid;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        InformationCount = informationCount;
        Truncated = truncated;
        IssuesJson = issuesJson;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid? CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string? AuditFileVersion { get; private set; }

    public bool SchemaAvailable { get; private set; }

    public bool SchemaValid { get; private set; }

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public int InformationCount { get; private set; }

    public bool Truncated { get; private set; }

    /// <summary>Lista de ocorrências serializada em JSON.</summary>
    public string IssuesJson { get; private set; } = "[]";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static SaftValidationRun Create(
        Guid? companyId,
        Guid userId,
        string fileName,
        string? auditFileVersion,
        bool schemaAvailable,
        bool schemaValid,
        int errorCount,
        int warningCount,
        int informationCount,
        bool truncated,
        string issuesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuesJson);

        return new SaftValidationRun(
            Guid.NewGuid(),
            companyId,
            userId,
            fileName.Trim(),
            string.IsNullOrWhiteSpace(auditFileVersion) ? null : auditFileVersion.Trim(),
            schemaAvailable,
            schemaValid,
            Math.Max(0, errorCount),
            Math.Max(0, warningCount),
            Math.Max(0, informationCount),
            truncated,
            issuesJson,
            DateTimeOffset.UtcNow);
    }
}
