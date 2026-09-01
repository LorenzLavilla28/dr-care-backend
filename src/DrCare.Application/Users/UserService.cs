using DrCare.Application.Contracts;
using DrCare.Domain;

namespace DrCare.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken);
    Task<UserListItem> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserListItem> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserListItem> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<DeactivateUserResponse> DeactivateAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class UserService(IUserRepository users, IPasswordService passwords, ICurrentUser currentUser, IEmailQueue emailQueue, IEmailComposer emailComposer, IApplicationLinks links) : IUserService
{
    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureUserRead();
        return (await users.ListAsync(currentUser.OrganizationId, cancellationToken)).Select(ToItem).ToArray();
    }

    public async Task<UserListItem> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        EnsureUserWrite();
        var user = new User(currentUser.OrganizationId, request.Email, request.DisplayName, request.Role, passwords.Hash(request.TemporaryPassword));
        await users.AddAsync(user, cancellationToken);
        var content = emailComposer.Compose(new BrandedEmailRequest(null, "Your Dr. Care staff account is ready.", "Welcome to the Dr. Care workspace", $"Hello {user.DisplayName},",
            ["A staff account has been created for you.", "Sign in using the temporary password below, then change it immediately and keep it private."], "Open Dr. Care", links.Login(),
            [new("Email", user.Email), new("Temporary password", request.TemporaryPassword), new("Role", user.Role.ToString())]));
        await emailQueue.EnqueueAsync(new QueueEmailRequest(user.OrganizationId, user.Email, user.DisplayName, "Your Dr. Care staff account", content.Html, content.Text,
            "staff-activation", IdempotencyKey: $"user:{user.Id}:activation"), cancellationToken);
        return ToItem(user);
    }

    public async Task<UserListItem> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        EnsureUserRead();
        var user = await users.GetAsync(currentUser.OrganizationId, userId, cancellationToken) ?? throw new NotFoundException("User not found.");
        return ToItem(user);
    }

    public async Task<UserListItem> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        EnsureUserWrite();
        var user = await users.GetAsync(currentUser.OrganizationId, userId, cancellationToken) ?? throw new NotFoundException("User not found.");
        user.Update(request.DisplayName, request.Role);
        await users.SaveChangesAsync(cancellationToken);
        return ToItem(user);
    }

    public async Task<DeactivateUserResponse> DeactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        EnsureUserWrite();
        if (userId == currentUser.UserId) throw new ConflictException("You cannot deactivate your own account.");
        var user = await users.GetAsync(currentUser.OrganizationId, userId, cancellationToken) ?? throw new NotFoundException("User not found.");
        user.Deactivate();
        await users.SaveChangesAsync(cancellationToken);
        return new DeactivateUserResponse(user.Id, user.IsActive, DateTimeOffset.UtcNow);
    }

    private void EnsureUserRead()
    {
        if (currentUser.Role is not (UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Finance or UserRole.Leadership))
            throw new ForbiddenException("Only authorized roles can view users.");
    }
    private void EnsureUserWrite() { if (currentUser.Role != UserRole.MarketingAdmin) throw new ForbiddenException("Leadership is read-only; only the Marketing Admin can manage users."); }

    private static UserListItem ToItem(User user) => new(user.Id, user.Email, user.DisplayName, user.Role, user.IsActive, user.CreatedAt);
}
