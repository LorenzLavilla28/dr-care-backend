using DrCare.Domain;

namespace DrCare.Application;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid OrganizationId { get; }
    UserRole Role { get; }
    string Email { get; }
    string DisplayName { get; }
    bool IsAuthenticated { get; }
}

public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Lead> Items, int Total)> SearchAsync(Guid organizationId, Guid? assignedAgentId, LeadState? state, ProductLine? productLine, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, string? search, int skip, int take, string sort, CancellationToken cancellationToken);
    Task AddAsync(Lead lead, CancellationToken cancellationToken);
    Task AddActivityAsync(ActivityLog activity, CancellationToken cancellationToken);
    Task AddTaskAsync(FollowUpTask task, CancellationToken cancellationToken);
    Task AddLocationAnalysisAsync(LocationAnalysis analysis, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ITaskRepository
{
    Task<FollowUpTask?> GetAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken);
    Task<FollowUpTask?> FindOpenByLeadAndTitleAsync(Guid organizationId, Guid leadId, string title, CancellationToken cancellationToken);
    Task<IReadOnlyList<FollowUpTask>> ListAsync(Guid organizationId, Guid? assignedTo, Guid? leadId, CancellationToken cancellationToken);
    Task AddAsync(FollowUpTask task, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> GetAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAuthTokenRepository
{
    Task<RefreshToken?> FindRefreshTokenAsync(string hash, CancellationToken cancellationToken);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken);
    Task<PasswordResetToken?> FindPasswordResetTokenAsync(string hash, CancellationToken cancellationToken);
    Task AddPasswordResetTokenAsync(PasswordResetToken token, CancellationToken cancellationToken);
    Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IWorkflowRepository
{
    Task<WorkflowRecord?> GetAsync(Guid organizationId, Guid recordId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowRecord>> ListAsync(Guid organizationId, string? operation, Guid? leadId, CancellationToken cancellationToken);
    Task AddAsync(WorkflowRecord record, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IProcessRepository
{
    Task<DownPayment?> GetDownPaymentAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DownPayment>> ListDownPaymentsAsync(Guid organizationId, IReadOnlyCollection<Guid> leadIds, CancellationToken cancellationToken);
    Task AddDownPaymentAsync(DownPayment payment, CancellationToken cancellationToken);
    Task<LeadDocument?> GetDocumentAsync(Guid organizationId, Guid leadId, Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LeadDocument>> ListDocumentsAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task AddDocumentAsync(LeadDocument document, CancellationToken cancellationToken);
    Task<Contract?> GetContractAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task AddContractAsync(Contract contract, CancellationToken cancellationToken);
    Task<ContractSigningRequest?> GetSigningRequestByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractSigningRequest>> ListSigningRequestsAsync(Guid organizationId, Guid contractId, CancellationToken cancellationToken);
    Task AddSigningRequestAsync(ContractSigningRequest request, CancellationToken cancellationToken);
    Task<Endorsement?> GetEndorsementAsync(Guid organizationId, Guid endorsementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Endorsement>> ListEndorsementsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task AddEndorsementAsync(Endorsement endorsement, CancellationToken cancellationToken);
    Task<IReadOnlyList<Notification>> ListNotificationsAsync(Guid organizationId, Guid recipientId, CancellationToken cancellationToken);
    Task<Notification?> GetNotificationAsync(Guid organizationId, Guid recipientId, Guid notificationId, CancellationToken cancellationToken);
    Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPreLaunchRepository
{
    Task<PreLaunchChecklist?> GetAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task AddAsync(PreLaunchChecklist checklist, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISettingsRepository
{
    Task<AppSetting?> GetAsync(Guid organizationId, string key, CancellationToken cancellationToken);
    Task AddAsync(AppSetting setting, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IReportingRepository
{
    Task<IReadOnlyList<Lead>> ListLeadsAsync(Guid organizationId, Guid? assignedAgentId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<IReadOnlyList<DownPayment>> ListPaymentsAsync(Guid organizationId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<IReadOnlyList<Endorsement>> ListEndorsementsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Contract>> ListContractsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityLog>> ListActivitiesAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken);
}

public interface IPrivateObjectStorage
{
    Task<string> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<string> CreateDownloadUrlAsync(string objectKey, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken);
    Task PutAsync(string objectKey, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
}

public interface IDocumentPdfRenderer
{
    Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken);
    Task<byte[]> StampSignatureAsync(Stream pdf, ReadOnlyMemory<byte> signaturePng, string signerRole, CancellationToken cancellationToken);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
