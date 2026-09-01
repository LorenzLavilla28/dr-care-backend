using DrCare.Application.Auth;
using DrCare.Application.Contracts;
using DrCare.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DrCare.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IAuthService authService, IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    private const string DefaultRefreshCookieName = "dr-care-refresh";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        SetRefreshCookie(response.RefreshToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshCookieName] ?? request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedException("Your session has expired.");
        var response = await authService.RefreshAsync(new RefreshTokenRequest(refreshToken), cancellationToken);
        SetRefreshCookie(response.RefreshToken);
        return Ok(response);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<MessageResponse>> Logout([FromBody] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(Request.Cookies[RefreshCookieName] ?? request?.RefreshToken, cancellationToken);
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/api/v1/auth" });
        return Ok(new MessageResponse("Signed out."));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<MessageResponse>> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await authService.ForgotPasswordAsync(request, cancellationToken);
        return Accepted(new MessageResponse("If the account exists, password reset instructions will be sent."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<MessageResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await authService.ResetPasswordAsync(request, cancellationToken);
        return Ok(new MessageResponse("Password reset."));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserProfile> Me() => Ok(authService.GetCurrentUser());

    private string RefreshCookieName => configuration["Auth:RefreshCookieName"] ?? DefaultRefreshCookieName;

    private void SetRefreshCookie(string value)
    {
        var sameSite = Enum.TryParse<SameSiteMode>(configuration["Auth:RefreshCookieSameSite"], true, out var configuredSameSite)
            ? configuredSameSite
            : SameSiteMode.Lax;
        var lifetimeDays = int.TryParse(configuration["Auth:RefreshCookieLifetimeDays"], out var configuredLifetimeDays)
            ? Math.Clamp(configuredLifetimeDays, 1, 30)
            : 14;

        Response.Cookies.Append(RefreshCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || Request.IsHttps,
            SameSite = sameSite,
            Path = "/api/v1/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(lifetimeDays),
            MaxAge = TimeSpan.FromDays(lifetimeDays)
        });
    }
}
