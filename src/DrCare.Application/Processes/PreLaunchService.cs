using DrCare.Application.Contracts;
using DrCare.Domain;
using DrCare.Application.Settings;

namespace DrCare.Application.Processes;

public interface IPreLaunchService
{
    Task<PreLaunchResponse> GetAsync(Guid leadId, CancellationToken ct);
    Task<PreLaunchResponse> InitializeAsync(Guid leadId, InitializePreLaunchRequest request, CancellationToken ct);
    Task<PreLaunchResponse> UpdateItemAsync(Guid leadId, Guid itemId, UpdatePreLaunchItemRequest request, CancellationToken ct);
    Task<SendVideoResponse> SendVideoAsync(Guid leadId, SendVideoRequest request, CancellationToken ct);
    Task<CompletePreLaunchResponse> CompleteAsync(Guid leadId, CompletePreLaunchRequest request, CancellationToken ct);
}

public sealed class PreLaunchService(IPreLaunchRepository checklists, ILeadRepository leads, ISettingsService settings, ICurrentUser currentUser) : IPreLaunchService
{
    public async Task<PreLaunchResponse> GetAsync(Guid leadId, CancellationToken ct)
    {
        await GetLeadAsync(leadId, ct);
        var checklist = await checklists.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Pre-launch checklist has not been initialized.");
        return ToResponse(checklist);
    }

    public async Task<PreLaunchResponse> InitializeAsync(Guid leadId, InitializePreLaunchRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite();
        var lead = await GetLeadAsync(leadId, ct);
        if (lead.State is not (LeadState.ContractSigned or LeadState.PreLaunch)) throw new ConflictException("Both contract signatures must be complete before pre-launch can begin.");
        if (!lead.ProductLine.HasValue) throw new ConflictException("A product line must be selected before pre-launch initialization.");
        var configured = await settings.GetChecklistsAsync(ct);
        var template = configured.FirstOrDefault(x => x.ProductLine.Equals(lead.ProductLine.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? configured.FirstOrDefault(x => x.ProductLine.Equals("ALL", StringComparison.OrdinalIgnoreCase));
        var items = template?.Items ?? ItemsFor(lead.ProductLine.Value).Select(x => new ChecklistTemplateItem(x.Code, x.Name, x.Required)).ToArray();
        var existing = await checklists.GetAsync(currentUser.OrganizationId, leadId, ct);
        if (existing is not null)
        {
            foreach (var item in items.Where(item => existing.Items.All(current => !current.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase))))
            {
                var paused = item.Code.Equals("ANIMAL_BITE_TRAINING", StringComparison.OrdinalIgnoreCase) && !request.NurseOrDoctorAvailable;
                existing.AddItem(new PreLaunchItem(existing.Id, item.Code, item.Name, item.Required && !paused, paused));
            }
            existing.Touch();
            if (lead.State == LeadState.ContractSigned) lead.MoveTo(LeadState.ContractSigned, LeadState.PreLaunch);
            await checklists.SaveChangesAsync(ct);
            return ToResponse(existing);
        }
        var checklist = new PreLaunchChecklist(currentUser.OrganizationId, leadId, lead.ProductLine.Value);
        foreach (var item in items)
        {
            var paused = item.Code.Equals("ANIMAL_BITE_TRAINING", StringComparison.OrdinalIgnoreCase) && !request.NurseOrDoctorAvailable;
            checklist.AddItem(new PreLaunchItem(checklist.Id, item.Code, item.Name, item.Required && !paused, paused));
        }
        lead.MoveTo(LeadState.ContractSigned, LeadState.PreLaunch);
        await checklists.AddAsync(checklist, ct);
        await AddAuditAsync(lead, ActivityType.PreLaunchInitialized, "Product-specific pre-launch checklist initialized.", ct);
        await checklists.SaveChangesAsync(ct); return ToResponse(checklist);
    }

    public async Task<PreLaunchResponse> UpdateItemAsync(Guid leadId, Guid itemId, UpdatePreLaunchItemRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite(); var lead = await GetLeadAsync(leadId, ct); if (lead.State != LeadState.PreLaunch) throw new ConflictException("Pre-launch items can only be updated after the checklist has started.");
        var checklist = await checklists.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Pre-launch checklist has not been initialized.");
        if (checklist.Status == "COMPLETED") throw new ConflictException("The completed pre-launch checklist is read-only.");
        var item = checklist.Items.SingleOrDefault(x => x.Id == itemId) ?? throw new NotFoundException("Pre-launch item not found.");
        try { item.Update(request.Complete, request.Notes); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        checklist.Touch(); await AddAuditAsync(lead, ActivityType.PreLaunchUpdated, $"Pre-launch item '{item.Code}' updated.", ct); await checklists.SaveChangesAsync(ct); return ToResponse(checklist);
    }

    public async Task<SendVideoResponse> SendVideoAsync(Guid leadId, SendVideoRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite(); await GetLeadAsync(leadId, ct);
        if (!Uri.TryCreate(request.VideoUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ValidationException("VideoUrl must be an absolute HTTP(S) URL.");
        return new SendVideoResponse(leadId, "QUEUED", DateTimeOffset.UtcNow);
    }

    public async Task<CompletePreLaunchResponse> CompleteAsync(Guid leadId, CompletePreLaunchRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite(); var lead = await GetLeadAsync(leadId, ct); if (lead.State != LeadState.PreLaunch) throw new ConflictException("Pre-launch can only be completed after both contract signatures are recorded.");
        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != lead.Version) throw new ConflictException("The lead was changed by another user. Refresh and try again.");
        var checklist = await checklists.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Pre-launch checklist has not been initialized.");
        try { checklist.Complete(); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        await AddAuditAsync(lead, ActivityType.PreLaunchCompleted, "Pre-launch checklist completed.", ct); await checklists.SaveChangesAsync(ct); return new CompletePreLaunchResponse(leadId, checklist.Status, checklist.UpdatedAt);
    }

    private async Task<Lead> GetLeadAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Lead not found.");
        if (currentUser.Role == UserRole.MarketingAgent && lead.AssignedAgentId != currentUser.UserId) throw new ForbiddenException("You do not have access to this lead.");
        return lead;
    }
    private void EnsureMarketingWrite() { if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin)) throw new ForbiddenException("Only marketing users can perform this action."); }
    private static IReadOnlyList<(string Code, string Name, bool Required)> ItemsFor(ProductLine productLine) =>
        productLine switch
        {
            ProductLine.Abc => [
                ("ABC_TRAINING", "ABC product training", true),
                ("ANIMAL_BITE_TRAINING", "Animal-bite training", true),
                ("SITE_READY", "Site readiness verified", true)],
            ProductLine.Pharmacy => [
                ("PHARMACY_TRAINING", "Pharmacy product training", true),
                ("SITE_READY", "Site readiness verified", true)],
            _ => [
                ("ABC_TRAINING", "ABC product training", true),
                ("ANIMAL_BITE_TRAINING", "Animal-bite training", true),
                ("PHARMACY_TRAINING", "Pharmacy product training", true),
                ("SITE_READY", "Site readiness verified", true)]
        };
    private async Task AddAuditAsync(Lead lead, ActivityType type, string message, CancellationToken ct)
    {
        var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, type, message);
        lead.AddActivity(activity); await leads.AddActivityAsync(activity, ct);
    }
    private static PreLaunchResponse ToResponse(PreLaunchChecklist x) => new(x.LeadId, x.Status, x.ProductLine, x.Items.Select(i => new PreLaunchItemResponse(i.Id, i.Code, i.Name, i.Required, i.Complete, i.Paused, i.Notes, i.CompletedAt)).ToArray(), x.UpdatedAt);
}
