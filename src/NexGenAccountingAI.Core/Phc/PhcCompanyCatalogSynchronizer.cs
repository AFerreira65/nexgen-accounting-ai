using NexGenAccountingAI.Core.Configuration;

namespace NexGenAccountingAI.Core.Phc;

public sealed record PhcCatalogSyncResult(int Added, int Updated, int Available, bool Changed);

public static class PhcCompanyCatalogSynchronizer
{
    public static PhcCatalogSyncResult Synchronize(
        AccountingAiConfiguration configuration,
        IEnumerable<PhcCompanyCatalogEntry> entries,
        DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(entries);
        configuration.PhcCompanies ??= [];
        configuration.PhcOdbcCredentials ??= [];

        var changed = false;
        foreach (var company in configuration.PhcCompanies.Where(company => company.PresentInEp))
        {
            company.PresentInEp = false;
            changed = true;
        }

        var added = 0;
        var updated = 0;
        var observedAt = observedAtUtc ?? DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            var company = configuration.PhcCompanies.FirstOrDefault(existing => SameCompany(existing, entry));
            if (company is null)
            {
                company = new PhcCompanyConfiguration
                {
                    Name = entry.Name,
                    DatabaseName = entry.DatabaseName,
                    OdbcName = entry.OdbcName,
                    PresentInEp = true,
                    LastSeenUtc = observedAt
                };
                configuration.PhcCompanies.Add(company);
                added++;
                changed = true;
            }
            else
            {
                var entryChanged = !company.Name.Equals(entry.Name, StringComparison.Ordinal) ||
                                   !company.PresentInEp || company.LastSeenUtc != observedAt;
                company.Name = entry.Name;
                company.PresentInEp = true;
                company.LastSeenUtc = observedAt;
                if (entryChanged)
                {
                    updated++;
                    changed = true;
                }
            }

            if (entry.OdbcName.Length > 0 && !configuration.PhcOdbcCredentials.Any(
                    credential => credential.OdbcName.Equals(entry.OdbcName, StringComparison.OrdinalIgnoreCase)))
            {
                configuration.PhcOdbcCredentials.Add(new PhcOdbcCredential { OdbcName = entry.OdbcName });
                changed = true;
            }
        }

        return new PhcCatalogSyncResult(added, updated, configuration.PhcCompanies.Count(company => company.PresentInEp), changed);
    }

    private static bool SameCompany(PhcCompanyConfiguration company, PhcCompanyCatalogEntry entry) =>
        company.OdbcName.Equals(entry.OdbcName, StringComparison.OrdinalIgnoreCase) &&
        company.DatabaseName.Equals(entry.DatabaseName, StringComparison.OrdinalIgnoreCase);
}
