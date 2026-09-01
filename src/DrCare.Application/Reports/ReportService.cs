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
    Task<FinanceWorkbenchResponse> FinanceWorkbenchAsync(FinanceWorkbenchQuery query, CancellationToken ct);
    Task<FinancePaymentDetail> FinancePaymentDetailAsync(Guid paymentId, CancellationToken ct);
    Task<IReadOnlyList<QueueItem>> FinanceQueueAsync(CancellationToken ct);
    Task<IReadOnlyList<QueueItem>> GeneralManagerQueueAsync(CancellationToken ct);
    Task<IReadOnlyList<QueueItem>> AdminQueueAsync(CancellationToken ct);
    Task<PagedResponse<AuditLogResponse>> AuditLogsAsync(Guid? leadId, int page, CancellationToken ct);
}

public sealed class ReportService(IReportingRepository reports, DrCare.Application.Settings.ISettingsService settings, ICurrentUser currentUser, IProcessRepository processes) : IReportService
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
        EnsureGeneralReportsAccess();
        var leads = await GetLeadsAsync(query, ct);
        var users = await reports.ListUsersAsync(currentUser.OrganizationId, ct);
        var endorsedLeadIds = (await reports.ListEndorsementsAsync(currentUser.OrganizationId, ct)).Select(x => x.LeadId).ToHashSet();
        var payments = await reports.ListPaymentsAsync(currentUser.OrganizationId, query.From, query.To, ct);
        var revenueByLead = payments.Where(x => x.Status == DownPaymentStatus.Confirmed)
            .GroupBy(x => x.LeadId)
            .ToDictionary(x => x.Key, x => x.Sum(payment => payment.Amount));
        return users.Where(x => x.Role == UserRole.MarketingAgent).Select(u =>
            new LeaderboardItem(
                u.Id,
                u.DisplayName,
                leads.Count(l => l.AssignedAgentId == u.Id),
                leads.Count(l => l.AssignedAgentId == u.Id && Reached(l.State, LeadState.Qualified)),
                leads.Count(l => l.AssignedAgentId == u.Id && endorsedLeadIds.Contains(l.Id)),
                leads.Where(l => l.AssignedAgentId == u.Id).Sum(l => revenueByLead.GetValueOrDefault(l.Id))))
            .OrderByDescending(x => x.Endorsed).ThenByDescending(x => x.Qualified).ThenByDescending(x => x.ConfirmedRevenue).ToArray();
    }
    public async Task<DownPaymentReport> DownPaymentsAsync(ReportQuery query, CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.Finance or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.AdminTeam or UserRole.Leadership)) throw new ForbiddenException("This role cannot view finance reports."); var items = await reports.ListPaymentsAsync(currentUser.OrganizationId, query.From, query.To, ct); return new DownPaymentReport(items.Where(x => x.Status is DownPaymentStatus.Invoiced or DownPaymentStatus.Confirmed).Sum(x => x.Amount), items.Where(x => x.Status == DownPaymentStatus.Confirmed).Sum(x => x.Amount), items.Count(x => x.Status == DownPaymentStatus.Invoiced), query.From ?? DateTimeOffset.UtcNow.AddDays(-30), query.To ?? DateTimeOffset.UtcNow, items.Where(x => x.Status == DownPaymentStatus.Invoiced).Sum(x => x.Amount));
    }
    public async Task<FinanceWorkbenchResponse> FinanceWorkbenchAsync(FinanceWorkbenchQuery query, CancellationToken ct)
    {
        EnsureFinanceAccess();
        if (query.From.HasValue && query.To.HasValue && query.From > query.To) throw new ValidationException("from must be earlier than to.");
        var page = Math.Clamp(query.Page, 1, 10_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var view = string.Equals(query.View, "history", StringComparison.OrdinalIgnoreCase) ? "history" : "action";
        var status = string.IsNullOrWhiteSpace(query.Status) ? "all" : query.Status.Trim().ToLowerInvariant();
        var result = await reports.ListFinancePaymentsPageAsync(currentUser.OrganizationId, view, status, query.Search, query.OwnerId, query.From, query.To, (page - 1) * pageSize, pageSize, ct);
        var allPayments = await reports.ListPaymentsAsync(currentUser.OrganizationId, null, null, ct);
        var leads = await reports.ListLeadsByIdsAsync(currentUser.OrganizationId, result.Items.Select(x => x.LeadId).ToHashSet(), ct);
        var users = (await reports.ListUsersAsync(currentUser.OrganizationId, ct)).ToDictionary(x => x.Id, x => x.DisplayName);
        var byLead = leads.ToDictionary(x => x.Id);
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var awaiting = allPayments.Where(IsAwaiting).ToArray();
        var items = result.Items
            .Where(x => byLead.ContainsKey(x.LeadId))
            .Select(x => ToFinancePayment(x, byLead[x.LeadId], users, false))
            .ToArray();
        return new FinanceWorkbenchResponse(
            items,
            page,
            pageSize,
            result.Total,
            awaiting.Length,
            awaiting.Sum(x => x.Amount),
            allPayments.Where(x => x.Status == DownPaymentStatus.Confirmed && x.ConfirmedAt >= monthStart && x.ConfirmedAt <= now).Sum(x => x.Amount),
            allPayments.Count(x => IsException(x.Status.ToString())));
    }
    public async Task<FinancePaymentDetail> FinancePaymentDetailAsync(Guid paymentId, CancellationToken ct)
    {
        EnsureFinanceAccess();
        var payment = await reports.GetPaymentAsync(currentUser.OrganizationId, paymentId, ct) ?? throw new NotFoundException("Payment record not found.");
        var leads = await reports.ListLeadsByIdsAsync(currentUser.OrganizationId, new[] { payment.LeadId }, ct);
        var lead = leads.SingleOrDefault() ?? throw new NotFoundException("Lead not found.");
        var users = (await reports.ListUsersAsync(currentUser.OrganizationId, ct)).ToDictionary(x => x.Id, x => x.DisplayName);
        var documents = (await processes.ListDocumentsAsync(currentUser.OrganizationId, lead.Id, ct))
            .Where(x => x.Status != DocumentStatus.Archived && x.DocumentType == "PAYMENT_RECEIPT")
            .Select(x => new DocumentResponse(x.Id, x.LeadId, x.DocumentType, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString(), x.CreatedAt))
            .ToArray();
        var activities = await reports.ListActivitiesPageAsync(currentUser.OrganizationId, lead.Id, 0, 100, ct);
        var events = activities.Items
            .Where(x => x.Type is ActivityType.InvoiceGenerated or ActivityType.DownPaymentSubmittedForFinance or ActivityType.PaymentConfirmed)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new FinancePaymentEvent(x.Id, x.Type.ToString(), x.Message, x.CreatedAt, users.GetValueOrDefault(x.ActorId, "System")))
            .ToArray();
        return new FinancePaymentDetail(ToFinancePayment(payment, lead, users, documents.Length > 0), events, documents);
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
    public async Task<PagedResponse<AuditLogResponse>> AuditLogsAsync(Guid? leadId, int page, CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership or UserRole.AdminTeam)) throw new ForbiddenException("Only authorized operational roles can view audit logs.");
        page = Math.Clamp(page, 1, 10_000);
        const int pageSize = 10;
        var result = await reports.ListActivitiesPageAsync(currentUser.OrganizationId, leadId, (page - 1) * pageSize, pageSize, ct);
        var items = result.Items.Select(x => new AuditLogResponse(x.Id, x.LeadId, x.ActorId, x.Type.ToString(), x.FromState?.ToString(), x.ToState?.ToString(), x.CreatedAt)).ToArray();
        return new PagedResponse<AuditLogResponse>(items, page, pageSize, result.Total);
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
    private void EnsureFinanceAccess() { if (currentUser.Role is not (UserRole.Finance or UserRole.Leadership)) throw new ForbiddenException("Only Finance and Leadership can access the finance workbench."); }
    private static bool IsAwaiting(DownPayment payment) => payment.Status == DownPaymentStatus.Invoiced && payment.SubmittedAt.HasValue;
    private static bool IsException(string status) => status is "Returned" or "Cancelled";
    private static FinancePaymentItem ToFinancePayment(DownPayment payment, Lead lead, IReadOnlyDictionary<Guid, string> users, bool hasEvidence)
    {
        var activityAt = payment.Status == DownPaymentStatus.Confirmed
            ? payment.ConfirmedAt ?? payment.UpdatedAt
            : payment.SubmittedAt ?? payment.UpdatedAt;
        var status = payment.Status == DownPaymentStatus.Invoiced && payment.SubmittedAt.HasValue ? "Awaiting" : payment.Status.ToString();
        return new FinancePaymentItem(payment.Id, lead.Id, lead.FullName, lead.PreferredLocation, lead.State, lead.AssignedAgentId, users.GetValueOrDefault(lead.AssignedAgentId, "Unknown owner"), payment.InvoiceNumber, payment.Amount, payment.Currency, status, activityAt, payment.SubmittedAt, payment.ConfirmedAt, payment.SubmittedBy.HasValue ? users.GetValueOrDefault(payment.SubmittedBy.Value, "Unknown") : null, payment.ConfirmedBy.HasValue ? users.GetValueOrDefault(payment.ConfirmedBy.Value, "Unknown") : null, payment.ConfirmationReference, hasEvidence);
    }
    private static bool Reached(LeadState current, LeadState target)
    {
        static int Rank(LeadState value) => value switch { LeadState.New => 0, LeadState.Inquiry or LeadState.InquiryIncomplete => 1, LeadState.Nurturing or LeadState.FollowUp => 2, LeadState.Qualified => 3, LeadState.DownPaymentPending => 4, LeadState.DownPaymentConfirmed => 5, LeadState.ContractDrafting => 6, LeadState.ContractReview => 7, LeadState.ContractSigned => 8, LeadState.PreLaunch => 9, LeadState.EndorsedToAdmin => 10, _ => 0 };
        return Rank(current) >= Rank(target);
    }
}
