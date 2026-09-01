using DrCare.Application.Contracts;
using DrCare.Domain;

namespace DrCare.Application.Reports;

public interface IReportService
{
    Task<OverviewReport> OverviewAsync(ReportQuery query, CancellationToken ct);
    Task<PipelineReport> PipelineAsync(ReportQuery query, CancellationToken ct);
    Task<ConversionReport> ConversionAsync(ReportQuery query, CancellationToken ct);
    Task<GoalReport> GoalsAsync(ReportQuery query, CancellationToken ct);
    Task<IReadOnlyList<LeaderboardItem>> LeaderboardAsync(ReportQuery query, CancellationToken ct);
    Task<DownPaymentReport> DownPaymentsAsync(ReportQuery query, CancellationToken ct);
    Task<IReadOnlyList<QueueItem>> FinanceQueueAsync(CancellationToken ct);
    Task<IReadOnlyList<QueueItem>> GeneralManagerQueueAsync(CancellationToken ct);
    Task<IReadOnlyList<QueueItem>> AdminQueueAsync(CancellationToken ct);
    Task<IReadOnlyList<AuditLogResponse>> AuditLogsAsync(CancellationToken ct);
}

public sealed class ReportService(IReportingRepository reports, DrCare.Application.Settings.ISettingsService settings, ICurrentUser currentUser) : IReportService
{
    public async Task<OverviewReport> OverviewAsync(ReportQuery query, CancellationToken ct)
    {
        EnsureGeneralReportsAccess();
        var leads = await GetLeadsAsync(query, ct); var payments = await reports.ListPaymentsAsync(currentUser.OrganizationId, query.From, query.To, ct);
        return new OverviewReport(leads.Count, leads.GroupBy(x => x.State.ToString()).ToDictionary(x => x.Key, x => x.Count()), payments.Where(x => x.Status == DownPaymentStatus.Confirmed).Sum(x => x.Amount), DateTimeOffset.UtcNow);
    }
    public async Task<PipelineReport> PipelineAsync(ReportQuery query, CancellationToken ct) { EnsureGeneralReportsAccess(); return new((await GetLeadsAsync(query, ct)).GroupBy(x => x.State.ToString()).ToDictionary(x => x.Key, x => x.Count()), DateTimeOffset.UtcNow); }
    public async Task<ConversionReport> ConversionAsync(ReportQuery query, CancellationToken ct)
    {
        EnsureGeneralReportsAccess(); var leads = await GetLeadsAsync(query, ct); var total = Math.Max(1, leads.Count);
        var ordered = new[] { LeadState.New, LeadState.Inquiry, LeadState.Nurturing, LeadState.Qualified, LeadState.DownPaymentPending, LeadState.DownPaymentConfirmed, LeadState.ContractDrafting, LeadState.ContractReview, LeadState.ContractSigned, LeadState.PreLaunch, LeadState.EndorsedToAdmin };
        var rates = ordered.ToDictionary(x => x.ToString(), x => Math.Round(leads.Count(l => Reached(l.State, x)) * 100m / total, 2));
        return new ConversionReport(rates, query.From ?? DateTimeOffset.UtcNow.AddDays(-30), query.To ?? DateTimeOffset.UtcNow);
    }
    public async Task<GoalReport> GoalsAsync(ReportQuery query, CancellationToken ct)
    {
        EnsureGeneralReportsAccess(); var leads = await GetLeadsAsync(query, ct); var goal = await settings.GetAnnualGoalAsync(ct); var achieved = leads.Count(x => x.State == LeadState.EndorsedToAdmin); return new GoalReport(goal.Year, goal.Target, achieved, Math.Round(achieved * 100m / Math.Max(1, goal.Target), 2));
    }
    public async Task<IReadOnlyList<LeaderboardItem>> LeaderboardAsync(ReportQuery query, CancellationToken ct)
    {
        EnsureGeneralReportsAccess(); var leads = await GetLeadsAsync(query, ct); var users = await reports.ListUsersAsync(currentUser.OrganizationId, ct); var endorsedLeadIds = (await reports.ListEndorsementsAsync(currentUser.OrganizationId, ct)).Select(x => x.LeadId).ToHashSet();
        return users.Where(x => x.Role == UserRole.MarketingAgent).Select(u => new LeaderboardItem(u.Id, u.DisplayName, leads.Count(l => l.AssignedAgentId == u.Id), leads.Count(l => l.AssignedAgentId == u.Id && Reached(l.State, LeadState.Qualified)), leads.Count(l => l.AssignedAgentId == u.Id && endorsedLeadIds.Contains(l.Id)))).OrderByDescending(x => x.Endorsed).ThenByDescending(x => x.Qualified).ToArray();
    }
    public async Task<DownPaymentReport> DownPaymentsAsync(ReportQuery query, CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.Finance or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.AdminTeam or UserRole.Leadership)) throw new ForbiddenException("This role cannot view finance reports."); var items = await reports.ListPaymentsAsync(currentUser.OrganizationId, query.From, query.To, ct); return new DownPaymentReport(items.Where(x => x.Status is DownPaymentStatus.Invoiced or DownPaymentStatus.Confirmed).Sum(x => x.Amount), items.Where(x => x.Status == DownPaymentStatus.Confirmed).Sum(x => x.Amount), items.Count(x => x.Status == DownPaymentStatus.Invoiced), query.From ?? DateTimeOffset.UtcNow.AddDays(-30), query.To ?? DateTimeOffset.UtcNow);
    }
    public async Task<IReadOnlyList<QueueItem>> FinanceQueueAsync(CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.Finance or UserRole.Leadership)) throw new ForbiddenException("Only Finance and Leadership can view the payment queue.");
        var payments = (await reports.ListPaymentsAsync(currentUser.OrganizationId, null, null, ct)).Where(x => x.Status == DownPaymentStatus.Invoiced && x.SubmittedAt.HasValue); return await LeadsForAsync(payments.Select(x => x.LeadId), ct);
    }
    public async Task<IReadOnlyList<QueueItem>> GeneralManagerQueueAsync(CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.GeneralManager or UserRole.Leadership)) throw new ForbiddenException("Only the General Manager and Leadership can view the contract review queue.");
        var contracts = (await reports.ListContractsAsync(currentUser.OrganizationId, ct)).Where(x => x.Status == ContractStatus.InReview); return await LeadsForAsync(contracts.Select(x => x.LeadId), ct);
    }
    public async Task<IReadOnlyList<QueueItem>> AdminQueueAsync(CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.AdminTeam or UserRole.Leadership)) throw new ForbiddenException("Only the Admin Team and Leadership can view the endorsement queue.");
        var endorsements = (await reports.ListEndorsementsAsync(currentUser.OrganizationId, ct)).Where(x => x.Status == EndorsementStatus.Pending); return await LeadsForAsync(endorsements.Select(x => x.LeadId), ct);
    }
    public async Task<IReadOnlyList<AuditLogResponse>> AuditLogsAsync(CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership or UserRole.AdminTeam)) throw new ForbiddenException("Only authorized operational roles can view audit logs.");
        return (await reports.ListActivitiesAsync(currentUser.OrganizationId, ct)).Select(x => new AuditLogResponse(x.Id, x.LeadId, x.ActorId, x.Type.ToString(), x.FromState?.ToString(), x.ToState?.ToString(), x.CreatedAt)).ToArray();
    }
    private async Task<IReadOnlyList<Lead>> GetLeadsAsync(ReportQuery query, CancellationToken ct)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To) throw new ValidationException("from must be earlier than to.");
        if (query.From.HasValue && query.To.HasValue && query.To.Value - query.From.Value > TimeSpan.FromDays(366)) throw new ValidationException("Report range cannot exceed 366 days.");
        return await reports.ListLeadsAsync(currentUser.OrganizationId, currentUser.Role == UserRole.MarketingAgent ? currentUser.UserId : query.AgentId, query.From, query.To, ct);
    }
    private async Task<IReadOnlyList<QueueItem>> LeadsForAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var set = ids.ToHashSet(); return (await reports.ListLeadsAsync(currentUser.OrganizationId, currentUser.Role == UserRole.MarketingAgent ? currentUser.UserId : null, null, null, ct)).Where(x => set.Contains(x.Id)).Select(x => new QueueItem(x.Id, x.FullName, x.State, x.UpdatedAt, x.Version)).ToArray();
    }
    private void EnsureGeneralReportsAccess() { if (currentUser.Role is not (UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership)) throw new ForbiddenException("This role cannot view organization-wide reports."); }
    private static bool Reached(LeadState current, LeadState target)
    {
        static int Rank(LeadState value) => value switch { LeadState.New => 0, LeadState.Inquiry or LeadState.InquiryIncomplete => 1, LeadState.Nurturing or LeadState.FollowUp => 2, LeadState.Qualified => 3, LeadState.DownPaymentPending => 4, LeadState.DownPaymentConfirmed => 5, LeadState.ContractDrafting => 6, LeadState.ContractReview => 7, LeadState.ContractSigned => 8, LeadState.PreLaunch => 9, LeadState.EndorsedToAdmin => 10, _ => 0 };
        return Rank(current) >= Rank(target);
    }
}
