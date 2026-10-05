using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Uma tarefa automática (F4-05). O anfitrião chama <see cref="RunAsync"/> de
/// <see cref="CheckEvery"/> em <see cref="CheckEvery"/>; é a tarefa que decide se há
/// trabalho e regista cada unidade no <see cref="BackgroundJobLog"/>, que impede
/// repetições. Uma tarefa nunca deve deixar escapar exceções de uma empresa para as outras.
/// </summary>
public interface IBackgroundJob
{
    string Key { get; }

    /// <summary>Nome em português, para o ecrã.</summary>
    string Name { get; }

    /// <summary>Quando corre, em português, para o ecrã.</summary>
    string Schedule { get; }

    TimeSpan CheckEvery { get; }

    Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record BackgroundJobRunRow(
    string JobKey, Guid? CompanyId, string? CompanyName, string PeriodKey, string Status, int Attempts,
    DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, string? Message);

/// <summary>
/// O registo das tarefas automáticas: reserva uma unidade de trabalho (tarefa, empresa,
/// período) antes de a executar e guarda o resultado. A reserva é o que garante que a
/// mesma unidade não corre duas vezes, mesmo com dois processos a tentar ao mesmo tempo
/// (índice único e controlo de concorrência da linha).
/// </summary>
public sealed class BackgroundJobLog(AccountingDbContext context)
{
    public const int MaxAttempts = 3;

    /// <summary>Espera mínima antes de voltar a tentar uma unidade que falhou.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>Uma unidade «a correr» há mais tempo do que isto ficou a meio (o processo parou) e pode ser retomada.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    /// <returns>A unidade reservada, ou <c>null</c> se já foi feita, está a correr ou esgotou as tentativas.</returns>
    public async Task<BackgroundJobRun?> TryBeginAsync(
        string jobKey, Guid companyId, string periodKey, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        var run = await context.BackgroundJobRuns
            .SingleOrDefaultAsync(item => item.JobKey == jobKey && item.CompanyId == companyId && item.PeriodKey == periodKey, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            run = new BackgroundJobRun(Guid.NewGuid(), jobKey, companyId, periodKey, now);
            context.BackgroundJobRuns.Add(run);
        }
        else
        {
            var retake = run.Status switch
            {
                BackgroundJobStatus.Running => now - run.StartedAtUtc > StaleAfter,
                BackgroundJobStatus.Failed => run.Attempts < MaxAttempts && now - (run.FinishedAtUtc ?? run.StartedAtUtc) >= RetryAfter,
                _ => false,
            };
            if (!retake)
            {
                context.ChangeTracker.Clear();
                return null;
            }

            run.Restart(now);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return run;
        }
        catch (DbUpdateException)
        {
            // Outro processo reservou a mesma unidade primeiro.
            context.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task FinishAsync(
        BackgroundJobRun run, BackgroundJobStatus status, DateTimeOffset now, string? message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        // A tarefa pode ter deixado o contexto num estado qualquer (até com erro): grava-se só o resultado.
        context.ChangeTracker.Clear();
        var stored = await context.BackgroundJobRuns.SingleAsync(item => item.Id == run.Id, cancellationToken).ConfigureAwait(false);
        stored.Finish(status, now, message);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
    }

    /// <summary>As últimas execuções que o utilizador pode ver: as das suas empresas e as gerais.</summary>
    public async Task<IReadOnlyList<BackgroundJobRunRow>> RecentAsync(Guid userId, int take, CancellationToken cancellationToken = default)
    {
        var companies = await context.Companies
            .AsNoTracking()
            .Where(company => company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .Select(company => new { company.Id, company.LegalName })
            .ToDictionaryAsync(company => company.Id, company => company.LegalName, cancellationToken)
            .ConfigureAwait(false);
        var ids = companies.Keys.Append(Guid.Empty).ToList();

        var runs = await context.BackgroundJobRuns
            .AsNoTracking()
            .Where(run => ids.Contains(run.CompanyId))
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return runs.Select(run => new BackgroundJobRunRow(
            run.JobKey,
            run.CompanyId == Guid.Empty ? null : run.CompanyId,
            companies.GetValueOrDefault(run.CompanyId),
            run.PeriodKey,
            run.Status.ToString(),
            run.Attempts,
            run.StartedAtUtc,
            run.FinishedAtUtc,
            run.Message)).ToList();
    }
}

/// <summary>
/// Gera a síntese mensal com IA do mês anterior, nas empresas que ligaram a geração
/// automática. Espera pelo dia 10, para o mês anterior já estar lançado; um mês sem
/// movimentos não gera relatório. O relatório fica em nome do dono (ou de um
/// administrador) da empresa.
/// </summary>
public sealed class MonthlyAiReportJob(
    AccountingDbContext context,
    ManagementReportService reports,
    IManagementReportNarrator narrator,
    BackgroundJobLog log) : IBackgroundJob
{
    public const string JobKey = "sintese-mensal-ia";

    /// <summary>Primeiro dia do mês em que a síntese do mês anterior é gerada.</summary>
    public const int FirstDay = 10;

    public string Key => JobKey;

    public string Name => "Síntese mensal com IA";

    public string Schedule => $"A partir do dia {FirstDay} de cada mês, para o mês anterior, nas empresas com a geração automática ligada";

    public TimeSpan CheckEvery => TimeSpan.FromHours(1);

    public async Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        if (today.Day < FirstDay || !narrator.IsConfigured)
        {
            return;
        }

        var to = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var from = new DateOnly(to.Year, to.Month, 1);
        var period = $"{from:yyyy-MM}";

        var companies = await context.CompanyAiSettings
            .AsNoTracking()
            .Where(settings => settings.Enabled && settings.AutoMonthly)
            .Join(context.Companies.Where(company => company.IsActive), settings => settings.CompanyId, company => company.Id, (_, company) => company.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var companyId in companies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await log.TryBeginAsync(JobKey, companyId, period, now, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                continue;
            }

            try
            {
                var (status, message) = await GenerateAsync(companyId, from, to, cancellationToken).ConfigureAwait(false);
                await log.FinishAsync(run, status, DateTimeOffset.UtcNow, message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Uma empresa que falha não impede as outras; fica registado e volta a tentar mais tarde.
                await log.FinishAsync(run, BackgroundJobStatus.Failed, DateTimeOffset.UtcNow, exception.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<(BackgroundJobStatus Status, string Message)> GenerateAsync(
        Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var hasMovements = await context.JournalEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.CompanyId == companyId && entry.Status != JournalEntryStatus.Voided &&
                entry.EntryDate >= from && entry.EntryDate <= to, cancellationToken)
            .ConfigureAwait(false);
        if (!hasMovements)
        {
            return (BackgroundJobStatus.Skipped, "Sem movimentos no mês.");
        }

        var owner = await context.CompanyUsers
            .AsNoTracking()
            .Where(membership => membership.CompanyId == companyId && membership.IsActive && membership.User.IsActive &&
                (membership.Role == CompanyUserRole.Owner || membership.Role == CompanyUserRole.Administrator))
            .OrderBy(membership => membership.Role)
            .Select(membership => (Guid?)membership.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (owner is not { } userId)
        {
            return (BackgroundJobStatus.Failed, "A empresa não tem dono nem administrador ativo em nome de quem gerar o relatório.");
        }

        var generation = await reports
            .GenerateAsync(userId, companyId, new ManagementReportRequest(ManagementReportKind.SinteseMensal, null, from, to), cancellationToken)
            .ConfigureAwait(false);
        return generation.View is null
            ? (BackgroundJobStatus.Failed, generation.DisabledReason ?? "O relatório não foi gerado.")
            : (BackgroundJobStatus.Succeeded, $"Relatório gerado ({generation.View.Model}).");
    }
}

/// <summary>
/// Atualiza todos os dias as estatísticas da base de dados. Sem estatísticas atualizadas
/// o PostgreSQL escolhe planos de consulta maus e os mapas ficam lentos (aconteceu depois
/// de uma importação grande).
/// </summary>
public sealed class DatabaseStatisticsJob(AccountingDbContext context, BackgroundJobLog log) : IBackgroundJob
{
    public const string JobKey = "estatisticas-base-dados";

    public string Key => JobKey;

    public string Name => "Manutenção da base de dados";

    public string Schedule => "Todos os dias, depois das 3 da manhã";

    public TimeSpan CheckEvery => TimeSpan.FromMinutes(30);

    public async Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var local = now.ToLocalTime();
        if (local.Hour < 3)
        {
            return;
        }

        var run = await log.TryBeginAsync(JobKey, Guid.Empty, $"{local:yyyy-MM-dd}", now, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return;
        }

        try
        {
            await context.RefreshAfterImportAsync(cancellationToken).ConfigureAwait(false);
            await log.FinishAsync(run, BackgroundJobStatus.Succeeded, DateTimeOffset.UtcNow, "Estatísticas atualizadas.", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await log.FinishAsync(run, BackgroundJobStatus.Failed, DateTimeOffset.UtcNow, exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }
}
