using System.Text;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Avisa por email quem recebe uma tarefa atribuída (F4E-01). Envia com a conta de email de quem atribuiu, se a
/// configurou; nunca falha a gravação da tarefa — sem conta, sem destinatário ou com o servidor em baixo, não envia.
/// </summary>
public sealed class TaskNotifier(AccountingDbContext context, MailAccountService mail)
{
    /// <summary>O responsável atual de uma tarefa, para saber se a alteração o mudou.</summary>
    public Task<Guid?> AssigneeOfAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        context.FirmTasks.AsNoTracking().Where(task => task.Id == taskId).Select(task => task.AssigneeUserId).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Avisa o responsável pela tarefa e quem a criou (menos o autor) de um comentário novo. Nunca falha.</summary>
    public async Task<int> NotifyCommentedAsync(Guid authorId, Guid taskId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var task = await context.FirmTasks.AsNoTracking()
                .Where(item => item.Id == taskId)
                .Select(item => new { item.Title, item.AssigneeUserId, item.CreatedByUserId, Company = context.Companies.Where(company => company.Id == item.CompanyId).Select(company => company.LegalName).FirstOrDefault() })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var hasAccount = await context.MailAccounts.AsNoTracking()
                .AnyAsync(item => item.Purpose == MailAccountPurpose.UserSending && item.OwnerId == authorId && item.Secret != null, cancellationToken)
                .ConfigureAwait(false);
            if (task is null || !hasAccount)
            {
                return 0;
            }

            var ids = new[] { task.AssigneeUserId, task.CreatedByUserId }.Where(id => id is { } value && value != authorId).Select(id => id!.Value).Distinct().ToList();
            var recipients = await context.UserAccounts.AsNoTracking().Where(user => ids.Contains(user.Id) && user.IsActive).Select(user => user.Email).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (recipients.Count == 0)
            {
                return 0;
            }

            var author = await context.UserAccounts.AsNoTracking().Where(user => user.Id == authorId).Select(user => user.DisplayName).SingleAsync(cancellationToken).ConfigureAwait(false);
            await mail.SendAsync(authorId, recipients, $"Comentário na tarefa: {task.Title}", $"{author} comentou a tarefa «{task.Title}» ({task.Company}):{Environment.NewLine}{Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}Pode responder em Gestão → Tarefas do gabinete.", null, cancellationToken).ConfigureAwait(false);
            return recipients.Count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return 0;
        }
    }

    /// <returns><c>true</c> se o email foi enviado.</returns>
    public async Task<bool> NotifyAssignedAsync(Guid actorId, FirmTaskView task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.AssigneeUserId is not { } assignee || assignee == actorId)
        {
            return false;
        }

        try
        {
            var hasAccount = await context.MailAccounts.AsNoTracking()
                .AnyAsync(item => item.Purpose == MailAccountPurpose.UserSending && item.OwnerId == actorId && item.Secret != null, cancellationToken)
                .ConfigureAwait(false);
            var recipient = await context.UserAccounts.AsNoTracking()
                .Where(item => item.Id == assignee && item.IsActive)
                .Select(item => new { item.Email, item.DisplayName })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!hasAccount || recipient is null)
            {
                return false;
            }

            var actor = await context.UserAccounts.AsNoTracking().Where(item => item.Id == actorId).Select(item => item.DisplayName).SingleAsync(cancellationToken).ConfigureAwait(false);
            var priority = task.Priority switch { "high" => "alta", "low" => "baixa", _ => "normal" };
            var body = new StringBuilder()
                .AppendLine($"Olá, {recipient.DisplayName}.").AppendLine()
                .AppendLine($"{actor} atribuiu-lhe uma tarefa em {task.CompanyName}:").AppendLine()
                .AppendLine($"  {task.Title}");
            if (!string.IsNullOrWhiteSpace(task.Description))
            {
                body.AppendLine().AppendLine(task.Description);
            }

            body.AppendLine().AppendLine($"Prazo: {(task.DueDate is { } due ? due.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : "sem prazo")} · Prioridade: {priority}")
                .AppendLine().AppendLine("Pode vê-la em Gestão → Tarefas do gabinete.");
            await mail.SendAsync(actorId, [recipient.Email], $"Tarefa atribuída: {task.Title}", body.ToString(), null, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
