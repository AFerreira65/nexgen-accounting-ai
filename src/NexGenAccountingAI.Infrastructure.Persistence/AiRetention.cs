using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// O prazo de guarda das conversas com o assistente (por empresa): <c>0</c> não guarda nenhuma (apaga as que existem),
/// <c>N</c> apaga as que não mexem há mais de N dias, sem prazo não apaga nada. Os relatórios de gestão gerados não entram aqui:
/// são documentos da empresa, não conversas.
/// </summary>
public sealed class AiRetentionService(AccountingDbContext context, TimeProvider time)
{
    /// <returns>Quantas conversas se apagaram.</returns>
    public async Task<int> PurgeCompanyAsync(Guid companyId, int? retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays is not { } days || days < 0)
        {
            return 0;
        }

        var cutoff = time.GetUtcNow().AddDays(-days);
        return await context.AiConversations
            .Where(conversation => conversation.CompanyId == companyId && (days == 0 || conversation.UpdatedAtUtc < cutoff))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Aplica o prazo de todas as empresas que o definiram.</summary>
    public async Task<int> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        var rules = await context.CompanyAiSettings.AsNoTracking()
            .Where(item => item.ConversationRetentionDays != null)
            .Select(item => new { item.CompanyId, Days = item.ConversationRetentionDays })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var total = 0;
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += await PurgeCompanyAsync(rule.CompanyId, rule.Days, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }
}

/// <summary>Apaga, de 6 em 6 horas, as conversas com a IA que passaram o prazo de guarda da sua empresa.</summary>
public sealed class AiRetentionJob(AiRetentionService retention) : IBackgroundJob
{
    public const string JobKey = "retencao-conversas-ia";

    public string Key => JobKey;

    public string Name => "Prazo de guarda das conversas com a IA";

    public string Schedule => "De 6 em 6 horas, nas empresas que definiram um prazo";

    public TimeSpan CheckEvery => TimeSpan.FromHours(6);

    public async Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await retention.PurgeAllAsync(cancellationToken).ConfigureAwait(false);
}
