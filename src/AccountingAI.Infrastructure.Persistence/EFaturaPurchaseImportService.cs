using AccountingAI.Application.Efatura;
using AccountingAI.Domain.Efatura;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>
/// Importa para uma empresa as faturas de um CSV de compras do e-Fatura. É
/// idempotente pela chave <c>NIF|nº|data</c>: reimportar o mesmo CSV não
/// duplica. As linhas incompletas são ignoradas e reportadas em pt-PT. Uma só
/// transação envolve toda a importação; <see cref="ImportAsync"/> com
/// <c>dryRun</c> não escreve nada.
/// </summary>
public sealed class EFaturaPurchaseImportService(IEFaturaCsvReader reader, AccountingDbContext context)
{
    private const int InvalidRowLimit = 50;

    public async Task<EFaturaImportResult?> ImportAsync(
        Guid userId,
        Guid companyId,
        Stream content,
        string fileName,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var read = await reader.ReadAsync(content, cancellationToken).ConfigureAwait(false);
        var valid = read.Rows.Where(row => row.IsValid).ToList();
        var invalidRows = read.Rows
            .Where(row => !row.IsValid)
            .Take(InvalidRowLimit)
            .Select(row => new EFaturaInvalidRow(row.LineNumber, row.Error!))
            .ToList();
        var invalidCount = read.Rows.Count(row => !row.IsValid);

        var keys = valid
            .Select(row => EFaturaPurchase.KeyOf(row.SupplierTaxId, row.InvoiceNo, row.InvoiceDate!.Value))
            .ToList();
        var existingKeys = keys.Count == 0
            ? []
            : await context.EFaturaPurchases
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && keys.Contains(item.DocumentKey))
                .Select(item => item.DocumentKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var existing = existingKeys.ToHashSet(StringComparer.Ordinal);

        var batchId = Guid.NewGuid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var imported = 0;
        var alreadyExisted = 0;
        foreach (var row in valid)
        {
            var key = EFaturaPurchase.KeyOf(row.SupplierTaxId, row.InvoiceNo, row.InvoiceDate!.Value);
            if (existing.Contains(key) || !seen.Add(key))
            {
                alreadyExisted++;
                continue;
            }

            imported++;
            if (!dryRun)
            {
                context.EFaturaPurchases.Add(EFaturaPurchase.Create(
                    companyId, batchId,
                    row.SupplierTaxId, row.SupplierName, row.InvoiceNo, row.Atcud, row.InvoiceDate.Value,
                    row.InvoiceType, row.Status, row.Sector,
                    row.TaxBase, row.TaxAmount, row.GrossTotal, fileName));
            }
        }

        if (dryRun)
        {
            return new EFaturaImportResult(null, imported, alreadyExisted, invalidCount, invalidRows, read.Warnings);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new EFaturaImportResult(batchId, imported, alreadyExisted, invalidCount, invalidRows, read.Warnings);
    }
}
