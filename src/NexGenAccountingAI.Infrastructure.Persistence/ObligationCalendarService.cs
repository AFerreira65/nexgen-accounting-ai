using NexGenAccountingAI.Application.Fiscal;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha do calendário: a obrigação num período, o prazo e o que está registado.</summary>
public sealed record ObligationRow(
    string Code,
    string Name,
    string PeriodKey,
    string PeriodLabel,
    DateOnly LegalDueDate,
    DateOnly DueDate,
    bool DueDateChanged,
    string State,
    DateOnly? DeliveredOn,
    string? Note,
    string Reason,
    string Source)
{
    public bool Overdue(DateOnly today) => State == nameof(TaxObligationState.Pendente) && DueDate < today;
}

public sealed record ObligationCalendar(
    DateOnly From,
    DateOnly To,
    string VatPeriodicity,
    bool VatPeriodicityConfigured,
    IReadOnlyList<ObligationRow> Rows,
    string NotIncluded);

public sealed record ObligationUpdate(string State, DateOnly? DeliveredOn, string? Note, DateOnly? DueDateOverride);

/// <summary>
/// O calendário de obrigações de uma empresa (F3-27A). Parte do
/// <see cref="ObligationCatalog"/> e decide pelos dados quais se aplicam em cada
/// período: o IVA pela periodicidade das credenciais da AT (mensal, quando não
/// estão configuradas); a recapitulativa quando há linhas com naturezas
/// intracomunitárias (13 a 15) no período; as retenções quando há créditos nas
/// contas 242 no mês; a declaração mensal de remunerações quando há gastos nas
/// contas 63 no mês; o Modelo 10 quando houve retenções no ano anterior; o
/// Modelo 22, a IES e os pagamentos por conta sempre. Guarda, por período, o
/// estado, a data de entrega, uma nota e o prazo alterado.
/// </summary>
public sealed class ObligationCalendarService(AccountingDbContext context)
{
    private const int MaxDays = 400;

    public async Task<ObligationCalendar?> CalendarAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default, bool includeAll = false)
    {
        if (to < from || to.DayNumber - from.DayNumber > MaxDays)
        {
            throw new ArgumentException($"Indique um período válido, até {MaxDays} dias.", nameof(to));
        }

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var periodicity = await context.CompanyAtCredentials
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => (VatFilingPeriodicity?)item.Periodicity)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var quarterly = periodicity == VatFilingPeriodicity.Trimestral;

        // A recapitulativa e as obrigações por mês só aparecem nos períodos com operações; os prazos podem cair até um
        // ano depois do período, por isso olha-se o ano anterior ao início da janela.
        var activityFrom = new DateOnly(from.Year - 2, 1, 1);
        var activity = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.Source != EntrySource.Opening &&
                line.JournalEntry.Source != EntrySource.Closing &&
                line.JournalEntry.EntryDate >= activityFrom &&
                line.JournalEntry.EntryDate <= to &&
                ((line.VatCode != null &&
                    (line.VatCode.Nature == VatOperationNature.TransmissoesIntracomunitariasBens ||
                     line.VatCode.Nature == VatOperationNature.PrestacoesServicosIntracomunitarias ||
                     line.VatCode.Nature == VatOperationNature.OperacoesTriangulares)) ||
                 (line.Account.Code.StartsWith("242") && line.Credit > 0) ||
                 (line.Account.Code.StartsWith("63") && line.Debit > 0) ||
                 ((line.Account.Code.StartsWith("71") || line.Account.Code.StartsWith("72")) && line.Credit > 0)))
            .Select(line => new
            {
                line.JournalEntry.EntryDate,
                IntraCommunity = line.VatCode != null &&
                    (line.VatCode.Nature == VatOperationNature.TransmissoesIntracomunitariasBens ||
                     line.VatCode.Nature == VatOperationNature.PrestacoesServicosIntracomunitarias ||
                     line.VatCode.Nature == VatOperationNature.OperacoesTriangulares),
                Withholding = line.Account.Code.StartsWith("242") && line.Credit > 0,
                Payroll = line.Account.Code.StartsWith("63") && line.Debit > 0,
                Sales = (line.Account.Code.StartsWith("71") || line.Account.Code.StartsWith("72")) && line.Credit > 0,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool Any(Func<DateOnly, bool> inPeriod, Func<(bool Intra, bool Withholding, bool Payroll, bool Sales), bool> kind) =>
            activity.Any(item => inPeriod(item.EntryDate) && kind((item.IntraCommunity, item.Withholding, item.Payroll, item.Sales)));

        var records = await context.TaxObligationRecords
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byKey = records.ToDictionary(item => (item.Code, item.PeriodKey));

        // As datas alteradas à mão podem tirar ou trazer um período para a janela: olha-se uma margem à volta.
        var occurrences = ObligationCatalog.Between(from.AddDays(-60), to.AddDays(60));
        var rows = new List<ObligationRow>();
        foreach (var occurrence in occurrences)
        {
            var code = occurrence.Definition.Code;
            bool InPeriod(DateOnly date) => date >= occurrence.PeriodStart && date <= occurrence.PeriodEnd;
            var reason = code switch
            {
                ObligationCatalog.VatReturnMonthly or ObligationCatalog.VatPaymentMonthly => quarterly ? null : "IVA mensal",
                ObligationCatalog.VatReturnQuarterly or ObligationCatalog.VatPaymentQuarterly => quarterly ? "IVA trimestral" : null,
                ObligationCatalog.RecapitulativeMonthly => !quarterly && Any(InPeriod, kind => kind.Intra) ? "Operações intracomunitárias no mês" : null,
                ObligationCatalog.RecapitulativeQuarterly => quarterly && Any(InPeriod, kind => kind.Intra) ? "Operações intracomunitárias no trimestre" : null,
                ObligationCatalog.Withholdings => Any(InPeriod, kind => kind.Withholding) ? "Retenções no mês (contas 242)" : null,
                ObligationCatalog.MonthlyPayroll => Any(InPeriod, kind => kind.Payroll) ? "Remunerações no mês (contas 63)" : null,
                ObligationCatalog.Modelo10 => Any(InPeriod, kind => kind.Withholding) ? "Retenções no ano" : null,
                ObligationCatalog.InvoiceCommunication => Any(InPeriod, kind => kind.Sales) ? "Vendas no mês (contas 71 e 72)" : null,
                ObligationCatalog.StampDuty => null,
                _ => "Sujeito passivo de IRC",
            };

            byKey.TryGetValue((code, occurrence.PeriodKey), out var record);
            if (reason is null && record is null && !includeAll)
            {
                continue;
            }

            var dueDate = record?.DueDateOverride ?? occurrence.DueDate;
            if (dueDate < from || dueDate > to)
            {
                continue;
            }

            rows.Add(new ObligationRow(
                code,
                occurrence.Definition.Name,
                occurrence.PeriodKey,
                occurrence.PeriodLabel,
                occurrence.DueDate,
                dueDate,
                record?.DueDateOverride is not null,
                (record?.State ?? TaxObligationState.Pendente).ToString(),
                record?.DeliveredOn,
                record?.Note,
                reason ?? (record is null ? "Os lançamentos não a indicam" : "Registada à mão"),
                occurrence.Definition.Source));
        }

        return new ObligationCalendar(
            from, to,
            quarterly ? "Trimestral" : "Mensal",
            periodicity is not null,
            rows.OrderBy(row => row.DueDate).ThenBy(row => row.Code, StringComparer.Ordinal).ToList(),
            ObligationCatalog.NotIncluded);
    }

    public async Task<bool?> UpdateAsync(
        Guid userId, Guid companyId, string code, string periodKey, ObligationUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var definition = ObligationCatalog.Find(code);
        if (!Enum.TryParse<TaxObligationState>(update.State, ignoreCase: true, out var state))
        {
            throw new ArgumentException("O estado tem de ser Pendente, Entregue ou Dispensada.", nameof(update));
        }

        var normalizedPeriod = (periodKey ?? string.Empty).Trim().ToUpperInvariant();
        var record = await context.TaxObligationRecords
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == definition.Code && item.PeriodKey == normalizedPeriod, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = TaxObligationRecord.Create(companyId, definition.Code, normalizedPeriod);
            context.TaxObligationRecords.Add(record);
        }

        record.Update(state, update.DeliveredOn, update.Note, update.DueDateOverride);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
