using DrCare.Application.Contracts;
using DrCare.Application.Processes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/leads/{leadId:guid}/contract/signing-requests"), Authorize, EnableRateLimiting("api")]
public sealed class ContractSigningController(IContractSigningService service) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<SigningRequestResponse>>> List(Guid leadId, CancellationToken ct) => Ok(await service.ListAsync(leadId, ct));
    [HttpPost] public async Task<ActionResult<SigningRequestResponse>> Create(Guid leadId, CreateSigningRequest request, CancellationToken ct) => Ok(await service.CreateAsync(leadId, request, ct));
    [HttpPost("{requestId:guid}/void")] public async Task<ActionResult<SigningRequestResponse>> Void(Guid leadId, Guid requestId, CancellationToken ct) => Ok(await service.VoidAsync(leadId, requestId, ct));
}

[ApiController, Route("api/v1/public/contract-signing"), AllowAnonymous, EnableRateLimiting("signing")]
public sealed class PublicContractSigningController(IContractSigningService service) : ControllerBase
{
    [HttpGet("{token}")] public async Task<ActionResult<PublicSigningResponse>> Get(string token, CancellationToken ct) => Ok(await service.GetPublicAsync(token, ct));
    [HttpPost("{token}/sign")] public async Task<ActionResult<PublicSigningResponse>> Sign(string token, SignContractRequest request, CancellationToken ct) => Ok(await service.SignPublicAsync(token, request, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct));
    [HttpPost("{token}/decline")] public async Task<IActionResult> Decline(string token, DeclineSigningRequest request, CancellationToken ct) { await service.DeclinePublicAsync(token, request, ct); return NoContent(); }
}
