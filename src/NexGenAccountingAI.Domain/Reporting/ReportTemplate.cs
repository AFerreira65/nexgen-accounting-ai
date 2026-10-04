namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>Orientação da folha impressa de um relatório.</summary>
public enum ReportOrientation
{
    Portrait = 0,
    Landscape = 1,
}

/// <summary>
/// Configuração de impressão de um relatório fixo (balancete, razão, …) numa
/// empresa: as linhas de cabeçalho próprias, se mostra a identificação do
/// gabinete e da empresa, e a orientação. À imagem dos IDU do PHC.
/// </summary>
public sealed class ReportTemplate
{
    /// <summary>Máximo de caracteres da chave do relatório.</summary>
    public const int ReportKeyMaxLength = 64;

    private ReportTemplate()
    {
    }

    private ReportTemplate(Guid id, Guid companyId, string reportKey)
    {
        Id = id;
        CompanyId = companyId;
        ReportKey = reportKey;
        HeaderLinesJson = "[]";
        ShowFirmName = true;
        ShowCompanyIdentity = true;
        Orientation = ReportOrientation.Portrait;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string ReportKey { get; private set; } = string.Empty;

    /// <summary>Linhas de cabeçalho livres, serializadas em JSON (lista de texto).</summary>
    public string HeaderLinesJson { get; private set; } = "[]";

    public bool ShowFirmName { get; private set; }

    public bool ShowCompanyIdentity { get; private set; }

    public ReportOrientation Orientation { get; private set; }

    public uint RowVersion { get; private set; }

    public static ReportTemplate Create(Guid companyId, string reportKey)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reportKey);
        return new ReportTemplate(Guid.NewGuid(), companyId, reportKey.Trim().ToLowerInvariant());
    }

    public void Update(string headerLinesJson, bool showFirmName, bool showCompanyIdentity, ReportOrientation orientation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerLinesJson);
        HeaderLinesJson = headerLinesJson;
        ShowFirmName = showFirmName;
        ShowCompanyIdentity = showCompanyIdentity;
        Orientation = orientation;
    }
}
