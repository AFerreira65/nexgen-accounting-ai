using AccountingAI.Core.Configuration;
using AccountingAI.Core.Phc;

namespace AccountingAI.WinForms.Dialogs;

internal static class PhcCredentialPrompt
{
    public static async Task<PhcOdbcCredential?> GetOrPromptAsync(
        IWin32Window owner,
        AccountingAiConfiguration configuration,
        ConfigurationStore store,
        PhcCompanyConfiguration company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(company);
        if (string.IsNullOrWhiteSpace(company.OdbcName))
        {
            throw new ArgumentException("A empresa PHC não tem um ODBC definido.", nameof(company));
        }

        var existing = configuration.PhcOdbcCredentials.FirstOrDefault(item =>
            item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Username.Length > 0 && existing.Password.Length > 0 && existing.LastTestSucceeded)
        {
            return existing;
        }

        using var dialog = new PhcCredentialDialog(
            company.OdbcName,
            existing?.Username ?? string.Empty,
            existing?.Password ?? string.Empty);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        var candidate = new PhcOdbcCredential
        {
            OdbcName = company.OdbcName,
            Username = dialog.Username,
            Password = dialog.Password
        };
        var result = await new PhcOdbcDataSource().TestConnectionAsync(company, candidate, cancellationToken);
        MessageBox.Show(
            owner,
            result.Message,
            result.Success ? "Ligação PHC válida" : "Falha na ligação PHC",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        if (!result.Success)
        {
            return null;
        }

        candidate.LastTestSucceeded = true;
        candidate.LastTestedUtc = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            configuration.PhcOdbcCredentials.Add(candidate);
            existing = candidate;
        }
        else
        {
            existing.Username = candidate.Username;
            existing.Password = candidate.Password;
            existing.LastTestSucceeded = true;
            existing.LastTestedUtc = candidate.LastTestedUtc;
        }

        store.Save(configuration);
        return existing;
    }
}
