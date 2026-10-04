using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Cria a empresa correspondente a um ficheiro SAF-T e provisiona os cadastros
/// contabilísticos na mesma transação. Recusa criar uma empresa duplicada: se já
/// existir uma com o mesmo NIF, a operação falha sem escrever.
/// </summary>
public sealed class SaftCompanyCreationService(
    CompanyAccountingProvisioner provisioner,
    AccountingDbContext context)
{
    public async Task<SaftCompanyCreationResult> CreateCompanyAsync(
        Guid userId,
        string taxRegistrationNumber,
        string legalName,
        TaxRegion taxRegion,
        AccountingStandard accountingStandard,
        string baseCurrency,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaxNumber = (taxRegistrationNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedTaxNumber))
        {
            throw new ArgumentException("O NIF é obrigatório.", nameof(taxRegistrationNumber));
        }

        var firmId = await context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Select(item => (Guid?)item.Company.AccountingFirmId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (firmId is null)
        {
            throw new InvalidOperationException("O utilizador não pertence a nenhum gabinete.");
        }

        SaftCompanyCreationResult? result = null;

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var duplicate = await context.Companies
                .AnyAsync(item => item.TaxRegistrationNumber == normalizedTaxNumber, cancellationToken)
                .ConfigureAwait(false);
            if (duplicate)
            {
                throw new InvalidOperationException($"Já existe uma empresa com o NIF {normalizedTaxNumber}.");
            }

            var company = new Company(
                Guid.NewGuid(),
                firmId.Value,
                legalName,
                normalizedTaxNumber,
                taxRegion,
                baseCurrency,
                accountingStandard);
            context.Companies.Add(company);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await provisioner.ProvisionAsync(company.Id, cancellationToken).ConfigureAwait(false);

            context.CompanyUsers.Add(new CompanyUser(company.Id, userId, CompanyUserRole.Accountant));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var accountCount = await context.Accounts
                .CountAsync(item => item.CompanyId == company.Id, cancellationToken)
                .ConfigureAwait(false);
            var journalCount = await context.Journals
                .CountAsync(item => item.CompanyId == company.Id, cancellationToken)
                .ConfigureAwait(false);
            var documentTypeCount = await context.AccountingDocumentTypes
                .CountAsync(item => item.CompanyId == company.Id, cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            result = new SaftCompanyCreationResult(
                company.Id,
                company.LegalName,
                company.TaxRegistrationNumber,
                company.AccountingStandard.ToString(),
                company.AccountingSetupVersion,
                accountCount,
                journalCount,
                documentTypeCount);
        }).ConfigureAwait(false);

        return result!;
    }
}
