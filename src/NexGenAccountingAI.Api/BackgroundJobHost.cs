using NexGenAccountingAI.Infrastructure.Persistence;

namespace NexGenAccountingAI.Api;

/// <summary>
/// Dispara as tarefas automáticas (F4-05) dentro do próprio processo da API, para o
/// serviço do Windows continuar a ser um só. De minuto a minuto vê que tarefas estão na
/// hora de verificar e corre cada uma no seu âmbito de serviços. Uma tarefa que falha
/// fica no log e não afeta as outras nem a API. Desliga-se com
/// <c>BackgroundJobs:Enabled = false</c> (os testes desligam).
/// </summary>
public sealed class BackgroundJobHost(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<BackgroundJobHost> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            logger.LogInformation("Background jobs are disabled by configuration.");
            return;
        }

        var nextCheck = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        try
        {
            // Dá tempo ao arranque (migrações, dados de demonstração) antes da primeira volta.
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunDueJobsAsync(nextCheck, stoppingToken).ConfigureAwait(false);
                await Task.Delay(Tick, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Paragem normal do serviço.
        }
    }

    private async Task RunDueJobsAsync(Dictionary<string, DateTimeOffset> nextCheck, CancellationToken stoppingToken)
    {
        List<(string Key, TimeSpan CheckEvery)> jobs;
        await using (var scope = scopes.CreateAsyncScope())
        {
            jobs = scope.ServiceProvider.GetServices<IBackgroundJob>().Select(job => (job.Key, job.CheckEvery)).ToList();
        }

        foreach (var (key, checkEvery) in jobs)
        {
            var now = DateTimeOffset.UtcNow;
            if (nextCheck.TryGetValue(key, out var due) && now < due)
            {
                continue;
            }

            nextCheck[key] = now + checkEvery;
            try
            {
                // Um âmbito por execução: a tarefa tem o seu próprio contexto da base de dados.
                await using var scope = scopes.CreateAsyncScope();
                var job = scope.ServiceProvider.GetServices<IBackgroundJob>().Single(item => item.Key == key);
                await job.RunAsync(now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job {JobKey} failed.", key);
            }
        }
    }
}
