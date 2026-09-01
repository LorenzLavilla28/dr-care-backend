using DrCare.Application;
using DrCare.Domain;
using Microsoft.EntityFrameworkCore;

namespace DrCare.Infrastructure.Persistence;

public sealed class LeadRepository(ApplicationDbContext db) : ILeadRepository
{
    public async Task<Lead?> GetAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken) =>
        await db.Leads
            .Include(x => x.LocationAnalysis)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == leadId, cancellationToken);

    public async Task<(IReadOnlyList<Lead> Items, int Total)> SearchAsync(Guid organizationId, Guid? assignedAgentId, LeadState? state, ProductLine? productLine, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, string? search, int skip, int take, string sort, CancellationToken cancellationToken)
    {
        var query = db.Leads.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (assignedAgentId.HasValue) query = query.Where(x => x.AssignedAgentId == assignedAgentId.Value);
        if (state.HasValue) query = query.Where(x => x.State == state.Value);
        if (productLine.HasValue) query = query.Where(x => x.ProductLine == productLine.Value);
        if (createdFrom.HasValue) query = query.Where(x => x.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue) query = query.Where(x => x.CreatedAt <= createdTo.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.Email.ToLower().Contains(term) || x.ContactNumber.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        query = sort switch
        {
            "createdAt" => query.OrderByDescending(x => x.CreatedAt),
            "name" => query.OrderBy(x => x.FullName),
            _ => query.OrderByDescending(x => x.UpdatedAt)
        };
        var items = await query.Skip(skip).Take(take).ToArrayAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<ActivityLog> Items, int Total)> ListActivitiesAsync(Guid organizationId, Guid leadId, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.ActivityLogs.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.LeadId == leadId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToArrayAsync(cancellationToken);
        return (items, total);
    }

    public async Task AddAsync(Lead lead, CancellationToken cancellationToken) => await db.Leads.AddAsync(lead, cancellationToken);
    public async Task AddActivityAsync(ActivityLog activity, CancellationToken cancellationToken) => await db.ActivityLogs.AddAsync(activity, cancellationToken);
    public async Task AddTaskAsync(FollowUpTask task, CancellationToken cancellationToken) => await db.FollowUpTasks.AddAsync(task, cancellationToken);
    public async Task AddLocationAnalysisAsync(LocationAnalysis analysis, CancellationToken cancellationToken) => await db.LocationAnalyses.AddAsync(analysis, cancellationToken);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class TaskRepository(ApplicationDbContext db) : ITaskRepository
{
    public Task<FollowUpTask?> GetAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken) =>
        db.FollowUpTasks.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == taskId, cancellationToken);

    public Task<FollowUpTask?> FindOpenByLeadAndTitleAsync(Guid organizationId, Guid leadId, string title, CancellationToken cancellationToken) =>
        db.FollowUpTasks.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.LeadId == leadId && x.Status == DrCare.Domain.TaskStatus.Open && x.Title.ToLower() == title.Trim().ToLower(), cancellationToken);

    public async Task<IReadOnlyList<FollowUpTask>> ListAsync(Guid organizationId, Guid? assignedTo, Guid? leadId, CancellationToken cancellationToken)
    {
        var query = db.FollowUpTasks.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (assignedTo.HasValue) query = query.Where(x => x.AssignedTo == assignedTo.Value);
        if (leadId.HasValue) query = query.Where(x => x.LeadId == leadId.Value);
        return await query.OrderBy(x => x.Status).ThenBy(x => x.DueAt).ThenByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<FollowUpTask>> ListCompletedAsync(Guid organizationId, Guid assignedTo, CancellationToken cancellationToken) =>
        await db.FollowUpTasks.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.AssignedTo == assignedTo && x.Status == DrCare.Domain.TaskStatus.Completed)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

    public async Task<(IReadOnlyList<FollowUpTask> Items, int Total)> ListCompletedPageAsync(Guid organizationId, Guid assignedTo, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.FollowUpTasks.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.AssignedTo == assignedTo && x.Status == DrCare.Domain.TaskStatus.Completed);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToArrayAsync(cancellationToken);
        return (items, total);
    }

    public async Task AddAsync(FollowUpTask task, CancellationToken cancellationToken) => await db.FollowUpTasks.AddAsync(task, cancellationToken);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class UserRepository(ApplicationDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(x => x.Email == email.Trim().ToLower(), cancellationToken);

    public Task<User?> GetAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == userId, cancellationToken);
    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken) => db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.DisplayName).ToArrayAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken) => await db.Users.AddAsync(user, cancellationToken);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class AuthTokenRepository(ApplicationDbContext db) : IAuthTokenRepository
{
    public Task<RefreshToken?> FindRefreshTokenAsync(string hash, CancellationToken ct) => db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct) => await db.RefreshTokens.AddAsync(token, ct);
    public Task<PasswordResetToken?> FindPasswordResetTokenAsync(string hash, CancellationToken ct) => db.PasswordResetTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    public async Task AddPasswordResetTokenAsync(PasswordResetToken token, CancellationToken ct) => await db.PasswordResetTokens.AddAsync(token, ct);
    public async Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ToArrayAsync(ct);
        foreach (var token in tokens) token.Revoke();
    }
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class WorkflowRepository(ApplicationDbContext db) : IWorkflowRepository
{
    public Task<WorkflowRecord?> GetAsync(Guid organizationId, Guid recordId, CancellationToken cancellationToken) =>
        db.WorkflowRecords.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == recordId, cancellationToken);

    public async Task<IReadOnlyList<WorkflowRecord>> ListAsync(Guid organizationId, string? operation, Guid? leadId, CancellationToken cancellationToken)
    {
        var query = db.WorkflowRecords.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(operation)) query = query.Where(x => x.Operation == operation);
        if (leadId.HasValue) query = query.Where(x => x.LeadId == leadId.Value);
        return await query.OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(WorkflowRecord record, CancellationToken cancellationToken) => await db.WorkflowRecords.AddAsync(record, cancellationToken);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class ProcessRepository(ApplicationDbContext db) : IProcessRepository
{
    public Task<DownPayment?> GetDownPaymentAsync(Guid organizationId, Guid leadId, CancellationToken ct) => db.DownPayments.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.LeadId == leadId, ct);
    public async Task<IReadOnlyList<DownPayment>> ListDownPaymentsAsync(Guid organizationId, IReadOnlyCollection<Guid> leadIds, CancellationToken ct) => await db.DownPayments.AsNoTracking().Where(x => x.OrganizationId == organizationId && leadIds.Contains(x.LeadId)).ToArrayAsync(ct);
    public async Task AddDownPaymentAsync(DownPayment payment, CancellationToken ct) => await db.DownPayments.AddAsync(payment, ct);
    public Task<LeadDocument?> GetDocumentAsync(Guid organizationId, Guid leadId, Guid documentId, CancellationToken ct) => db.LeadDocuments.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.LeadId == leadId && x.Id == documentId, ct);
    public async Task<IReadOnlyList<LeadDocument>> ListDocumentsAsync(Guid organizationId, Guid leadId, CancellationToken ct) => await db.LeadDocuments.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.LeadId == leadId && x.Status != DocumentStatus.Archived).OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
    public async Task AddDocumentAsync(LeadDocument document, CancellationToken ct) => await db.LeadDocuments.AddAsync(document, ct);
    public Task<Contract?> GetContractAsync(Guid organizationId, Guid leadId, CancellationToken ct) => db.Contracts.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.LeadId == leadId, ct);
    public async Task AddContractAsync(Contract contract, CancellationToken ct) => await db.Contracts.AddAsync(contract, ct);
    public Task<ContractSigningRequest?> GetSigningRequestByTokenHashAsync(string tokenHash, CancellationToken ct) => db.ContractSigningRequests.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
    public async Task<IReadOnlyList<ContractSigningRequest>> ListSigningRequestsAsync(Guid organizationId, Guid contractId, CancellationToken ct) => await db.ContractSigningRequests.Where(x => x.OrganizationId == organizationId && x.ContractId == contractId).OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
    public async Task AddSigningRequestAsync(ContractSigningRequest request, CancellationToken ct) => await db.ContractSigningRequests.AddAsync(request, ct);
    public Task<Endorsement?> GetEndorsementAsync(Guid organizationId, Guid endorsementId, CancellationToken ct) => db.Endorsements.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == endorsementId, ct);
    public async Task<IReadOnlyList<Endorsement>> ListEndorsementsAsync(Guid organizationId, CancellationToken ct) => await db.Endorsements.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(ct);
    public async Task AddEndorsementAsync(Endorsement endorsement, CancellationToken ct) => await db.Endorsements.AddAsync(endorsement, ct);
    public async Task<IReadOnlyList<Notification>> ListNotificationsAsync(Guid organizationId, Guid recipientId, CancellationToken ct) => await db.Notifications.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.RecipientId == recipientId).OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(ct);
    public Task<Notification?> GetNotificationAsync(Guid organizationId, Guid recipientId, Guid notificationId, CancellationToken ct) => db.Notifications.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.RecipientId == recipientId && x.Id == notificationId, ct);
    public async Task AddNotificationAsync(Notification notification, CancellationToken ct) => await db.Notifications.AddAsync(notification, ct);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class PreLaunchRepository(ApplicationDbContext db) : IPreLaunchRepository
{
    public Task<PreLaunchChecklist?> GetAsync(Guid organizationId, Guid leadId, CancellationToken ct) => db.PreLaunchChecklists.Include("_items").SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.LeadId == leadId, ct);
    public async Task AddAsync(PreLaunchChecklist checklist, CancellationToken ct) => await db.PreLaunchChecklists.AddAsync(checklist, ct);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class SettingsRepository(ApplicationDbContext db) : ISettingsRepository
{
    public Task<AppSetting?> GetAsync(Guid organizationId, string key, CancellationToken ct) => db.AppSettings.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Key == key, ct);
    public async Task AddAsync(AppSetting setting, CancellationToken ct) => await db.AppSettings.AddAsync(setting, ct);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class ReportingRepository(ApplicationDbContext db) : IReportingRepository
{
    public async Task<IReadOnlyList<Lead>> ListLeadsAsync(Guid org, Guid? assignedAgentId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = db.Leads.AsNoTracking().Where(x => x.OrganizationId == org);
        if (assignedAgentId.HasValue) query = query.Where(x => x.AssignedAgentId == assignedAgentId.Value);
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        return await query.ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<Lead>> ListLeadsByIdsAsync(Guid org, IReadOnlyCollection<Guid> leadIds, CancellationToken ct)
    {
        if (leadIds.Count == 0) return Array.Empty<Lead>();
        return await db.Leads.AsNoTracking().Where(x => x.OrganizationId == org && leadIds.Contains(x.Id)).ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<DownPayment>> ListPaymentsAsync(Guid org, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = db.DownPayments.AsNoTracking().Where(x => x.OrganizationId == org);
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        return await query.ToArrayAsync(ct);
    }
    public Task<DownPayment?> GetPaymentAsync(Guid org, Guid paymentId, CancellationToken ct) =>
        db.DownPayments.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == paymentId, ct);
    public async Task<(IReadOnlyList<DownPayment> Items, int Total)> ListFinancePaymentsPageAsync(Guid org, string view, string status, string? search, Guid? ownerId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct)
    {
        var query = db.DownPayments.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.InvoiceNumber != null &&
                (x.Status == DownPaymentStatus.Confirmed || (x.Status == DownPaymentStatus.Invoiced && x.SubmittedAt != null)));

        if (string.Equals(view, "action", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == DownPaymentStatus.Invoiced && x.SubmittedAt != null);
        if (string.Equals(status, "awaiting", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == DownPaymentStatus.Invoiced && x.SubmittedAt != null);
        else if (string.Equals(status, "confirmed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == DownPaymentStatus.Confirmed);
        else if (string.Equals(status, "returned", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
            query = query.Where(_ => false);

        if (ownerId.HasValue)
            query = query.Where(x => db.Leads.Any(lead => lead.OrganizationId == org && lead.Id == x.LeadId && lead.AssignedAgentId == ownerId.Value));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => (x.InvoiceNumber != null && x.InvoiceNumber.Contains(term)) || db.Leads.Any(lead => lead.OrganizationId == org && lead.Id == x.LeadId && lead.FullName.Contains(term)));
        }
        if (from.HasValue)
            query = query.Where(x => (x.Status == DownPaymentStatus.Confirmed && x.ConfirmedAt >= from.Value) || (x.Status != DownPaymentStatus.Confirmed && ((x.SubmittedAt.HasValue && x.SubmittedAt >= from.Value) || (!x.SubmittedAt.HasValue && x.CreatedAt >= from.Value))));
        if (to.HasValue)
            query = query.Where(x => (x.Status == DownPaymentStatus.Confirmed && x.ConfirmedAt <= to.Value) || (x.Status != DownPaymentStatus.Confirmed && ((x.SubmittedAt.HasValue && x.SubmittedAt <= to.Value) || (!x.SubmittedAt.HasValue && x.CreatedAt <= to.Value))));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);
        return (items, total);
    }
    public async Task<IReadOnlyList<Endorsement>> ListEndorsementsAsync(Guid org, CancellationToken ct) => await db.Endorsements.AsNoTracking().Where(x => x.OrganizationId == org).ToArrayAsync(ct);
    public async Task<IReadOnlyList<Contract>> ListContractsAsync(Guid org, CancellationToken ct) => await db.Contracts.AsNoTracking().Where(x => x.OrganizationId == org).ToArrayAsync(ct);
    public async Task<IReadOnlyList<ActivityLog>> ListActivitiesAsync(Guid org, CancellationToken ct) => await db.ActivityLogs.AsNoTracking().Where(x => x.OrganizationId == org).OrderByDescending(x => x.CreatedAt).Take(500).ToArrayAsync(ct);
    public async Task<IReadOnlyList<ActivityLog>> ListActivitiesForActorAsync(Guid org, Guid actorId, CancellationToken ct) =>
        await db.ActivityLogs.AsNoTracking().Where(x => x.OrganizationId == org && x.ActorId == actorId).OrderByDescending(x => x.CreatedAt).Take(200).ToArrayAsync(ct);

    public async Task<(IReadOnlyList<ActivityLog> Items, int Total)> ListActivitiesPageAsync(Guid organizationId, Guid? leadId, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.ActivityLogs.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (leadId.HasValue) query = query.Where(x => x.LeadId == leadId.Value);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToArrayAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<ActivityLog> Items, int Total)> ListActivitiesForActorPageAsync(Guid organizationId, Guid actorId, IReadOnlyCollection<ActivityType> types, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.ActivityLogs.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.ActorId == actorId && types.Contains(x.Type));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToArrayAsync(cancellationToken);
        return (items, total);
    }
    public async Task<IReadOnlyList<User>> ListUsersAsync(Guid org, CancellationToken ct) => await db.Users.AsNoTracking().Where(x => x.OrganizationId == org).ToArrayAsync(ct);
}
