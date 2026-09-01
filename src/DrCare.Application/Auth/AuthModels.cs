using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DrCare.Domain;
using DrCare.Application.Contracts;

namespace DrCare.Application.Auth;

public sealed record LoginRequest(
    [param: Required, EmailAddress, MaxLength(254)] string Email,
    [param: Required, MinLength(12), MaxLength(128)] string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, [property: JsonIgnore] string RefreshToken, UserProfile User);
public sealed record MessageResponse(string Message);

public sealed record UserProfile(Guid Id, Guid OrganizationId, string Email, string DisplayName, UserRole Role);

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
    UserProfile GetCurrentUser();
}

public sealed class AuthService(IUserRepository users, IAuthTokenRepository authTokens, IPasswordService passwords, ITokenService tokens, ICurrentUser currentUser, IEmailQueue emailQueue, IEmailComposer emailComposer, IApplicationLinks links) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || !user.IsActive || !passwords.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid email or password.");

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var stored = await authTokens.FindRefreshTokenAsync(HashToken(request.RefreshToken), cancellationToken);
        if (stored is null || !stored.IsActive) throw new UnauthorizedException("Invalid refresh token.");
        var user = await users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive || user.OrganizationId != stored.OrganizationId) throw new UnauthorizedException("Invalid refresh token.");
        stored.Revoke();
        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var stored = await authTokens.FindRefreshTokenAsync(HashToken(refreshToken), cancellationToken);
            if (stored is not null && stored.OrganizationId == currentUser.OrganizationId) stored.Revoke();
        }
        await authTokens.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        if (user is not null && user.IsActive)
        {
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            var reset = new PasswordResetToken(user.Id, HashToken(rawToken), DateTimeOffset.UtcNow.AddMinutes(30));
            await authTokens.AddPasswordResetTokenAsync(reset, cancellationToken);
            var url = links.PasswordReset(rawToken, user.Email);
            var content = emailComposer.Compose(new BrandedEmailRequest(null, "Reset your Dr. Care password.", "Reset your password", $"Hello {user.DisplayName},",
                ["We received a request to reset your Dr. Care account password.", "This secure link expires in 30 minutes. If you did not request this, you can safely ignore this email."], "Reset password", url));
            await emailQueue.EnqueueAsync(new QueueEmailRequest(user.OrganizationId, user.Email, user.DisplayName, "Reset your Dr. Care password", content.Html, content.Text,
                "password-reset", IdempotencyKey: $"password-reset:{reset.Id}"), cancellationToken);
        }
        await authTokens.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var reset = await authTokens.FindPasswordResetTokenAsync(HashToken(request.Token), cancellationToken);
        if (reset is null || !reset.IsActive) throw new ValidationException("The password reset token is invalid or expired.");
        var user = await users.GetByIdAsync(reset.UserId, cancellationToken);
        if (user is null || !user.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase)) throw new ValidationException("The password reset token is invalid or expired.");
        user.ChangePassword(passwords.Hash(request.NewPassword));
        reset.Use();
        await authTokens.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);
        await authTokens.SaveChangesAsync(cancellationToken);
    }

    public UserProfile GetCurrentUser()
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedException("Authentication is required.");
        return new UserProfile(currentUser.UserId, currentUser.OrganizationId, currentUser.Email, currentUser.DisplayName, currentUser.Role);
    }

    private static UserProfile ToProfile(User user) => new(user.Id, user.OrganizationId, user.Email, user.DisplayName, user.Role);
    private async Task<LoginResponse> IssueTokensAsync(User user, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        await authTokens.AddRefreshTokenAsync(new RefreshToken(user.Id, user.OrganizationId, HashToken(raw), DateTimeOffset.UtcNow.AddDays(14)), ct);
        await authTokens.SaveChangesAsync(ct);
        return new LoginResponse(access.Value, access.ExpiresAt, raw, ToProfile(user));
    }
    private static string HashToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
