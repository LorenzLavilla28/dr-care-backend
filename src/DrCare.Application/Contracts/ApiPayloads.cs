using System.ComponentModel.DataAnnotations;
using DrCare.Domain;

namespace DrCare.Application.Contracts;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record RefreshTokenRequest([param: Required, MinLength(32), MaxLength(4096)] string RefreshToken);
public sealed record RefreshTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);
public sealed record ForgotPasswordRequest([param: Required, EmailAddress, MaxLength(254)] string Email);
public sealed record ResetPasswordRequest(
    [param: Required, MaxLength(4096)] string Token,
    [param: Required, EmailAddress, MaxLength(254)] string Email,
    [param: Required, MinLength(12), MaxLength(128)] string NewPassword);

public sealed record UserListItem(Guid Id, string Email, string DisplayName, UserRole Role, bool IsActive, DateTimeOffset CreatedAt);
public sealed record CreateUserRequest(
    [param: Required, EmailAddress, MaxLength(254)] string Email,
    [param: Required, MinLength(2), MaxLength(160)] string DisplayName,
    UserRole Role,
    [param: Required, MinLength(12), MaxLength(128)] string TemporaryPassword);
public sealed record UpdateUserRequest(
    [param: MinLength(2), MaxLength(160)] string? DisplayName = null,
    UserRole? Role = null);
public sealed record DeactivateUserResponse(Guid UserId, bool IsActive, DateTimeOffset ChangedAt);

public sealed record ReferenceDataItem(string Code, string Name, bool Active = true);
public sealed record ChecklistTemplateItem(string Code, string Name, bool Required, string? AppliesTo = null);

public sealed record AssignLeadRequest([param: Required] Guid AssignedAgentId, int? ExpectedVersion = null);
public sealed record LocationAnalysisAnswer(
    [param: Required, MaxLength(40)] string QuestionCode,
    [param: Required, RegularExpression("^(YES_COMPLETELY|YES_PARTIALLY|NO|DONT_KNOW)$")] string Response,
    [param: MaxLength(1000)] string? Remark = null);
public sealed record LocationAnalysisQuestion(string Code, string Group, string Prompt, string? Hint);
public sealed record LocationAnalysisResponse(
    Guid LeadId,
    string CandidateName,
    string PreferredLocation,
    string LeaseOwnershipStatus,
    string Status,
    string? Notes,
    string? ReviewNotes,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<LocationAnalysisAnswer> Answers,
    int QuestionCount,
    int AnsweredCount,
    DateTimeOffset? SubmittedAt,
    string? SubmittedByName,
    DateTimeOffset? EvaluatedAt,
    string? EvaluatedByName,
    string? RevisionReason,
    IReadOnlyList<LocationAnalysisQuestion> Questions);
public sealed record UpdateLocationAnalysisRequest(
    [param: Required, MinLength(2), MaxLength(240)] string PreferredLocation,
    [param: MaxLength(40)] string? LeaseOwnershipStatus = null,
    [param: MaxLength(31)] IReadOnlyList<LocationAnalysisAnswer>? Answers = null,
    [param: MaxLength(2000)] string? Notes = null,
    int? ExpectedVersion = null);
public sealed record SubmitLocationAnalysisRequest(int? ExpectedVersion = null);
public sealed record LocationEvaluationRequest([param: Required, RegularExpression("^(Pending|Passed|Failed|Approved|Returned)$")] string Decision, [param: MaxLength(2000)] string? Notes = null, int? ExpectedVersion = null);
public sealed record LocationEvaluationResponse(Guid LeadId, string Decision, string? Notes, DateTimeOffset EvaluatedAt, LeadState State);

public sealed record AddActivityRequest(
    [param: Required, MaxLength(40)] string ActivityType,
    [param: Required, MaxLength(4000)] string Notes,
    DateTimeOffset? OccurredAt = null);
public sealed record WelcomeEmailRequest([param: MaxLength(2000)] string? PersonalMessage = null, int? ExpectedVersion = null);
public sealed record UpdateNurturingRequest(
    [param: MaxLength(4000)] string? Notes = null,
    ProductLine? ProductLine = null,
    [param: Range(typeof(decimal), "0.01", "1000000000")] decimal? ActualPrice = null,
    int? ExpectedVersion = null);
public sealed record WelcomeEmailResponse(Guid LeadId, string Status, DateTimeOffset QueuedAt);
public sealed record CallOutcomeRequest(
    [param: Required, MaxLength(40)] string Outcome,
    [param: Required, MinLength(2), MaxLength(40)] string GoodTimeToDiscuss,
    [param: MaxLength(4000)] string? Notes = null,
    DateTimeOffset? FollowUpAt = null,
    int? ExpectedVersion = null);
public sealed record CallOutcomeResponse(Guid LeadId, string Outcome, DateTimeOffset RecordedAt, Guid? FollowUpTaskId);
public sealed record QualificationRequest(
    [param: Required, RegularExpression("^(qualified|follow_up)$")] string Decision,
    [param: MaxLength(4000)] string? Notes = null,
    DateTimeOffset? FollowUpAt = null,
    int? ExpectedVersion = null);
public sealed record QualificationResponse(Guid LeadId, LeadState State, bool Qualified, DateTimeOffset EvaluatedAt);

public sealed record TaskResponse(Guid Id, Guid LeadId, Guid AssignedTo, string Title, DrCare.Domain.TaskStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? DueAt);
public sealed record CompletedWorkItemResponse(Guid Id, Guid LeadId, string Title, string Detail, string Kind, DateTimeOffset CompletedAt);
public sealed record CreateTaskRequest(
    [param: Required, MinLength(2), MaxLength(240)] string Title,
    Guid? AssignedTo = null,
    DateTimeOffset? DueAt = null,
    Guid? LeadId = null);
public sealed record CompleteTaskRequest([param: MaxLength(1000)] string? CompletionNote = null, int? ExpectedVersion = null);
public sealed record SnoozeTaskRequest([param: Required] DateTimeOffset DueAt);
public sealed record NurturingResponse(Guid LeadId, string? Notes, DateTimeOffset? LastContactedAt, string WelcomeEmailStatus, DateTimeOffset? WelcomeEmailQueuedAt, DateTimeOffset? WelcomeEmailSentAt, string? GoodTimeToDiscuss, string? LastCallOutcome, ProductLine? ProductLine, decimal? ListPrice, decimal? ActualPrice, DateTimeOffset UpdatedAt, int Version);

public sealed record DownPaymentResponse(Guid LeadId, decimal Amount, string Currency, string Status, string? InvoiceNumber, DateTimeOffset? ConfirmedAt, bool DocumentsComplete, bool SubmittedForFinance, DateTimeOffset? SubmittedAt, Guid? SubmittedBy);
public sealed record CreateInvoiceRequest([param: Range(typeof(decimal), "0.01", "1000000000")] decimal? Amount = null, [param: MaxLength(3)] string Currency = "PHP", int? ExpectedVersion = null);
public sealed record SubmitDownPaymentRequest(int? ExpectedVersion = null);
public sealed record InvoiceResponse(Guid LeadId, string InvoiceNumber, decimal Amount, string Currency, string Status, DateTimeOffset IssuedAt, DateTimeOffset? DueAt, string? DownloadUrl = null);
public sealed record ConfirmDownPaymentRequest(
    [param: Required, MaxLength(120)] string ReferenceNumber,
    [param: Range(typeof(decimal), "0.01", "1000000000")] decimal Amount,
    [param: Required, MaxLength(3)] string Currency,
    DateTimeOffset PaidAt,
    int? ExpectedVersion = null);
public sealed record ConfirmDownPaymentResponse(Guid LeadId, string Status, DateTimeOffset ConfirmedAt, string ReferenceNumber);

public sealed record CreateUploadIntentRequest(
    [param: Required, MaxLength(60)] string DocumentType,
    [param: Required, MaxLength(160)] string FileName,
    [param: Required, MaxLength(120)] string ContentType,
    [param: Range(1, 25_000_000)] long SizeBytes);
public sealed record UploadIntentResponse(Guid DocumentId, string ObjectKey, string UploadUrl, DateTimeOffset ExpiresAt, string RequiredContentType, long MaxSizeBytes);
public sealed record DocumentDownloadResponse(Guid DocumentId, string DownloadUrl, DateTimeOffset ExpiresAt);
public sealed record CompleteUploadRequest(Guid DocumentId, [param: Required, MaxLength(128)] string ObjectKey, [param: Required, MaxLength(128)] string Sha256);
public sealed record CompleteUploadResponse(Guid DocumentId, string Status, DateTimeOffset CompletedAt);
public sealed record DocumentResponse(Guid Id, Guid LeadId, string DocumentType, string FileName, string ContentType, long SizeBytes, string Status, DateTimeOffset CreatedAt);

public sealed record GenerateContractRequest(
    [param: Required, MaxLength(80)] string TemplateCode,
    [param: MaxLength(32)] string Version = "1",
    int? ExpectedVersion = null);
public sealed record ContractResponse(Guid LeadId, Guid ContractId, string Status, string TemplateCode, string Version, DateTimeOffset UpdatedAt, string? DownloadUrl = null, bool GmApproved = false, string? FranchiseeSignerName = null, DateTimeOffset? FranchiseeSignedAt = null, string? DrCareSignerName = null, DateTimeOffset? DrCareSignedAt = null, string? RevisionReason = null, DateTimeOffset? RevisionRequestedAt = null, Guid? RevisionRequestedBy = null, string? RevisionRequestedByName = null);
public sealed record SubmitContractReviewRequest([param: MaxLength(4000)] string? Notes = null, int? ExpectedVersion = null);
public sealed record ContractChecklistItem(Guid Id, string Code, string Name, bool Required, bool Complete, string? Notes);
public sealed record ContractChecklistResponse(Guid LeadId, IReadOnlyList<ContractChecklistItem> Items, bool Complete, int? ExpectedVersion = null);
public sealed record ApproveContractRequest([param: Required, MaxLength(4000)] string Notes, int? ExpectedVersion = null);
public sealed record RevisionRequest([param: Required, MinLength(2), MaxLength(4000)] string Reason, int? ExpectedVersion = null);
public sealed record ContractSignatureRequest(
    [param: Required, MaxLength(40)] string SignerRole,
    [param: Required, MaxLength(160)] string SignerName,
    [param: Required, MaxLength(128)] string SignatureHash,
    DateTimeOffset SignedAt,
    int? ExpectedVersion = null);
public sealed record ContractSignatureResponse(Guid ContractId, string SignerRole, DateTimeOffset SignedAt, string Status);
public sealed record CreateSigningRequest(
    [param: Required, RegularExpression("^(franchisee|dr-care)$")] string SignerRole,
    [param: Required, MinLength(2), MaxLength(160)] string SignerName,
    [param: Required, EmailAddress, MaxLength(254)] string SignerEmail,
    [param: Range(1, 30)] int ExpiresInDays = 7);
public sealed record SigningRequestResponse(Guid Id, Guid LeadId, Guid ContractId, string SignerRole, string SignerName, string SignerEmail, string Status, DateTimeOffset ExpiresAt, DateTimeOffset? SignedAt, bool EmailQueued, string? SigningUrl = null);
public sealed record PublicSigningResponse(Guid RequestId, string SignerRole, string SignerName, string ContractStatus, DateTimeOffset ExpiresAt, bool AlreadySigned, string? DocumentUrl = null);
public sealed record SignContractRequest([param: Required, MinLength(2), MaxLength(160)] string SignerName, [param: Required] bool AcceptedTerms, [param: Required, MaxLength(400000)] string SignatureData);
public sealed record DeclineSigningRequest([param: MaxLength(1000)] string? Reason = null);

public sealed record PreLaunchResponse(Guid LeadId, string Status, ProductLine? ProductLine, IReadOnlyList<PreLaunchItemResponse> Items, DateTimeOffset UpdatedAt);
public sealed record PreLaunchItemResponse(Guid Id, string Code, string Name, bool Required, bool Complete, bool Paused, string? Notes, DateTimeOffset? CompletedAt);
public sealed record InitializePreLaunchRequest(
    bool NurseOrDoctorAvailable = false,
    bool ConfirmSignedContractReview = false);
public sealed record UpdatePreLaunchItemRequest(bool Complete, [param: MaxLength(2000)] string? Notes = null, int? ExpectedVersion = null);
public sealed record SendVideoRequest([param: Required, Url, MaxLength(2000)] string VideoUrl, [param: MaxLength(1000)] string? Message = null);
public sealed record SendVideoResponse(Guid LeadId, Guid QueueId, string Status, DateTimeOffset QueuedAt);
public sealed record CompletePreLaunchRequest([param: MaxLength(2000)] string? Notes = null, int? ExpectedVersion = null);
public sealed record CompletePreLaunchResponse(Guid LeadId, string Status, DateTimeOffset CompletedAt);

public sealed record CreateEndorsementRequest(
    [param: Required, MaxLength(160)] string ReceivingTeam,
    [param: Required, MaxLength(4000)] string HandoffNotes,
    IReadOnlyList<HandoffBoundaryItem>? Items = null,
    int? ExpectedVersion = null);
public sealed record HandoffBoundaryItem([param: Required, MaxLength(80)] string Code, [param: Required, MaxLength(160)] string Name, bool CompletedByMarketing, bool AdminActionRequired, [param: MaxLength(1000)] string? Notes = null);
public sealed record EndorsementResponse(Guid Id, Guid LeadId, string ReceivingTeam, string Status, string HandoffNotes, IReadOnlyList<HandoffBoundaryItem> Items, DateTimeOffset CreatedAt, DateTimeOffset? AcknowledgedAt);

public sealed record QueueItem(Guid LeadId, string FullName, LeadState State, DateTimeOffset UpdatedAt, int Version);
public sealed record FinanceWorkbenchQuery(
    [param: MaxLength(20)] string View = "action",
    [param: MaxLength(40)] string Status = "all",
    [param: MaxLength(200)] string? Search = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? OwnerId = null,
    [param: Range(1, 10_000)] int Page = 1,
    [param: Range(1, 100)] int PageSize = 20);
public sealed record FinancePaymentItem(
    Guid PaymentId,
    Guid LeadId,
    string LeadName,
    string? Location,
    LeadState LeadState,
    Guid OwnerId,
    string OwnerName,
    string? InvoiceNumber,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset ActivityAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ConfirmedAt,
    string? SubmittedByName,
    string? ConfirmedByName,
    string? ConfirmationReference,
    bool HasPaymentEvidence);
public sealed record FinancePaymentEvent(Guid Id, string Type, string Message, DateTimeOffset CreatedAt, string ActorName);
public sealed record FinancePaymentDetail(FinancePaymentItem Payment, IReadOnlyList<FinancePaymentEvent> Events, IReadOnlyList<DocumentResponse> EvidenceDocuments);
public sealed record FinanceWorkbenchResponse(
    IReadOnlyList<FinancePaymentItem> Items,
    int Page,
    int PageSize,
    int Total,
    int AwaitingCount,
    decimal PendingAmount,
    decimal ConfirmedThisMonth,
    int ExceptionCount);
public sealed record PipelineReport(IReadOnlyDictionary<string, int> Counts, DateTimeOffset GeneratedAt);
public sealed record ReportQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, Guid? AgentId = null);
public sealed record OverviewReport(int TotalLeads, IReadOnlyDictionary<string, int> ByState, decimal ConfirmedDownPayments, DateTimeOffset GeneratedAt);
public sealed record ConversionReport(IReadOnlyDictionary<string, decimal> Rates, DateTimeOffset From, DateTimeOffset To);
public sealed record GoalReport(int Year, int Target, int Achieved, decimal CompletionPercentage);
public sealed record LeaderboardItem(Guid AgentId, string AgentName, int Leads, int Qualified, int Endorsed, decimal ConfirmedRevenue = 0);
public sealed record DownPaymentReport(decimal TotalInvoiced, decimal TotalConfirmed, int PendingCount, DateTimeOffset From, DateTimeOffset To, decimal PendingAmount = 0);
public sealed record AnnualGoalSettingsResponse(int Year, int Target, DateTimeOffset UpdatedAt);
public sealed record UpdateAnnualGoalSettingsRequest([param: Range(1, 1000000)] int Target);
public sealed record NotificationResponse(Guid Id, string Type, string Title, string Message, bool Read, DateTimeOffset CreatedAt);
public sealed record AuditLogResponse(Guid Id, Guid? LeadId, Guid ActorId, string Action, string? FromState, string? ToState, DateTimeOffset CreatedAt);

public sealed record PricingSettingsResponse(decimal AbcPrice, decimal PharmacyPrice, decimal ComboPrice, string Currency, DateTimeOffset UpdatedAt);
public sealed record UpdatePricingSettingsRequest([param: Range(typeof(decimal), "0.01", "1000000000")] decimal AbcPrice, [param: Range(typeof(decimal), "0.01", "1000000000")] decimal PharmacyPrice, [param: Range(typeof(decimal), "0.01", "1000000000")] decimal ComboPrice, [param: Required, MaxLength(3)] string Currency);
public sealed record InvoiceSettingsResponse(decimal DefaultDownPayment, string Currency, int PaymentTermDays, DateTimeOffset UpdatedAt);
public sealed record UpdateInvoiceSettingsRequest([param: Range(typeof(decimal), "0.01", "1000000000")] decimal DefaultDownPayment, [param: Required, MaxLength(3)] string Currency, [param: Range(0, 365)] int PaymentTermDays);
public sealed record ContractTemplateResponse(Guid Id, string Code, string Name, string Version, bool Active, DateTimeOffset UpdatedAt);
public sealed record CreateContractTemplateRequest([param: Required, MaxLength(80)] string Code, [param: Required, MaxLength(160)] string Name, [param: Required, MaxLength(32)] string Version, [param: Required, MaxLength(512)] string ObjectKey);
public sealed record PreLaunchChecklistSettingsResponse(string ProductLine, IReadOnlyList<ChecklistTemplateItem> Items, DateTimeOffset UpdatedAt);
public sealed record UpdatePreLaunchChecklistSettingsRequest([param: Required] IReadOnlyList<ChecklistTemplateItem> Items);
