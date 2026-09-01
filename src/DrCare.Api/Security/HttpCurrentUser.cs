using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DrCare.Application;
using DrCare.Domain;

namespace DrCare.Api.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
    public Guid UserId => Guid.TryParse(Principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
    public Guid OrganizationId => Guid.TryParse(Principal.FindFirstValue("org_id"), out var id) ? id : Guid.Empty;
    public UserRole Role => Enum.TryParse<UserRole>(Principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : throw new UnauthorizedAccessException();
    public string Email => Principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty;
    public string DisplayName => Principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}
