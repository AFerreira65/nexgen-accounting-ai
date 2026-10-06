using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma tarefa do gabinete, com os nomes já resolvidos.</summary>
/// <param name="Priority"><c>low</c>, <c>normal</c> ou <c>high</c>.</param>
/// <param name="Status"><c>open</c>, <c>inprogress</c>, <c>done</c> ou <c>cancelled</c>.</param>
/// <param name="Mine">Está atribuída ao utilizador que pediu a lista.</param>
public sealed record FirmTaskView(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Title,
    string? Description,
    DateOnly? DueDate,
    string Priority,
    string Status,
    Guid? AssigneeUserId,
    string? AssigneeName,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? CompletedByName,
    string? Origin,
    bool Overdue,
    bool Mine);

/// <param name="Assignee"><c>me</c> (as minhas), <c>none</c> (sem responsável) ou o identificador de um utilizador.</param>
/// <param name="Status"><c>open</c> (por fazer ou em curso), <c>closed</c> (concluídas ou canceladas) ou <c>all</c>.</param>
public sealed record FirmTaskFilter(Guid? CompanyId = null, string? Assignee = null, string? Status = "open", string? Search = null, int Take = 300, Guid? TaskId = null);

public sealed record FirmTaskInput(string? Title, string? Description, DateOnly? DueDate, string? Priority, Guid? AssigneeUserId, string? Status);

public sealed record FirmTaskAssignee(Guid UserId, string DisplayName, bool IsResponsible);

/// <summary>Resultado de uma escrita: a tarefa (se passou), os erros de validação, ou 404.</summary>
public sealed record FirmTaskWriteResult(FirmTaskView? Saved, IReadOnlyList<string> Errors, bool NotFound)
{
    public static FirmTaskWriteResult Missing => new(null, [], NotFound: true);

    public static FirmTaskWriteResult Invalid(IReadOnlyList<string> errors) => new(null, errors, NotFound: false);

    public static FirmTaskWriteResult Ok(FirmTaskView view) => new(view, [], NotFound: false);
}

/// <summary>O que a geração de tarefas a partir das obrigações fez.</summary>
public sealed record FirmTaskGeneration(int Created, int AlreadyExisted);

/// <summary>
/// As tarefas atribuíveis do gabinete (F4E-01). Uma tarefa é sempre de uma empresa: vê-a quem tem
/// acesso a essa empresa; cria, altera e conclui quem não é só de consulta; o responsável tem de
/// ser membro da empresa. Podem nascer das obrigações em atraso ou a vencer, sem duplicar.
/// </summary>
public sealed class FirmTaskService(AccountingDbContext context, ObligationCalendarService obligations, TimeProvider time)
{
    public const int DueSoonDays = 15;

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>As tarefas das empresas a que o utilizador tem acesso, filtradas.</summary>
    public async Task<IReadOnlyList<FirmTaskView>> ListAsync(Guid userId, FirmTaskFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var companies = await context.Companies
            .AsNoTracking()
            .Where(company => company.IsActive && company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .ToDictionaryAsync(company => company.Id, company => company.LegalName, cancellationToken)
            .ConfigureAwait(false);
        var ids = companies.Keys.Where(id => filter.CompanyId is null || id == filter.CompanyId).ToList();

        var query = context.FirmTasks.AsNoTracking().Where(task => ids.Contains(task.CompanyId));
        if (filter.TaskId is { } taskId)
        {
            query = query.Where(task => task.Id == taskId);
        }

        query = (filter.Status ?? "open").ToLowerInvariant() switch
        {
            "closed" => query.Where(task => task.Status == FirmTaskStatus.Done || task.Status == FirmTaskStatus.Cancelled),
            "all" => query,
            _ => query.Where(task => task.Status == FirmTaskStatus.Open || task.Status == FirmTaskStatus.InProgress),
        };
        var assignee = (filter.Assignee ?? string.Empty).Trim().ToLowerInvariant();
        if (assignee == "me")
        {
            query = query.Where(task => task.AssigneeUserId == userId);
        }
        else if (assignee == "none")
        {
            query = query.Where(task => task.AssigneeUserId == null);
        }
        else if (Guid.TryParse(assignee, out var assigneeId))
        {
            query = query.Where(task => task.AssigneeUserId == assigneeId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + filter.Search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(task => EF.Functions.ILike(task.Title, pattern, "\\") || (task.Description != null && EF.Functions.ILike(task.Description, pattern, "\\")));
        }

        var rows = await query
            .Select(task => new { Task = task, CreatedAtUtc = EF.Property<DateTimeOffset>(task, "CreatedAtUtc") })
            .OrderBy(item => item.Task.Status == FirmTaskStatus.Done || item.Task.Status == FirmTaskStatus.Cancelled ? 1 : 0)
            .ThenBy(item => item.Task.DueDate == null ? 1 : 0)
            .ThenBy(item => item.Task.DueDate)
            .ThenByDescending(item => item.Task.Priority)
            .ThenBy(item => item.CreatedAtUtc)
            .Take(Math.Clamp(filter.Take, 1, 1000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var names = await NamesAsync(rows.SelectMany(item => new[] { item.Task.AssigneeUserId, item.Task.CreatedByUserId, item.Task.CompletedByUserId }), cancellationToken).ConfigureAwait(false);
        return rows.Select(item => View(item.Task, item.CreatedAtUtc, companies, names, userId)).ToList();
    }

    /// <summary>Os membros a quem se pode atribuir uma tarefa da empresa. <c>null</c> sem acesso.</summary>
    public async Task<IReadOnlyList<FirmTaskAssignee>?> AssigneesAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (await RoleAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return await context.CompanyUsers
            .AsNoTracking()
            .Where(membership => membership.CompanyId == companyId && membership.IsActive)
            .OrderBy(membership => membership.User.DisplayName)
            .Select(membership => new FirmTaskAssignee(membership.UserId, membership.User.DisplayName, membership.IsResponsible))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FirmTaskWriteResult> CreateAsync(Guid userId, Guid companyId, FirmTaskInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanWriteAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return FirmTaskWriteResult.Missing;
        }

        var errors = await ValidateAsync(companyId, input, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return FirmTaskWriteResult.Invalid(errors);
        }

        var task = FirmTask.Create(companyId, userId, input.Title!, input.Description, input.DueDate, ParsePriority(input.Priority), input.AssigneeUserId);
        context.FirmTasks.Add(task);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(userId, task.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirmTaskWriteResult> UpdateAsync(Guid userId, Guid companyId, Guid id, FirmTaskInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanWriteAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return FirmTaskWriteResult.Missing;
        }

        var task = await context.FirmTasks.FirstOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return FirmTaskWriteResult.Missing;
        }

        var errors = await ValidateAsync(companyId, input, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return FirmTaskWriteResult.Invalid(errors);
        }

        task.Edit(input.Title!, input.Description, input.DueDate, ParsePriority(input.Priority), input.AssigneeUserId);
        if (input.Status is not null && ParseStatus(input.Status) is { } status && status != task.Status)
        {
            task.SetStatus(status, userId, time.GetUtcNow());
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(userId, task.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Apaga: o autor, ou quem é dono ou administrador da empresa.</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var role = await RoleAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (role is null or CompanyUserRole.Viewer)
        {
            return false;
        }

        var task = await context.FirmTasks.FirstOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (task is null || (task.CreatedByUserId != userId && role is not (CompanyUserRole.Owner or CompanyUserRole.Administrator)))
        {
            return false;
        }

        context.FirmTasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Cria uma tarefa por cada obrigação pendente em atraso ou a vencer nos próximos
    /// <see cref="DueSoonDays"/> dias, entregue ao responsável da empresa, se houver. Não repete as que já existem.
    /// </summary>
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<FirmTaskGeneration?> GenerateFromObligationsAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await CanWriteAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var today = Today;
        var calendar = await obligations.CalendarAsync(userId, companyId, today.AddMonths(-3), today.AddDays(DueSoonDays), cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            return null;
        }

        var responsible = await context.CompanyUsers
            .AsNoTracking()
            .Where(membership => membership.CompanyId == companyId && membership.IsActive && membership.IsResponsible)
            .OrderBy(membership => membership.UserId)
            .Select(membership => (Guid?)membership.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var existing = (await context.FirmTasks
            .AsNoTracking()
            .Where(task => task.CompanyId == companyId && task.Origin != null)
            .Select(task => task.Origin!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        var created = 0;
        var skipped = 0;
        foreach (var row in calendar.Rows.Where(row => row.State == nameof(TaxObligationState.Pendente) && row.DueDate <= today.AddDays(DueSoonDays)))
        {
            var origin = $"obrigacao:{row.Code}:{row.PeriodKey}";
            if (!existing.Add(origin))
            {
                skipped++;
                continue;
            }

            var overdue = row.DueDate < today;
            context.FirmTasks.Add(FirmTask.Create(
                companyId, userId, $"Entregar {row.Name} ({row.PeriodLabel})",
                overdue ? $"Prazo de {row.DueDate:dd/MM/yyyy} ultrapassado." : $"Prazo: {row.DueDate:dd/MM/yyyy}.",
                row.DueDate, overdue ? FirmTaskPriority.High : FirmTaskPriority.Normal, responsible, origin));
            created++;
        }

        if (created > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FirmTaskGeneration(created, skipped);
    }

    private async Task<FirmTaskWriteResult> ReadAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var view = (await ListAsync(userId, new FirmTaskFilter(Status: "all", Take: 1, TaskId: id), cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return view is null ? FirmTaskWriteResult.Missing : FirmTaskWriteResult.Ok(view);
    }

    private async Task<List<string>> ValidateAsync(Guid companyId, FirmTaskInput input, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors.Add("Indique o título da tarefa.");
        }
        else if (input.Title.Trim().Length > FirmTask.TitleMaxLength)
        {
            errors.Add($"O título tem mais de {FirmTask.TitleMaxLength} caracteres.");
        }

        if (input.Description is { Length: > FirmTask.DescriptionMaxLength })
        {
            errors.Add($"A descrição tem mais de {FirmTask.DescriptionMaxLength} caracteres.");
        }

        if (input.Priority is not null && !Enum.TryParse<FirmTaskPriority>(input.Priority, ignoreCase: true, out _))
        {
            errors.Add("A prioridade tem de ser «low», «normal» ou «high».");
        }

        if (input.Status is not null && ParseStatus(input.Status) is null)
        {
            errors.Add("O estado tem de ser «open», «inprogress», «done» ou «cancelled».");
        }

        if (input.AssigneeUserId is { } assignee && assignee != Guid.Empty &&
            !await context.CompanyUsers.AnyAsync(membership => membership.CompanyId == companyId && membership.UserId == assignee && membership.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("O responsável tem de ser um membro da empresa.");
        }

        return errors;
    }

    private static FirmTaskPriority ParsePriority(string? text) =>
        Enum.TryParse<FirmTaskPriority>(text, ignoreCase: true, out var priority) ? priority : FirmTaskPriority.Normal;

    private static FirmTaskStatus? ParseStatus(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "open" => FirmTaskStatus.Open,
        "inprogress" => FirmTaskStatus.InProgress,
        "done" => FirmTaskStatus.Done,
        "cancelled" => FirmTaskStatus.Cancelled,
        _ => null,
    };

    private FirmTaskView View(
        FirmTask task, DateTimeOffset createdAtUtc, IReadOnlyDictionary<Guid, string> companies, IReadOnlyDictionary<Guid, string> names, Guid userId) => new(
        task.Id, task.CompanyId, companies.GetValueOrDefault(task.CompanyId, string.Empty), task.Title, task.Description, task.DueDate,
        task.Priority.ToString().ToLowerInvariant(), task.Status.ToString().ToLowerInvariant(),
        task.AssigneeUserId, task.AssigneeUserId is { } assignee ? names.GetValueOrDefault(assignee) : null,
        names.GetValueOrDefault(task.CreatedByUserId, string.Empty), createdAtUtc, task.CompletedAtUtc,
        task.CompletedByUserId is { } completer ? names.GetValueOrDefault(completer) : null,
        task.Origin, !task.IsClosed && task.DueDate is { } due && due < Today, task.AssigneeUserId == userId);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        return await context.UserAccounts
            .AsNoTracking()
            .Where(user => wanted.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CompanyUserRole?> RoleAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        await context.CompanyUsers
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.CompanyId == companyId && membership.IsActive && membership.Company.IsActive)
            .Select(membership => (CompanyUserRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<bool> CanWriteAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        await RoleAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is { } role && role != CompanyUserRole.Viewer;
}
