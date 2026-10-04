using AccountingAI.Application.Saft;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>
/// Lê o cabeçalho de um ficheiro SAF-T e procura uma empresa acessível ao utilizador
/// com o mesmo NIF. Não cria nem altera nada: o passo seguinte (confirmar norma,
/// provisionar, pré-visualizar e importar) é explícito e posterior.
/// </summary>
public sealed class SaftInspectionService(ISaftHeaderReader headerReader, AccountingDbContext context)
{
    public async Task<SaftInspectionResult> InspectAsync(
        Guid userId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var header = await headerReader.ReadAsync(content, cancellationToken).ConfigureAwait(false);

        var company = await context.Companies
            .AsNoTracking()
            .Where(item => item.TaxRegistrationNumber == header.TaxRegistrationNumber &&
                item.IsActive &&
                item.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .OrderBy(item => item.LegalName)
            .Select(item => new
            {
                item.Id,
                item.LegalName,
                item.AccountingStandard,
                item.AccountingSetupVersion
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var match = company is null
            ? new SaftCompanyMatch(SaftCompanyMatchOutcome.NewCompanyProposed, null, null, null, null)
            : new SaftCompanyMatch(
                SaftCompanyMatchOutcome.ExistingCompany,
                company.Id,
                company.LegalName,
                company.AccountingStandard.ToString(),
                company.AccountingSetupVersion);

        return new SaftInspectionResult(header, match);
    }
}
