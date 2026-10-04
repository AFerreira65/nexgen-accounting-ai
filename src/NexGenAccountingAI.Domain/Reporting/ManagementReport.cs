namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>
/// Um relatório de gestão gerado por IA: o tipo (ou <c>"pergunta"</c>), o
/// período, o modelo usado, e o dossiê e o resultado serializados. O dossiê fica
/// guardado para o relatório ser reproduzível e inspecionável.
/// </summary>
public sealed class ManagementReport
{
    public const int KindMaxLength = 40;
    public const int QuestionMaxLength = 500;
    public const int ModelMaxLength = 80;

    private ManagementReport()
    {
    }

    private ManagementReport(
        Guid id,
        Guid companyId,
        string kind,
        string? question,
        DateOnly periodFrom,
        DateOnly periodTo,
        string model,
        string dossierJson,
        string resultJson,
        int promptTokens,
        int outputTokens)
    {
        Id = id;
        CompanyId = companyId;
        Kind = kind;
        Question = question;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        Model = model;
        DossierJson = dossierJson;
        ResultJson = resultJson;
        PromptTokens = promptTokens;
        OutputTokens = outputTokens;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>A chave do tipo pré-definido, ou <c>"pergunta"</c> quando é livre.</summary>
    public string Kind { get; private set; } = string.Empty;

    public string? Question { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    public string Model { get; private set; } = string.Empty;

    /// <summary>O <c>ManagementReportDossier</c> serializado em JSON.</summary>
    public string DossierJson { get; private set; } = "{}";

    /// <summary>O <c>ManagementReportResult</c> serializado em JSON.</summary>
    public string ResultJson { get; private set; } = "{}";

    public int PromptTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public uint RowVersion { get; private set; }

    public static ManagementReport Create(
        Guid companyId,
        string kind,
        string? question,
        DateOnly periodFrom,
        DateOnly periodTo,
        string model,
        string dossierJson,
        string resultJson,
        int promptTokens,
        int outputTokens)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultJson);

        return new ManagementReport(
            Guid.NewGuid(),
            companyId,
            Trim(kind, KindMaxLength),
            string.IsNullOrWhiteSpace(question) ? null : Trim(question, QuestionMaxLength),
            periodFrom,
            periodTo,
            Trim(model, ModelMaxLength),
            dossierJson,
            resultJson,
            Math.Max(0, promptTokens),
            Math.Max(0, outputTokens));
    }

    private static string Trim(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
