using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record FirmTaskCommentView(Guid Id, Guid UserId, string AuthorName, string Text, DateTimeOffset CreatedAtUtc, bool Mine);

/// <summary>
/// Os comentários das tarefas do gabinete (F4E-01). Qualquer membro da empresa lê; quem comenta é avisado
/// por email o responsável pela tarefa e quem a criou (se não forem o autor), com a conta de email do autor.
/// </summary>
public sealed class FirmTaskCommentService(AccountingDbContext context, TaskNotifier notifier)
{
    /// <summary>Os comentários da tarefa, do mais antigo para o mais novo; <c>null</c> se a tarefa não existe na empresa ou não há acesso.</summary>
    public async Task<IReadOnlyList<FirmTaskCommentView>?> ListAsync(Guid userId, Guid companyId, Guid taskId, CancellationToken cancellationToken = default)
    {
        if (!await TaskVisibleAsync(userId, companyId, taskId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await context.FirmTaskComments.AsNoTracking()
            .Where(item => item.TaskId == taskId)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var names = await context.UserAccounts.AsNoTracking()
            .Where(user => rows.Select(row => row.UserId).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => new FirmTaskCommentView(row.Id, row.UserId, names.GetValueOrDefault(row.UserId, "Utilizador removido"), row.Text, row.CreatedAtUtc, row.UserId == userId)).ToList();
    }

    public async Task<FirmTaskCommentView?> AddAsync(Guid userId, Guid companyId, Guid taskId, string text, CancellationToken cancellationToken = default)
    {
        if (!await TaskVisibleAsync(userId, companyId, taskId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var comment = new FirmTaskComment(Guid.NewGuid(), taskId, companyId, userId, text);
        context.FirmTaskComments.Add(comment);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await notifier.NotifyCommentedAsync(userId, taskId, comment.Text, cancellationToken).ConfigureAwait(false);
        var author = await context.UserAccounts.AsNoTracking().Where(user => user.Id == userId).Select(user => user.DisplayName).SingleAsync(cancellationToken).ConfigureAwait(false);
        return new FirmTaskCommentView(comment.Id, userId, author, comment.Text, comment.CreatedAtUtc, true);
    }

    private Task<bool> TaskVisibleAsync(Guid userId, Guid companyId, Guid taskId, CancellationToken cancellationToken) =>
        context.FirmTasks.AsNoTracking().AnyAsync(
            task => task.Id == taskId && task.CompanyId == companyId &&
                context.CompanyUsers.Any(membership => membership.CompanyId == companyId && membership.UserId == userId && membership.IsActive),
            cancellationToken);
}
