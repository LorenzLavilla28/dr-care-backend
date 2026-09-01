using DrCare.Application.Contracts;
using DrCare.Application.Users;
using DrCare.Application.Tasks;
using DrCare.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/users"), Authorize, EnableRateLimiting("api")]
public sealed class PlannedUsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserListItem>>> List(CancellationToken ct) => Ok(await users.ListAsync(ct));
    [HttpPost]
    public async Task<ActionResult<UserListItem>> Create(CreateUserRequest request, CancellationToken ct) => Ok(await users.CreateAsync(request, ct));
    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<UserListItem>> Get(Guid userId, CancellationToken ct) => Ok(await users.GetAsync(userId, ct));
    [HttpPatch("{userId:guid}")]
    public async Task<ActionResult<UserListItem>> Update(Guid userId, UpdateUserRequest request, CancellationToken ct) => Ok(await users.UpdateAsync(userId, request, ct));
    [HttpPost("{userId:guid}/deactivate")]
    public async Task<ActionResult<DeactivateUserResponse>> Deactivate(Guid userId, CancellationToken ct) => Ok(await users.DeactivateAsync(userId, ct));
}

[ApiController, Route("api/v1/reference"), Authorize, EnableRateLimiting("api")]
public sealed class PlannedReferenceController : ControllerBase
{
    [HttpGet("pipeline-states")]
    public ActionResult<IReadOnlyList<ReferenceDataItem>> PipelineStates() => Ok(Enum.GetValues<LeadState>().Select(x => new ReferenceDataItem(x.ToString().ToUpperInvariant(), x.ToString())).ToArray());
    [HttpGet("product-lines")]
    public ActionResult<IReadOnlyList<ReferenceDataItem>> ProductLines() => Ok(new[] { new ReferenceDataItem("ABC", "ABC"), new ReferenceDataItem("PHARMACY", "Pharmacy"), new ReferenceDataItem("COMBO", "Combo") });
    [HttpGet("document-types")]
    public ActionResult<IReadOnlyList<ReferenceDataItem>> DocumentTypes() => Ok(new[] { new ReferenceDataItem("VALID_ID_SIGNATURES", "Valid ID + 3 specimen signatures"), new ReferenceDataItem("FLOOR_PLAN", "Floor plan"), new ReferenceDataItem("PERSPECTIVE", "Perspective"), new ReferenceDataItem("PAYMENT_RECEIPT", "Payment receipt or bank confirmation") });
    [HttpGet("task-types")]
    public ActionResult<IReadOnlyList<ReferenceDataItem>> TaskTypes() => Ok(new[] { new ReferenceDataItem("FOLLOW_UP", "Follow-up"), new ReferenceDataItem("CALL_BACK", "Call back"), new ReferenceDataItem("DOCUMENT_REVIEW", "Document review"), new ReferenceDataItem("CONTRACT_REVIEW", "Contract review") });
    [HttpGet("checklists/{productLine}")]
    public ActionResult<IReadOnlyList<ChecklistTemplateItem>> Checklists(string productLine) => Ok(new[] { new ChecklistTemplateItem($"{productLine.ToUpperInvariant()}_PRE_LAUNCH", $"{productLine} pre-launch", true, productLine.ToUpperInvariant()), new ChecklistTemplateItem("ANIMAL_BITE_TRAINING", "Animal bite training", false, "ABC") });
}

[ApiController, Route("api/v1/tasks"), Authorize, EnableRateLimiting("api")]
public sealed class PlannedTasksController(ITaskService tasks) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskResponse>>> List([FromQuery] Guid? leadId, CancellationToken ct) => Ok(await tasks.ListAsync(leadId, ct));
    [HttpGet("completed")]
    public async Task<ActionResult<PagedResponse<CompletedWorkItemResponse>>> Completed([FromQuery] int page = 1, CancellationToken ct = default) => Ok(await tasks.ListCompletedWorkAsync(page, ct));
    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request, CancellationToken ct) => Ok(await tasks.CreateAsync(request, null, ct));
    [HttpGet("{taskId:guid}")]
    public async Task<ActionResult<TaskResponse>> Get(Guid taskId, CancellationToken ct) => Ok(await tasks.GetAsync(taskId, ct));
    [HttpPatch("{taskId:guid}")]
    public async Task<ActionResult<TaskResponse>> Update(Guid taskId, CreateTaskRequest request, CancellationToken ct) => Ok(await tasks.UpdateAsync(taskId, request, ct));
    [HttpPost("{taskId:guid}/complete")]
    public async Task<ActionResult<TaskResponse>> Complete(Guid taskId, CompleteTaskRequest request, CancellationToken ct) => Ok(await tasks.CompleteAsync(taskId, request, ct));
    [HttpPost("{taskId:guid}/snooze")]
    public async Task<ActionResult<TaskResponse>> Snooze(Guid taskId, [FromBody] SnoozeTaskRequest request, CancellationToken ct) => Ok(await tasks.SnoozeAsync(taskId, request.DueAt, ct));
}
