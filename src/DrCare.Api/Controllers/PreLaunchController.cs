using DrCare.Application.Contracts;
using DrCare.Application.Processes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/leads/{leadId:guid}/pre-launch"), Authorize, EnableRateLimiting("api")]
public sealed class PreLaunchController(IPreLaunchService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PreLaunchResponse>> Get(Guid leadId, CancellationToken ct) => Ok(await service.GetAsync(leadId, ct));
    [HttpGet("items")]
    public async Task<ActionResult<PreLaunchResponse>> Items(Guid leadId, CancellationToken ct) => Ok(await service.GetAsync(leadId, ct));
    [HttpPost("initialize")]
    public async Task<ActionResult<PreLaunchResponse>> Initialize(Guid leadId, InitializePreLaunchRequest request, CancellationToken ct) => Ok(await service.InitializeAsync(leadId, request, ct));
    [HttpPatch("items/{itemId:guid}")]
    public async Task<ActionResult<PreLaunchResponse>> UpdateItem(Guid leadId, Guid itemId, UpdatePreLaunchItemRequest request, CancellationToken ct) => Ok(await service.UpdateItemAsync(leadId, itemId, request, ct));
    [HttpPost("send-video")]
    public async Task<ActionResult<SendVideoResponse>> SendVideo(Guid leadId, SendVideoRequest request, CancellationToken ct) => Ok(await service.SendVideoAsync(leadId, request, ct));
    [HttpPost("complete")]
    public async Task<ActionResult<CompletePreLaunchResponse>> Complete(Guid leadId, CompletePreLaunchRequest request, CancellationToken ct) => Ok(await service.CompleteAsync(leadId, request, ct));
}
