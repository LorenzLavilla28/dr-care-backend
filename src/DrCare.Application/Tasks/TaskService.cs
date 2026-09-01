using System.ComponentModel.DataAnnotations;
using DrCare.Application.Contracts;
using DrCare.Domain;

namespace DrCare.Application.Tasks;

public interface ITaskService
{
    Task<IReadOnlyList<TaskResponse>> ListAsync(Guid? leadId, CancellationToken cancellationToken);
    Task<TaskResponse> GetAsync(Guid taskId, CancellationToken cancellationToken);
    Task<TaskResponse> CreateAsync(CreateTaskRequest request, Guid? routeLeadId, CancellationToken cancellationToken);
    Task<TaskResponse> UpdateAsync(Guid taskId, CreateTaskRequest request, CancellationToken cancellationToken);
    Task<TaskResponse> CompleteAsync(Guid taskId, CompleteTaskRequest request, CancellationToken cancellationToken);
    Task<TaskResponse> SnoozeAsync(Guid taskId, DateTimeOffset dueAt, CancellationToken cancellationToken);
}

public sealed class TaskService(ITaskRepository tasks, ILeadRepository leads, ICurrentUser currentUser) : ITaskService
{
    public async Task<IReadOnlyList<TaskResponse>> ListAsync(Guid? leadId, CancellationToken cancellationToken)
    {
        EnsureReadRole();
        // The unscoped endpoint powers "My work queue" for every role. A lead-scoped
        // request is an oversight view, so elevated roles may see all tasks for that lead.
        Guid? assignedTo = !leadId.HasValue || currentUser.Role == UserRole.MarketingAgent
            ? currentUser.UserId
            : null;
        if (leadId.HasValue) await GetLeadAsync(leadId.Value, cancellationToken);
        return (await tasks.ListAsync(currentUser.OrganizationId, assignedTo, leadId, cancellationToken)).Select(ToResponse).ToArray();
    }

    public async Task<TaskResponse> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(taskId, cancellationToken);
        return ToResponse(task);
    }

    public async Task<TaskResponse> CreateAsync(CreateTaskRequest request, Guid? routeLeadId, CancellationToken cancellationToken)
    {
        EnsureWriteRole();
        var leadId = routeLeadId ?? request.LeadId;
        if (!leadId.HasValue) throw new ValidationException("LeadId is required when creating a task.");
        var lead = await GetLeadAsync(leadId.Value, cancellationToken);
        var assignee = request.AssignedTo ?? lead.AssignedAgentId;
        var title = request.Title.Trim();
        if (await tasks.FindOpenByLeadAndTitleAsync(currentUser.OrganizationId, lead.Id, title, cancellationToken) is not null)
            throw new ConflictException("An open task for this workflow step already exists for the opportunity.");
        var task = new FollowUpTask(currentUser.OrganizationId, lead.Id, assignee, title, request.DueAt);
        await tasks.AddAsync(task, cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
        return ToResponse(task);
    }

    public async Task<TaskResponse> UpdateAsync(Guid taskId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        EnsureWriteRole();
        var task = await GetTaskAsync(taskId, cancellationToken);
        task.Update(request.Title, request.AssignedTo, request.DueAt);
        await tasks.SaveChangesAsync(cancellationToken);
        return ToResponse(task);
    }

    public async Task<TaskResponse> CompleteAsync(Guid taskId, CompleteTaskRequest request, CancellationToken cancellationToken)
    {
        EnsureWriteRole();
        var task = await GetTaskAsync(taskId, cancellationToken);
        task.Complete();
        await tasks.SaveChangesAsync(cancellationToken);
        return ToResponse(task);
    }

    public async Task<TaskResponse> SnoozeAsync(Guid taskId, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        EnsureWriteRole();
        if (dueAt <= DateTimeOffset.UtcNow) throw new ValidationException("Snooze time must be in the future.");
        var task = await GetTaskAsync(taskId, cancellationToken);
        task.Snooze(dueAt);
        await tasks.SaveChangesAsync(cancellationToken);
        return ToResponse(task);
    }

    private async Task<FollowUpTask> GetTaskAsync(Guid taskId, CancellationToken ct)
    {
        var task = await tasks.GetAsync(currentUser.OrganizationId, taskId, ct) ?? throw new NotFoundException("Task not found.");
        if (currentUser.Role == UserRole.MarketingAgent && task.AssignedTo != currentUser.UserId)
            throw new ForbiddenException("You do not have access to this task.");
        return task;
    }

    private async Task<Lead> GetLeadAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Lead not found.");
        if (currentUser.Role == UserRole.MarketingAgent && lead.AssignedAgentId != currentUser.UserId)
            throw new ForbiddenException("You do not have access to this lead.");
        return lead;
    }

    private void EnsureReadRole()
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership))
            throw new ForbiddenException("This role cannot access tasks.");
    }

    private void EnsureWriteRole()
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin))
            throw new ForbiddenException("Only marketing users can manage tasks.");
    }

    private static TaskResponse ToResponse(FollowUpTask task) => new(task.Id, task.LeadId, task.AssignedTo, task.Title, task.Status, task.CreatedAt, task.DueAt);
}
