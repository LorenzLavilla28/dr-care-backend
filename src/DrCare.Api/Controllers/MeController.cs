using DrCare.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrCare.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(IAuthService authService) : ControllerBase
{
    [HttpGet]
    public ActionResult<UserProfile> Get() => Ok(authService.GetCurrentUser());
}
