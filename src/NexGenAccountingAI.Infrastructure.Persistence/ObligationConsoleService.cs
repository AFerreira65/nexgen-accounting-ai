using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma obrigação de uma empresa na consola do gabinete.</summary>
public sealed record ObligationConsoleRow(
    Guid CompanyId,
    string CompanyName,
    string TaxRegistrationNumber,
    bool CanEdit,
    ObligationRow Obligation);

public sealed record ObligationConsole(
    DateOnly From,
    DateOnly To,
    int CompanyCount,
    IReadOnlyList<ObligationConsoleRow> Rows);

/// <summary>Uma obrigação a atualizar: a empresa, o código e o período.</summary>
public sealed record ObligationTarget(Guid CompanyId, string Code, string PeriodKey);

/// <summary>
/// A consola de obrigações em lote (F4E-02): as obrigações de todas as empresas a que o utilizador tem
/// acesso, num só ecrã, e a atualização do estado de várias de uma vez. Cada empresa só entra se o
/// grupo de permissões do utilizador nela inclui <c>obligations.view</c>, e só se altera com
/// <c>obligations.edit</c> (ADR-0018) — como estas rotas não têm empresa, a verificação é feita aqui.
/// </summary>
public sealed class ObligationConsoleService(
    AccountingDbContext context, ObligationCalendarService calendar, PermissionService permissions)
{
    private const int MaxBatch = 500;

    public async Task<ObligationConsole> BuildAsync(
        Guid userId, DateOnly from, DateOnly to, Guid? responsibleUserId, CancellationToken cancellationToken = default)
    {
        var companies = await context.CompanyUsers
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive && item.Company.IsActive &&
                (responsibleUserId == null || item.Company.UserMemberships.Any(other => other.UserId == responsibleUserId && other.IsActive && other.IsResponsible)))
            .Select(item => new { item.Company.Id, item.Company.LegalName, item.Company.TaxRegistrationNumber })
            .OrderBy(item => item.LegalName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<ObligationConsoleRow>();
        var counted = 0;
        foreach (var company in companies)
        {
            var effective = await permissions.ResolveAsync(userId, company.Id, cancellationToken).ConfigureAwait(false);
            if (!effective.Has("obligations.view"))
            {
                continue;
            }

            counted++;
            var result = await calendar.CalendarAsync(userId, company.Id, from, to, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                continue;
            }

            var canEdit = effective.Has("obligations.edit");
            rows.AddRange(result.Rows.Select(row => new ObligationConsoleRow(company.Id, company.LegalName, company.TaxRegistrationNumber, canEdit, row)));
        }

        return new ObligationConsole(
            from,
            to,
            counted,
            rows.OrderBy(item => item.Obligation.DueDate).ThenBy(item => item.CompanyName, StringComparer.CurrentCulture).ThenBy(item => item.Obligation.Name, StringComparer.CurrentCulture).ToList());
    }

    /// <summary>Atualiza várias obrigações de uma vez. Recusa tudo se alguma empresa não permite alterar.</summary>
    public async Task<int> UpdateBatchAsync(
        Guid userId, IReadOnlyList<ObligationTarget> targets, ObligationUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            throw new ArgumentException("Escolha pelo menos uma obrigação.", nameof(targets));
        }

        if (targets.Count > MaxBatch)
        {
            throw new ArgumentException($"Escolha no máximo {MaxBatch} obrigações de cada vez.", nameof(targets));
        }

        foreach (var companyId in targets.Select(item => item.CompanyId).Distinct())
        {
            var effective = await permissions.ResolveAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
            if (!effective.IsMember)
            {
                throw new KeyNotFoundException("A empresa não existe.");
            }

            if (!effective.Has("obligations.edit"))
            {
                var name = await context.Companies.AsNoTracking().Where(item => item.Id == companyId).Select(item => item.LegalName).SingleAsync(cancellationToken).ConfigureAwait(false);
                throw new OrganizationAccessDeniedException($"Não tem permissão para registar entregas de {name}.");
            }
        }

        var done = 0;
        foreach (var target in targets.DistinctBy(item => (item.CompanyId, item.Code, item.PeriodKey)))
        {
            if (await calendar.UpdateAsync(userId, target.CompanyId, target.Code, target.PeriodKey, update, cancellationToken).ConfigureAwait(false) is true)
            {
                done++;
            }
        }

        return done;
    }
}
