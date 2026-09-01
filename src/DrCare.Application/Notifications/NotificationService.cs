using DrCare.Domain;

namespace DrCare.Application.Notifications;

public interface INotificationService
{
    Task NotifyRoleAsync(Guid organizationId, UserRole role, string type, string title, string message, CancellationToken cancellationToken);
    Task NotifyUserAsync(Guid organizationId, Guid userId, string type, string title, string message, CancellationToken cancellationToken);
}

public sealed class NotificationService(IUserRepository users, IProcessRepository processes) : INotificationService
{
    public async Task NotifyRoleAsync(Guid organizationId, UserRole role, string type, string title, string message, CancellationToken cancellationToken)
    {
        var recipients = await users.ListAsync(organizationId, cancellationToken);
        foreach (var recipient in recipients.Where(x => x.IsActive && x.Role == role))
            await processes.AddNotificationAsync(new Notification(organizationId, recipient.Id, type, title, message), cancellationToken);
    }

    public async Task NotifyUserAsync(Guid organizationId, Guid userId, string type, string title, string message, CancellationToken cancellationToken)
    {
        var recipient = await users.GetAsync(organizationId, userId, cancellationToken);
        if (recipient is { IsActive: true })
            await processes.AddNotificationAsync(new Notification(organizationId, recipient.Id, type, title, message), cancellationToken);
    }
}
