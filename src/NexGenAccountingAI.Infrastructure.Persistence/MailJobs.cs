using System.Text;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Domain.Operations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Lê as caixas de correio dos gabinetes que as ligaram (F4E-03) de poucos em poucos minutos e arquiva os
/// documentos recebidos. Uma caixa que falha regista o erro na própria conta e não impede as outras.
/// </summary>
public sealed class InboxPollJob(AccountingDbContext context, MailInboxService inbox) : IBackgroundJob
{
    public const string JobKey = "caixa-de-entrada";

    public string Key => JobKey;

    public string Name => "Documentos recebidos por email";

    public string Schedule => "De 5 em 5 minutos, nas caixas de correio ligadas";

    public TimeSpan CheckEvery => TimeSpan.FromMinutes(5);

    public async Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var firms = await context.MailAccounts
            .AsNoTracking()
            .Where(item => item.Purpose == MailAccountPurpose.FirmInbox && item.PollingEnabled && item.Secret != null)
            .Select(item => item.OwnerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var firmId in firms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await inbox.PollAsync(firmId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // O erro já ficou na conta (LastError), onde o administrador o vê.
                context.ChangeTracker.Clear();
            }
        }
    }
}

/// <summary>
/// Envia a cada utilizador que o pediu, por volta das 8 horas nos dias úteis, um resumo por email com as
/// obrigações em atraso ou a vencer nos próximos 7 dias das empresas de que é responsável, e as tarefas que
/// lhe estão atribuídas. Usa a conta de email do próprio utilizador e não envia nada quando não há nada a dizer.
/// </summary>
public sealed class DailyDigestJob(
    AccountingDbContext context,
    MailAccountService mail,
    ObligationConsoleService console,
    FirmTaskService tasks,
    BackgroundJobLog log) : IBackgroundJob
{
    public const string JobKey = "resumo-diario-email";

    private const int DueSoonDays = 7;

    public string Key => JobKey;

    public string Name => "Resumo diário por email";

    public string Schedule => "Dias úteis, a partir das 8 horas, para quem ligou o resumo na sua conta de email";

    public TimeSpan CheckEvery => TimeSpan.FromMinutes(30);

    public async Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var local = now.ToLocalTime();
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local.Hour < 8)
        {
            return;
        }

        var users = await context.MailAccounts
            .AsNoTracking()
            .Where(item => item.Purpose == MailAccountPurpose.UserSending && item.DailyDigest && item.Secret != null)
            .Select(item => item.OwnerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var period = DateOnly.FromDateTime(local.DateTime).ToString("yyyy-MM-dd");
        foreach (var userId in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A unidade de trabalho é o utilizador no dia: o registo impede de enviar o resumo duas vezes.
            var run = await log.TryBeginAsync(JobKey, userId, period, now, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                continue;
            }

            try
            {
                var message = await BuildAsync(userId, DateOnly.FromDateTime(local.DateTime), cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    await log.FinishAsync(run, BackgroundJobStatus.Skipped, DateTimeOffset.UtcNow, "Nada a avisar.", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var address = await context.MailAccounts.AsNoTracking()
                    .Where(item => item.Purpose == MailAccountPurpose.UserSending && item.OwnerId == userId)
                    .Select(item => item.Address)
                    .SingleAsync(cancellationToken)
                    .ConfigureAwait(false);
                await mail.SendAsync(userId, [address], message.Value.Subject, message.Value.Body, null, cancellationToken).ConfigureAwait(false);
                await log.FinishAsync(run, BackgroundJobStatus.Succeeded, DateTimeOffset.UtcNow, "Resumo enviado.", cancellationToken).ConfigureAwait(false);
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

    /// <summary>O texto do resumo, ou nulo quando não há obrigações a vencer nem tarefas por fazer.</summary>
    public async Task<(string Subject, string Body)?> BuildAsync(Guid userId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var name = await context.UserAccounts.AsNoTracking().Where(item => item.Id == userId).Select(item => item.DisplayName).SingleAsync(cancellationToken).ConfigureAwait(false);
        var calendar = await console.BuildAsync(userId, today.AddDays(-90), today.AddDays(DueSoonDays), userId, cancellationToken).ConfigureAwait(false);
        var pending = calendar.Rows.Where(row => row.Obligation.State == "Pendente").ToList();
        var overdue = pending.Where(row => row.Obligation.DueDate < today).ToList();
        var soon = pending.Where(row => row.Obligation.DueDate >= today).ToList();
        var open = await tasks.ListAsync(userId, new FirmTaskFilter(Assignee: "me", Status: "open", Take: 50), cancellationToken).ConfigureAwait(false);

        if (overdue.Count == 0 && soon.Count == 0 && open.Count == 0)
        {
            return null;
        }

        static string Line(ObligationConsoleRow row) => $" - {row.Obligation.DueDate:dd/MM/yyyy} · {row.CompanyName} · {row.Obligation.Name} ({row.Obligation.PeriodLabel})";
        var text = new StringBuilder();
        text.AppendLine($"Bom dia, {name}.").AppendLine();
        if (overdue.Count > 0)
        {
            text.AppendLine($"Obrigações em atraso ({overdue.Count}):");
            overdue.Take(30).ToList().ForEach(row => text.AppendLine(Line(row)));
            text.AppendLine();
        }

        if (soon.Count > 0)
        {
            text.AppendLine($"A vencer nos próximos {DueSoonDays} dias ({soon.Count}):");
            soon.Take(30).ToList().ForEach(row => text.AppendLine(Line(row)));
            text.AppendLine();
        }

        if (open.Count > 0)
        {
            text.AppendLine($"Tarefas atribuídas a si por fazer ({open.Count}):");
            foreach (var task in open.Take(30))
            {
                text.AppendLine($" - {task.Title} · {task.CompanyName}{(task.DueDate is { } due ? $" · prazo {due:dd/MM/yyyy}" : string.Empty)}{(task.Overdue ? " (em atraso)" : string.Empty)}");
            }

            text.AppendLine();
        }

        text.AppendLine("Mensagem automática do NexGen Accounting AI. Pode desligar este resumo em Definições → A minha conta de email.");
        var subject = overdue.Count > 0
            ? $"{overdue.Count} {(overdue.Count == 1 ? "obrigação em atraso" : "obrigações em atraso")} — resumo de {today:dd/MM/yyyy}"
            : $"Resumo de {today:dd/MM/yyyy}: prazos e tarefas";
        return (subject, text.ToString());
    }
}
