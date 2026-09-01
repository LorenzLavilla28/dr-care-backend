using System.ComponentModel.DataAnnotations;
using DrCare.Application.Contracts;
using DrCare.Domain;

namespace DrCare.Application.Tasks;

public interface ITaskService
{
    Task<IReadOnlyList<TaskResponse>> ListAsync(Guid? leadId, CancellationToken cancellationToken);
    Task<PagedResponse<CompletedWorkItemResponse>> ListCompletedWorkAsync(int page, CancellationToken cancellationToken);
    Task<TaskResponse> GetAsync(Guid taskId, CancellationToken cancellationToken);
    Task<TaskResponse> CreateAsync(CreateTaskRequest request, Guid? routeLeadId, CancellationToken cancellationToken);
    Task<TaskResponse> UpdateAsync(Guid taskId, CreateTaskRequest request, CancellationToken cancellationToken);
    Task<TaskResponse> CompleteAsync(Guid taskId, CompleteTaskRequest request, CancellationToken cancellationToken);
    Task<TaskResponse> SnoozeAsync(Guid taskId, DateTimeOffset dueAt, CancellationToken cancellationToken);
}

public sealed class TaskService(ITaskRepository tasks, ILeadRepository leads, IReportingRepository reports, ICurrentUser currentUser) : ITaskService
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

    public async Task<PagedResponse<CompletedWorkItemResponse>> ListCompletedWorkAsync(int page, CancellationToken cancellationToken)
    {
        EnsureReadRole();
        page = Math.Clamp(page, 1, 10_000);
        const int pageSize = 10;
        var skip = (page - 1) * pageSize;
        // Fetch a window from each source, then merge by completion time. Fetching
        // skip + pageSize from both sources keeps the ordering correct without
        // loading the complete history into the API process.
        var windowSize = skip + pageSize;
        var completedTasks = await tasks.ListCompletedPageAsync(currentUser.OrganizationId, currentUser.UserId, 0, windowSize, cancellationToken);
        var visibleLeads = await reports.ListLeadsAsync(
            currentUser.OrganizationId,
            currentUser.Role == UserRole.MarketingAgent ? currentUser.UserId : null,
            null,
            null,
            cancellationToken);
        var leadsById = visibleLeads.ToDictionary(x => x.Id);
        var items = new List<CompletedWorkItemResponse>(completedTasks.Items.Count + 32);
        foreach (var task in completedTasks.Items)
        {
            if (!leadsById.ContainsKey(task.LeadId)) continue;
            items.Add(new CompletedWorkItemResponse(
                task.Id,
                task.LeadId,
                task.Title,
                "Task completed from your work queue.",
                "Task",
                task.CreatedAt));
        }

        var activities = await reports.ListActivitiesForActorPageAsync(currentUser.OrganizationId, currentUser.UserId, CompletedActivityTypes, 0, windowSize, cancellationToken);
        foreach (var activity in activities.Items)
        {
            if (!CompletedActivityTypes.Contains(activity.Type) || !leadsById.ContainsKey(activity.LeadId)) continue;
            items.Add(new CompletedWorkItemResponse(
                activity.Id,
                activity.LeadId,
                CompletedTitle(activity.Type),
                activity.Message,
                "Workflow",
                activity.CreatedAt));
        }

        var ordered = items.OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.Id).ToArray();
        var total = completedTasks.Total + activities.Total;
        return new PagedResponse<CompletedWorkItemResponse>(ordered.Skip(skip).Take(pageSize).ToArray(), page, pageSize, total);
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

    private static readonly IReadOnlySet<ActivityType> CompletedActivityTypes = new HashSet<ActivityType>
    {
        ActivityType.StateChanged,
        ActivityType.InquiryUpdated,
        ActivityType.NurturingUpdated,
        ActivityType.CallOutcomeRecorded,
        ActivityType.DocumentUploaded,
        ActivityType.InvoiceGenerated,
        ActivityType.DownPaymentSubmittedForFinance,
        ActivityType.PaymentConfirmed,
        ActivityType.ContractGenerated,
        ActivityType.ContractSubmitted,
        ActivityType.ContractApproved,
        ActivityType.ContractRevisionRequested,
        ActivityType.ContractSigningRequested,
        ActivityType.ContractSigned,
        ActivityType.PreLaunchInitialized,
        ActivityType.PreLaunchUpdated,
        ActivityType.PreLaunchCompleted,
        ActivityType.EndorsementCreated,
        ActivityType.EndorsementAcknowledged,
        ActivityType.DocumentArchived,
        ActivityType.LeadAssigned,
    };

    private static string CompletedTitle(ActivityType type) => type switch
    {
        ActivityType.StateChanged => "Workflow stage advanced",
        ActivityType.InquiryUpdated => "Inquiry updated",
        ActivityType.NurturingUpdated => "Nurturing details updated",
        ActivityType.CallOutcomeRecorded => "Call outcome recorded",
        ActivityType.DocumentUploaded => "Document uploaded",
        ActivityType.InvoiceGenerated => "Invoice generated",
        ActivityType.DownPaymentSubmittedForFinance => "Payment package submitted",
        ActivityType.PaymentConfirmed => "Payment confirmed",
        ActivityType.ContractGenerated => "Contract generated",
        ActivityType.ContractSubmitted => "Contract submitted for review",
        ActivityType.ContractApproved => "Contract approved",
        ActivityType.ContractRevisionRequested => "Contract revision requested",
        ActivityType.ContractSigningRequested => "Signing request sent",
        ActivityType.ContractSigned => "Contract signature recorded",
        ActivityType.PreLaunchInitialized => "Pre-launch started",
        ActivityType.PreLaunchUpdated => "Pre-launch checklist updated",
        ActivityType.PreLaunchCompleted => "Pre-launch completed",
        ActivityType.EndorsementCreated => "Handoff created",
        ActivityType.EndorsementAcknowledged => "Handoff acknowledged",
        ActivityType.DocumentArchived => "Document archived",
        ActivityType.LeadAssigned => "Opportunity assignment updated",
        _ => "Workflow action completed",
    };

    private static TaskResponse ToResponse(FollowUpTask task) => new(task.Id, task.LeadId, task.AssignedTo, task.Title, task.Status, task.CreatedAt, task.DueAt);
}
