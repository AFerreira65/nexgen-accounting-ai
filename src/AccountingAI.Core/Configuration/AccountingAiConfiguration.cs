using AccountingAI.Core.Efatura;

namespace AccountingAI.Core.Configuration;

public sealed class AccountingAiConfiguration
{
    public int SchemaVersion { get; set; } = 8;

    public string CustomerTaxId { get; set; } = string.Empty;

    public string WfaUsername { get; set; } = string.Empty;

    public string ProducerPfxPath { get; set; } = string.Empty;

    public string AuthenticationCertificatePath { get; set; } = string.Empty;

    public EfaturaAtEnvironment AtEnvironment { get; set; } = EfaturaAtEnvironment.Production;

    public string PortalDownloadDirectory { get; set; } = DefaultPortalDownloadDirectory;

    public string PhcDirectory { get; set; } = string.Empty;

    public List<PhcCompanyConfiguration> PhcCompanies { get; set; } = [];

    public List<PhcOdbcCredential> PhcOdbcCredentials { get; set; } = [];

    public Dictionary<string, Dictionary<string, int>> GridColumnWidths { get; set; } = [];

    public static string DefaultPortalDownloadDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Accounting AI",
        "e-Fatura",
        "Downloads");

    public AccountingAiConfiguration Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        CustomerTaxId = CustomerTaxId,
        WfaUsername = WfaUsername,
        ProducerPfxPath = ProducerPfxPath,
        AuthenticationCertificatePath = AuthenticationCertificatePath,
        AtEnvironment = AtEnvironment,
        PortalDownloadDirectory = PortalDownloadDirectory,
        PhcDirectory = PhcDirectory,
        PhcCompanies = PhcCompanies.Select(company => company.Clone()).ToList(),
        PhcOdbcCredentials = PhcOdbcCredentials.Select(credential => credential.Clone()).ToList(),
        GridColumnWidths = GridColumnWidths.ToDictionary(
            grid => grid.Key,
            grid => grid.Value.ToDictionary(column => column.Key, column => column.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase)
    };
}
