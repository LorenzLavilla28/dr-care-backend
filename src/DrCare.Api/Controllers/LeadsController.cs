using DrCare.Application.Leads;
using DrCare.Application.Contracts;
using DrCare.Application.Tasks;
using DrCare.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController]
[Route("api/v1/leads")]
[Authorize]
[EnableRateLimiting("api")]
public sealed class LeadsController(ILeadService leadService, ITaskService taskService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LeadPage>> Search([FromQuery] LeadState? state, [FromQuery] ProductLine? productLine, [FromQuery] Guid? assignedAgentId, [FromQuery] DateTimeOffset? createdFrom, [FromQuery] DateTimeOffset? createdTo, [FromQuery] string? search, [FromQuery] string? cursor, [FromQuery] int page = 1, [FromQuery] int limit = 25, [FromQuery] string sort = "updatedAt", CancellationToken cancellationToken = default) =>
        Ok(await leadService.SearchAsync(state, productLine, assignedAgentId, createdFrom, createdTo, search, cursor, page, limit, sort, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<LeadDetails>> Create(CreateLeadRequest request, CancellationToken cancellationToken)
    {
        var result = await leadService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { leadId = result.Id }, result);
    }

    [HttpGet("{leadId:guid}")]
    public async Task<ActionResult<LeadDetails>> GetById(Guid leadId, CancellationToken cancellationToken)
    {
        var result = await leadService.GetAsync(leadId, cancellationToken);
        Response.Headers.ETag = $"\"{result.Version}\"";
        return Ok(result);
    }

    [HttpPost("{leadId:guid}/inquiry/start")]
    public async Task<ActionResult<LeadDetails>> StartInquiry(Guid leadId, CancellationToken cancellationToken) =>
        Ok(await leadService.StartInquiryAsync(leadId, cancellationToken));

    [HttpPatch("{leadId:guid}/inquiry")]
    public async Task<ActionResult<LeadDetails>> UpdateInquiry(Guid leadId, UpdateInquiryRequest request, CancellationToken cancellationToken) =>
        Ok(await leadService.UpdateInquiryAsync(leadId, request, cancellationToken));

    [HttpPost("{leadId:guid}/inquiry/submit")]
    public async Task<ActionResult<SubmitInquiryResponse>> SubmitInquiry(Guid leadId, CancellationToken cancellationToken) =>
        Ok(await leadService.SubmitInquiryAsync(leadId, cancellationToken));

    [HttpGet("{leadId:guid}/activities")]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> GetActivities(Guid leadId, CancellationToken cancellationToken) =>
        Ok(await leadService.GetActivitiesAsync(leadId, cancellationToken));

    [HttpGet("{leadId:guid}/activity")]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> GetActivityAlias(Guid leadId, CancellationToken cancellationToken) =>
        Ok(await leadService.GetActivitiesAsync(leadId, cancellationToken));

    [HttpGet("{leadId:guid}/inquiry")]
    public async Task<ActionResult<LeadDetails>> GetInquiry(Guid leadId, CancellationToken cancellationToken) => Ok(await leadService.GetInquiryAsync(leadId, cancellationToken));

    [HttpGet("{leadId:guid}/nurturing")]
    public async Task<ActionResult<NurturingResponse>> GetNurturing(Guid leadId, CancellationToken cancellationToken) => Ok(await leadService.GetNurturingAsync(leadId, cancellationToken));

    [HttpPatch("{leadId:guid}/nurturing")]
    public async Task<ActionResult<NurturingResponse>> UpdateNurturing(Guid leadId, UpdateNurturingRequest request, CancellationToken cancellationToken) => Ok(await leadService.UpdateNurturingAsync(leadId, request, cancellationToken));

    [HttpPost("{leadId:guid}/nurturing/call-outcome")]
    public async Task<ActionResult<CallOutcomeResponse>> RecordCallOutcome(Guid leadId, CallOutcomeRequest request, CancellationToken cancellationToken) => Ok(await leadService.RecordCallOutcomeAsync(leadId, request, cancellationToken));

    [HttpPost("{leadId:guid}/qualification")]
    public async Task<ActionResult<QualificationResponse>> Qualify(Guid leadId, QualificationRequest request, CancellationToken cancellationToken) => Ok(await leadService.QualifyAsync(leadId, request, cancellationToken));

    [HttpPost("{leadId:guid}/activities")]
    public async Task<ActionResult<ActivityResponse>> AddActivity(Guid leadId, AddActivityRequest request, CancellationToken cancellationToken) => Ok(await leadService.AddActivityAsync(leadId, request, cancellationToken));

    [HttpPost("{leadId:guid}/assign")]
    public async Task<ActionResult<LeadDetails>> Assign(Guid leadId, AssignLeadRequest request, CancellationToken cancellationToken) => Ok(await leadService.AssignAsync(leadId, request, cancellationToken));

    [HttpGet("{leadId:guid}/location-analysis")]
    public async Task<ActionResult<LocationAnalysisResponse>> GetLocationAnalysis(Guid leadId, CancellationToken cancellationToken) => Ok(await leadService.GetLocationAnalysisAsync(leadId, cancellationToken));

    [HttpPut("{leadId:guid}/location-analysis")]
    public async Task<ActionResult<LocationAnalysisResponse>> UpdateLocationAnalysis(Guid leadId, UpdateLocationAnalysisRequest request, CancellationToken cancellationToken) => Ok(await leadService.UpdateLocationAnalysisAsync(leadId, request, cancellationToken));

    [HttpPost("{leadId:guid}/location-analysis/evaluate")]
    public async Task<ActionResult<LocationEvaluationResponse>> EvaluateLocationAnalysis(Guid leadId, LocationEvaluationRequest request, CancellationToken cancellationToken) => Ok(await leadService.EvaluateLocationAnalysisAsync(leadId, request, cancellationToken));

    [HttpGet("{leadId:guid}/tasks")]
    public async Task<ActionResult<IReadOnlyList<TaskResponse>>> GetTasks(Guid leadId, CancellationToken cancellationToken) => Ok(await taskService.ListAsync(leadId, cancellationToken));

    [HttpPost("{leadId:guid}/tasks")]
    public async Task<ActionResult<TaskResponse>> CreateTask(Guid leadId, CreateTaskRequest request, CancellationToken cancellationToken) => Ok(await taskService.CreateAsync(request, leadId, cancellationToken));
}
