using AccountingAI.Application.Irc;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// Calcula a estimativa de IRC de um período a partir do resultado líquido
/// (mesma aritmética da Demonstração de Resultados, via
/// <see cref="AccountLedgerService.TrialBalanceAsync"/>), somando as correções
/// fiscais e aplicando a taxa que o utilizador introduz em cada cálculo. Não
/// persiste nada; não sugere correções nem taxa. Não é o Modelo 22.
/// </summary>
public sealed class IrcEstimateService(AccountingDbContext context, AccountLedgerService ledger)
{
    public async Task<IrcEstimate?> BuildAsync(
        Guid userId,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        decimal taxRate,
        IReadOnlyList<IrcAdjustment> adjustments,
        CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var trialBalance = await ledger.TrialBalanceAsync(companyId, from, to, cancellationToken).ConfigureAwait(false);

        var income = trialBalance.Rows
            .Where(row => row.AccountCode.StartsWith('7'))
            .Sum(row => row.Credit - row.Debit);
        var expenses = trialBalance.Rows
            .Where(row => row.AccountCode.StartsWith('6'))
            .Sum(row => row.Debit - row.Credit);
        var netIncome = decimal.Round(income - expenses, 2);

        var adjustmentsTotal = decimal.Round(adjustments.Sum(adjustment => adjustment.Amount), 2);
        var taxableResult = decimal.Round(netIncome + adjustmentsTotal, 2);
        var estimatedTax = taxableResult > 0m ? decimal.Round(taxableResult * taxRate, 2) : 0m;

        return new IrcEstimate(from, to, netIncome, adjustments, adjustmentsTotal, taxableResult, taxRate, estimatedTax);
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
