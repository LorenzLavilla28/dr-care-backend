using DrCare.Application.Contracts;
using DrCare.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/settings"), Authorize, EnableRateLimiting("api")]
public sealed class SettingsController(ISettingsService service) : ControllerBase
{
    [HttpGet("annual-goal"), Authorize(Roles = "MarketingAdmin,GeneralManager,Leadership")]
    public async Task<ActionResult<AnnualGoalSettingsResponse>> GetAnnualGoal(CancellationToken ct) => Ok(await service.GetAnnualGoalAsync(ct));
    [HttpPatch("annual-goal"), Authorize(Roles = "MarketingAdmin")]
    public async Task<ActionResult<AnnualGoalSettingsResponse>> UpdateAnnualGoal(UpdateAnnualGoalSettingsRequest request, CancellationToken ct) => Ok(await service.UpdateAnnualGoalAsync(request, ct));
    [HttpGet("pricing"), Authorize(Roles = "MarketingAdmin,Leadership")]
    public async Task<ActionResult<PricingSettingsResponse>> GetPricing(CancellationToken ct) => Ok(await service.GetPricingAsync(ct));
    [HttpPatch("pricing"), Authorize(Roles = "MarketingAdmin")]
    public async Task<ActionResult<PricingSettingsResponse>> UpdatePricing(UpdatePricingSettingsRequest request, CancellationToken ct) => Ok(await service.UpdatePricingAsync(request, ct));
    [HttpGet("invoice"), Authorize(Roles = "MarketingAdmin,Leadership")]
    public async Task<ActionResult<InvoiceSettingsResponse>> GetInvoice(CancellationToken ct) => Ok(await service.GetInvoiceAsync(ct));
    [HttpPatch("invoice"), Authorize(Roles = "MarketingAdmin")]
    public async Task<ActionResult<InvoiceSettingsResponse>> UpdateInvoice(UpdateInvoiceSettingsRequest request, CancellationToken ct) => Ok(await service.UpdateInvoiceAsync(request, ct));
    [HttpGet("contract-templates"), Authorize(Roles = "MarketingAdmin,Leadership")]
    public async Task<ActionResult<IReadOnlyList<ContractTemplateResponse>>> GetContractTemplates(CancellationToken ct) => Ok(await service.GetTemplatesAsync(ct));
    [HttpPost("contract-templates"), Authorize(Roles = "MarketingAdmin")]
    public async Task<ActionResult<ContractTemplateResponse>> CreateContractTemplate(CreateContractTemplateRequest request, CancellationToken ct) => Ok(await service.CreateTemplateAsync(request, ct));
    [HttpGet("pre-launch-checklists"), Authorize(Roles = "MarketingAdmin,Leadership")]
    public async Task<ActionResult<IReadOnlyList<PreLaunchChecklistSettingsResponse>>> GetPreLaunchChecklists(CancellationToken ct) => Ok(await service.GetChecklistsAsync(ct));
    [HttpPatch("pre-launch-checklists"), Authorize(Roles = "MarketingAdmin")]
    public async Task<ActionResult<IReadOnlyList<PreLaunchChecklistSettingsResponse>>> UpdatePreLaunchChecklists(UpdatePreLaunchChecklistSettingsRequest request, CancellationToken ct) => Ok(await service.UpdateChecklistsAsync(request, ct));
}
