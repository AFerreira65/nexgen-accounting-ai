using System.Text.Json;
using System.Text;

namespace AccountingAI.Core.Configuration;

public sealed class ConfigurationStore
{
    private static readonly byte[] FileHeader = Encoding.ASCII.GetBytes("SAFTIA2\0");
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public ConfigurationStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public Exception? LastLoadError { get; private set; }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Accounting AI",
        "config.dat");

    public static string LegacyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Accounting AI",
        "config.json");

    public AccountingAiConfiguration Load()
    {
        LastLoadError = null;
        if (!File.Exists(FilePath))
        {
            return LoadLegacyConfiguration();
        }

        try
        {
            var fileContent = File.ReadAllBytes(FilePath);
            if (!fileContent.AsSpan().StartsWith(FileHeader))
            {
                throw new InvalidDataException("O ficheiro de configuração não tem um formato reconhecido.");
            }

            var protectedContent = fileContent.AsSpan(FileHeader.Length).ToArray();
            var json = Encoding.UTF8.GetString(WindowsDataProtection.Unprotect(protectedContent));
            var configuration = JsonSerializer.Deserialize<AccountingAiConfiguration>(
                json,
                SerializerOptions) ?? new AccountingAiConfiguration();
            return Normalize(configuration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           JsonException or InvalidDataException or
                                           System.ComponentModel.Win32Exception or
                                           System.Security.Cryptography.CryptographicException)
        {
            LastLoadError = exception;
            return new AccountingAiConfiguration();
        }
    }

    public void Save(AccountingAiConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (LastLoadError is not null && File.Exists(FilePath))
        {
            throw new InvalidOperationException(
                "A configuração encriptada existente não pôde ser aberta e não será substituída. " +
                "Confirme que está a usar o mesmo utilizador do Windows ou restaure uma cópia válida.",
                LastLoadError);
        }
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("Não foi possível determinar a pasta de configuração.");
        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";
        configuration.SchemaVersion = 8;
        var json = JsonSerializer.SerializeToUtf8Bytes(configuration, SerializerOptions);
        var protectedContent = WindowsDataProtection.Protect(json);
        var fileContent = new byte[FileHeader.Length + protectedContent.Length];
        FileHeader.CopyTo(fileContent, 0);
        protectedContent.CopyTo(fileContent, FileHeader.Length);
        File.WriteAllBytes(temporaryPath, fileContent);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private AccountingAiConfiguration LoadLegacyConfiguration()
    {
        if (!FilePath.Equals(DefaultFilePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(LegacyFilePath))
        {
            return new AccountingAiConfiguration();
        }

        try
        {
            var configuration = JsonSerializer.Deserialize<AccountingAiConfiguration>(
                File.ReadAllText(LegacyFilePath),
                SerializerOptions) ?? new AccountingAiConfiguration();
            configuration = Normalize(configuration);
            Save(configuration);
            return configuration;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LastLoadError = exception;
            return new AccountingAiConfiguration();
        }
    }

    private static AccountingAiConfiguration Normalize(AccountingAiConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.PortalDownloadDirectory))
        {
            configuration.PortalDownloadDirectory = AccountingAiConfiguration.DefaultPortalDownloadDirectory;
        }

        configuration.PhcCompanies ??= [];
        configuration.PhcOdbcCredentials ??= [];
        configuration.GridColumnWidths ??= [];
        foreach (var company in configuration.PhcCompanies)
        {
            if (string.IsNullOrWhiteSpace(company.WfaUsername) &&
                !string.IsNullOrWhiteSpace(configuration.WfaUsername) &&
                configuration.WfaUsername.StartsWith(company.TaxRegistrationNumber + "/", StringComparison.Ordinal))
            {
                company.WfaUsername = configuration.WfaUsername;
            }

            company.InvoiceSeriesMappings ??= [];
            company.ReceiptSeriesMappings ??= [];
            company.VatArticleMappings ??= [];
            company.PurchaseAuditTotalTolerance = Math.Max(0m, company.PurchaseAuditTotalTolerance);
            company.PurchaseAuditVatTolerance = Math.Max(0m, company.PurchaseAuditVatTolerance);
            company.PurchaseAuditDateToleranceDays = Math.Max(0, company.PurchaseAuditDateToleranceDays);
            if (company.PurchaseAuditDocumentSuffixLength is < 3 or > 20)
            {
                company.PurchaseAuditDocumentSuffixLength = 6;
            }
        }
        return configuration;
    }
}
