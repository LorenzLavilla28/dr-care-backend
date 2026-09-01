using DrCare.Domain;
using DrCare.Application.Contracts;
using DrCare.Application.Notifications;
using DrCare.Application.Processes;
using DrCare.Application.Settings;

namespace DrCare.Application.Leads;

public interface ILeadService
{
    Task<LeadPage> SearchAsync(LeadState? state, ProductLine? productLine, Guid? assignedAgentId, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, string? search, string? cursor, int page, int pageSize, string sort, CancellationToken cancellationToken);
    Task<LeadDetails> GetAsync(Guid leadId, CancellationToken cancellationToken);
    Task<LeadDetails> CreateAsync(CreateLeadRequest request, CancellationToken cancellationToken);
    Task<LeadDetails> StartInquiryAsync(Guid leadId, CancellationToken cancellationToken);
    Task<LeadDetails> UpdateInquiryAsync(Guid leadId, UpdateInquiryRequest request, CancellationToken cancellationToken);
    Task<SubmitInquiryResponse> SubmitInquiryAsync(Guid leadId, CancellationToken cancellationToken);
    Task<LeadDetails> GetInquiryAsync(Guid leadId, CancellationToken cancellationToken);
    Task<NurturingResponse> GetNurturingAsync(Guid leadId, CancellationToken cancellationToken);
    Task<NurturingResponse> UpdateNurturingAsync(Guid leadId, UpdateNurturingRequest request, CancellationToken cancellationToken);
    Task<CallOutcomeResponse> RecordCallOutcomeAsync(Guid leadId, CallOutcomeRequest request, CancellationToken cancellationToken);
    Task<QualificationResponse> QualifyAsync(Guid leadId, QualificationRequest request, CancellationToken cancellationToken);
    Task<ActivityResponse> AddActivityAsync(Guid leadId, AddActivityRequest request, CancellationToken cancellationToken);
    Task<LeadDetails> AssignAsync(Guid leadId, AssignLeadRequest request, CancellationToken cancellationToken);
    Task<LocationAnalysisResponse> GetLocationAnalysisAsync(Guid leadId, CancellationToken cancellationToken);
    Task<LocationAnalysisResponse> UpdateLocationAnalysisAsync(Guid leadId, UpdateLocationAnalysisRequest request, CancellationToken cancellationToken);
    Task<LocationEvaluationResponse> EvaluateLocationAnalysisAsync(Guid leadId, LocationEvaluationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityResponse>> GetActivitiesAsync(Guid leadId, CancellationToken cancellationToken);
}

public sealed class LeadService(ILeadRepository leads, IProcessRepository processes, ITaskRepository tasks, IUserRepository users, ISettingsService settings, ICurrentUser currentUser, INotificationService notifications) : ILeadService
{
    public async Task<LeadPage> SearchAsync(LeadState? state, ProductLine? productLine, Guid? assignedAgentId, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, string? search, string? cursor, int page, int pageSize, string sort, CancellationToken cancellationToken)
    {
        EnsureLeadReadAccess();
        page = Math.Clamp(page, 1, 10_000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            try { page = Math.Max(1, int.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), System.Globalization.CultureInfo.InvariantCulture)); }
            catch (Exception) { throw new ValidationException("cursor is invalid."); }
        }
        if (createdFrom.HasValue && createdTo.HasValue && createdFrom > createdTo) throw new ValidationException("createdFrom must be earlier than createdTo.");
        if (sort is not ("updatedAt" or "createdAt" or "name")) throw new ValidationException("sort must be updatedAt, createdAt, or name.");
        if (currentUser.Role == UserRole.MarketingAgent && assignedAgentId.HasValue && assignedAgentId != currentUser.UserId) throw new ForbiddenException("Marketing agents cannot query another agent's leads.");
        assignedAgentId = currentUser.Role == UserRole.MarketingAgent ? currentUser.UserId : assignedAgentId;
        var result = await leads.SearchAsync(currentUser.OrganizationId, assignedAgentId, state, productLine, createdFrom, createdTo, search?.Trim(), (page - 1) * pageSize, pageSize, sort, cancellationToken);
        var paymentLeadIds = result.Items
            .Where(x => x.State == LeadState.DownPaymentPending)
            .Select(x => x.Id)
            .ToArray();
        var submittedForFinance = paymentLeadIds.Length == 0
            ? new HashSet<Guid>()
            : (await processes.ListDownPaymentsAsync(currentUser.OrganizationId, paymentLeadIds, cancellationToken))
                .Where(x => x.SubmittedAt.HasValue)
                .Select(x => x.LeadId)
                .ToHashSet();
        var agentNames = (await users.ListAsync(currentUser.OrganizationId, cancellationToken))
            .ToDictionary(x => x.Id, x => x.DisplayName);
        return new LeadPage(result.Items.Select(x => ToSummary(x, submittedForFinance.Contains(x.Id), agentNames.GetValueOrDefault(x.AssignedAgentId))).ToArray(), page, pageSize, result.Total);
    }

    public async Task<LeadDetails> GetAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        return await ToDetailsAsync(lead, cancellationToken);
    }

    public async Task<LeadDetails> CreateAsync(CreateLeadRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var assignedAgentId = request.AssignedAgentId ?? currentUser.UserId;
        if (currentUser.Role == UserRole.MarketingAgent && assignedAgentId != currentUser.UserId)
            throw new ForbiddenException("Marketing agents may only create leads assigned to themselves.");

        await EnsureValidAgentAsync(assignedAgentId, cancellationToken);
        var lead = new Lead(currentUser.OrganizationId, request.FullName, request.ContactNumber ?? string.Empty, request.Email ?? string.Empty, request.SourceOfIncome ?? string.Empty, assignedAgentId, request.LeadSource);
        if (request.ProductLine.HasValue) lead.SetProductLine(request.ProductLine.Value, PriceFor(request.ProductLine.Value, await settings.GetPricingAsync(cancellationToken)));
        lead.AddActivity(new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.LeadCreated, "Lead created."));
        await leads.AddAsync(lead, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(lead, cancellationToken);
    }

    public async Task<LeadDetails> StartInquiryAsync(Guid leadId, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        var from = lead.State;
        try { lead.StartInquiry(); }
        catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.StateChanged, "Inquiry started.", from, lead.State);
        lead.AddActivity(activity);
        await leads.AddActivityAsync(activity, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(lead, cancellationToken);
    }

    public async Task<LeadDetails> UpdateInquiryAsync(Guid leadId, UpdateInquiryRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != lead.Version)
            throw new ConflictException("The lead was changed by another user. Refresh and try again.");
        if (lead.State is not (LeadState.Inquiry or LeadState.InquiryIncomplete))
            throw new ConflictException("Inquiry fields can only be edited while the lead is in inquiry.");

        lead.UpdateInquiry(request.FullName, request.Age, request.ContactNumber, request.Email, request.SourceOfIncome, request.PreferredLocation, request.Address, request.Industry, request.MeetingDateTime, request.QuestionsConcerns);
        if (!string.IsNullOrWhiteSpace(lead.PreferredLocation))
        {
            var hadLocationAnalysis = lead.LocationAnalysis is not null;
            lead.SetLocationAnalysis();
            if (!hadLocationAnalysis && lead.LocationAnalysis is not null)
                await leads.AddLocationAnalysisAsync(lead.LocationAnalysis, cancellationToken);
            else if (lead.LocationAnalysis is not null &&
                     !lead.LocationAnalysis.PreferredLocation.Equals(lead.PreferredLocation, StringComparison.Ordinal))
                lead.LocationAnalysis.Update(lead.PreferredLocation, lead.LocationAnalysis.Notes);
        }
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.InquiryUpdated, "Inquiry details updated.");
        lead.AddActivity(activity);
        await leads.AddActivityAsync(activity, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(lead, cancellationToken);
    }

    public async Task<SubmitInquiryResponse> SubmitInquiryAsync(Guid leadId, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        var from = lead.State;
        IReadOnlyList<string> missing;
        try { lead.SubmitInquiry(out missing); }
        catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }

        var stateActivity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.StateChanged,
            missing.Count == 0 ? "Inquiry accepted and nurturing started." : "Inquiry is incomplete; follow-up required.", from, lead.State);
        lead.AddActivity(stateActivity);
        await leads.AddActivityAsync(stateActivity, cancellationToken);

        if (missing.Count == 0)
        {
            var hadLocationAnalysis = lead.LocationAnalysis is not null;
            lead.SetLocationAnalysis();
            if (!hadLocationAnalysis && lead.LocationAnalysis is not null)
                await leads.AddLocationAnalysisAsync(lead.LocationAnalysis, cancellationToken);
            var welcomeActivity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.WelcomeEmailQueued, "Welcome email queued for delivery.");
            lead.AddActivity(welcomeActivity);
            await leads.AddActivityAsync(welcomeActivity, cancellationToken);
            await notifications.NotifyUserAsync(currentUser.OrganizationId, lead.AssignedAgentId, "welcome-email-queued", "Welcome email queued", $"The welcome email for {lead.FullName} is queued for delivery.", cancellationToken);
        }
        else
        {
            if (await tasks.FindOpenByLeadAndTitleAsync(currentUser.OrganizationId, lead.Id, "Complete missing inquiry information.", cancellationToken) is null)
            {
                var task = new FollowUpTask(currentUser.OrganizationId, lead.Id, lead.AssignedAgentId, "Complete missing inquiry information.");
                lead.AddTask(task);
                await leads.AddTaskAsync(task, cancellationToken);
                var followUpActivity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.FollowUpCreated, "Follow-up task created.");
                lead.AddActivity(followUpActivity);
                await leads.AddActivityAsync(followUpActivity, cancellationToken);
            }
            await notifications.NotifyUserAsync(currentUser.OrganizationId, lead.AssignedAgentId, "inquiry-incomplete", "Inquiry needs attention", $"Complete the missing inquiry information for {lead.FullName}.", cancellationToken);
        }

        await leads.SaveChangesAsync(cancellationToken);
        return new SubmitInquiryResponse(await ToDetailsAsync(lead, cancellationToken), missing.Count == 0, missing);
    }

    public async Task<LeadDetails> GetInquiryAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        return await ToDetailsAsync(lead, cancellationToken);
    }

    public async Task<NurturingResponse> GetNurturingAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        return ToNurturing(lead);
    }

    public async Task<NurturingResponse> UpdateNurturingAsync(Guid leadId, UpdateNurturingRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        EnsureVersion(lead, request.ExpectedVersion);
        if (lead.State is not (LeadState.Nurturing or LeadState.FollowUp))
            throw new ConflictException("Nurturing can only be updated before qualification is completed.");
        lead.UpdateNurturing(request.Notes);
        if (request.ProductLine.HasValue)
        {
            var pricing = await settings.GetPricingAsync(cancellationToken);
            var listPrice = PriceFor(request.ProductLine.Value, pricing);
            lead.SetProductLine(request.ProductLine.Value, listPrice, request.ActualPrice);
        }
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.NurturingUpdated, "Nurturing details and franchise product were updated.");
        lead.AddActivity(activity);
        await leads.AddActivityAsync(activity, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return ToNurturing(lead);
    }

    public async Task<CallOutcomeResponse> RecordCallOutcomeAsync(Guid leadId, CallOutcomeRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        EnsureVersion(lead, request.ExpectedVersion);
        try { lead.RecordCallOutcome(request.Outcome, request.WelcomeEmailReceived, request.GoodTimeToDiscuss, request.Notes); }
        catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        Guid? taskId = null;
        if (request.FollowUpAt.HasValue && await tasks.FindOpenByLeadAndTitleAsync(currentUser.OrganizationId, lead.Id, "Nurturing follow-up.", cancellationToken) is null)
        {
            var task = new FollowUpTask(currentUser.OrganizationId, lead.Id, lead.AssignedAgentId, "Nurturing follow-up.", request.FollowUpAt);
            taskId = task.Id;
            lead.AddTask(task);
            await leads.AddTaskAsync(task, cancellationToken);
        }
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.CallOutcomeRecorded, $"Nurturing call outcome recorded: {request.Outcome.Trim()}.");
        lead.AddActivity(activity); await leads.AddActivityAsync(activity, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return new CallOutcomeResponse(lead.Id, request.Outcome.Trim(), lead.LastContactedAt ?? DateTimeOffset.UtcNow, taskId);
    }

    public async Task<QualificationResponse> QualifyAsync(Guid leadId, QualificationRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        EnsureVersion(lead, request.ExpectedVersion);
        if (!lead.ProductLine.HasValue) throw new ConflictException("Select a product line and save the nurturing details before qualification.");
        if (string.IsNullOrWhiteSpace(lead.LastCallOutcome)) throw new ConflictException("Record the nurturing call outcome before qualification.");
        var from = lead.State;
        try { lead.EvaluateQualification(request.Decision); }
        catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        lead.UpdateNurturing(request.Notes);
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.StateChanged, "Lead qualification recorded.", from, lead.State);
        lead.AddActivity(activity);
        await leads.AddActivityAsync(activity, cancellationToken);
        if (request.Decision.Equals("follow_up", StringComparison.OrdinalIgnoreCase) && !request.FollowUpAt.HasValue)
            throw new ValidationException("A follow-up date is required when the lead needs follow-up.");
        if (request.Decision.Equals("follow_up", StringComparison.OrdinalIgnoreCase) && request.FollowUpAt.HasValue &&
            await tasks.FindOpenByLeadAndTitleAsync(currentUser.OrganizationId, lead.Id, "Qualification follow-up.", cancellationToken) is null)
        {
            var task = new FollowUpTask(currentUser.OrganizationId, lead.Id, lead.AssignedAgentId, "Qualification follow-up.", request.FollowUpAt);
            lead.AddTask(task);
            await leads.AddTaskAsync(task, cancellationToken);
        }
        await leads.SaveChangesAsync(cancellationToken);
        return new QualificationResponse(lead.Id, lead.State, lead.State == LeadState.Qualified, lead.UpdatedAt);
    }

    public async Task<ActivityResponse> AddActivityAsync(Guid leadId, AddActivityRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        if (!Enum.TryParse<ActivityType>(request.ActivityType.Replace("-", ""), true, out var type) ||
            type is ActivityType.LeadCreated or ActivityType.StateChanged or ActivityType.InquiryUpdated or ActivityType.FollowUpCreated or ActivityType.WelcomeEmailQueued)
            throw new ValidationException("Activity type is not allowed for manual entry.");
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, type, request.Notes);
        lead.AddActivity(activity);
        await leads.AddActivityAsync(activity, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return new ActivityResponse(activity.Id, activity.Type, activity.Message, activity.FromState, activity.ToState, activity.CreatedAt, currentUser.DisplayName);
    }

    public async Task<LeadDetails> AssignAsync(Guid leadId, AssignLeadRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.Role != UserRole.MarketingAdmin)
            throw new ForbiddenException("Only marketing administrators can assign leads.");
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        EnsureVersion(lead, request.ExpectedVersion);
        await EnsureValidAgentAsync(request.AssignedAgentId, cancellationToken);
        lead.AssignTo(request.AssignedAgentId);
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, ActivityType.LeadAssigned, "Lead assignment updated.");
        lead.AddActivity(activity); await leads.AddActivityAsync(activity, cancellationToken);
        await leads.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(lead, cancellationToken);
    }

    public async Task<LocationAnalysisResponse> GetLocationAnalysisAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        if (lead.LocationAnalysis is null) throw new NotFoundException("Location analysis has not been initialized.");
        return ToLocation(lead.LocationAnalysis);
    }

    public async Task<LocationAnalysisResponse> UpdateLocationAnalysisAsync(Guid leadId, UpdateLocationAnalysisRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        EnsureVersion(lead, request.ExpectedVersion);
        var hadLocationAnalysis = lead.LocationAnalysis is not null;
        lead.SetLocationAnalysis();
        if (lead.LocationAnalysis is null) throw new ConflictException("A preferred location is required before analysis can be updated.");
        if (!hadLocationAnalysis) await leads.AddLocationAnalysisAsync(lead.LocationAnalysis, cancellationToken);
        lead.LocationAnalysis.Update(request.PreferredLocation, request.Notes);
        await leads.SaveChangesAsync(cancellationToken);
        return ToLocation(lead.LocationAnalysis);
    }

    public async Task<LocationEvaluationResponse> EvaluateLocationAnalysisAsync(Guid leadId, LocationEvaluationRequest request, CancellationToken cancellationToken)
    {
        EnsureMarketingWriteAccess();
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        EnsureVersion(lead, request.ExpectedVersion);
        var hadLocationAnalysis = lead.LocationAnalysis is not null;
        lead.SetLocationAnalysis();
        if (lead.LocationAnalysis is null) throw new ConflictException("A preferred location is required before analysis can be evaluated.");
        if (!hadLocationAnalysis) await leads.AddLocationAnalysisAsync(lead.LocationAnalysis, cancellationToken);
        lead.LocationAnalysis.Evaluate(request.Decision, request.Notes, currentUser.UserId);
        await leads.SaveChangesAsync(cancellationToken);
        return new LocationEvaluationResponse(lead.Id, lead.LocationAnalysis.Status, lead.LocationAnalysis.Notes, lead.LocationAnalysis.EvaluatedAt ?? lead.LocationAnalysis.UpdatedAt, lead.State);
    }

    public async Task<IReadOnlyList<ActivityResponse>> GetActivitiesAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await GetOwnedLeadAsync(leadId, cancellationToken);
        var actorNames = (await users.ListAsync(currentUser.OrganizationId, cancellationToken))
            .ToDictionary(x => x.Id, x => x.DisplayName);
        return lead.Activities.OrderByDescending(x => x.CreatedAt)
            .Select(x => new ActivityResponse(x.Id, x.Type, x.Message, x.FromState, x.ToState, x.CreatedAt, actorNames.GetValueOrDefault(x.ActorId, "System"))).ToArray();
    }

    private async Task<Lead> GetOwnedLeadAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await leads.GetAsync(currentUser.OrganizationId, leadId, cancellationToken);
        if (lead is null) throw new NotFoundException("Lead not found.");
        if (!CanAccess(lead)) throw new ForbiddenException("You do not have access to this lead.");
        return lead;
    }

    private bool CanAccess(Lead lead) => currentUser.Role != UserRole.MarketingAgent || lead.AssignedAgentId == currentUser.UserId;

    private void EnsureLeadReadAccess()
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership))
            throw new ForbiddenException("This role cannot access the lead pipeline.");
    }

    private void EnsureMarketingWriteAccess()
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager))
            throw new ForbiddenException("Only an Agent, Marketing Admin, or General Manager can perform this action.");
    }

    private static LeadSummary ToSummary(Lead x, bool downPaymentSubmittedForFinance = false, string? assignedAgentName = null) => new(x.Id, x.FullName, x.ContactNumber, x.Email, x.SourceOfIncome, x.LeadSource, x.Age, x.PreferredLocation, x.ProductLine, x.State, x.AssignedAgentId, assignedAgentName ?? "Unknown agent", x.CreatedAt, x.UpdatedAt, x.Version, downPaymentSubmittedForFinance);

    private async Task<LeadDetails> ToDetailsAsync(Lead x, CancellationToken ct)
    {
        var assignedAgent = await users.GetAsync(currentUser.OrganizationId, x.AssignedAgentId, ct);
        var payment = x.State == LeadState.DownPaymentPending
            ? await processes.GetDownPaymentAsync(currentUser.OrganizationId, x.Id, ct)
            : null;
        return new(x.Id, x.FullName, x.Age, x.ContactNumber, x.Email, x.SourceOfIncome, x.LeadSource, x.Address, x.Industry, x.MeetingDateTime, x.QuestionsConcerns, x.PreferredLocation, x.ProductLine, x.ListPrice, x.ActualPrice, x.WelcomeEmailReceived, x.GoodTimeToDiscuss, x.LastCallOutcome, x.State, x.AssignedAgentId, assignedAgent?.DisplayName ?? "Unknown agent", x.CreatedAt, x.UpdatedAt, x.Version, x.LocationAnalysis is not null && x.LocationAnalysis.Status == "PENDING", payment?.SubmittedAt is not null);
    }
    private static NurturingResponse ToNurturing(Lead x) => new(x.Id, x.NurturingNotes, x.LastContactedAt, x.WelcomeEmailReceived, x.GoodTimeToDiscuss, x.LastCallOutcome, x.ProductLine, x.ListPrice, x.ActualPrice, x.UpdatedAt, x.Version);
    private static decimal PriceFor(ProductLine productLine, PricingSettingsResponse pricing) => productLine switch { ProductLine.Abc => pricing.AbcPrice, ProductLine.Pharmacy => pricing.PharmacyPrice, _ => pricing.ComboPrice };
    private async Task EnsureValidAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await users.GetAsync(currentUser.OrganizationId, agentId, cancellationToken);
        if (agent is null || !agent.IsActive || agent.Role != UserRole.MarketingAgent) throw new ValidationException("Assigned agent must be an active Marketing Agent in this organization.");
    }
    private static LocationAnalysisResponse ToLocation(LocationAnalysis x) => new(x.LeadId, x.PreferredLocation, x.Status, x.Notes, x.UpdatedAt);
    private static void EnsureVersion(Lead lead, int? expectedVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != lead.Version)
            throw new ConflictException("The lead was changed by another user. Refresh and try again.");
    }
}
