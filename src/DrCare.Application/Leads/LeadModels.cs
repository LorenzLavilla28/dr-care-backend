using System.ComponentModel.DataAnnotations;
using DrCare.Domain;

namespace DrCare.Application.Leads;

public sealed record CreateLeadRequest(
    [param: Required, MinLength(2), MaxLength(160)] string FullName,
    [param: Phone, MaxLength(40)] string? ContactNumber = null,
    [param: EmailAddress, MaxLength(254)] string? Email = null,
    [param: MaxLength(160)] string? SourceOfIncome = null,
    [param: Required, MinLength(2), MaxLength(120)] string LeadSource = "Manual",
    Guid? AssignedAgentId = null,
    ProductLine? ProductLine = null);

public sealed record UpdateInquiryRequest(
    [param: MinLength(2), MaxLength(160)] string? FullName = null,
    [param: Range(18, 120)] int? Age = null,
    [param: Phone, MaxLength(40)] string? ContactNumber = null,
    [param: EmailAddress, MaxLength(254)] string? Email = null,
    [param: MinLength(2), MaxLength(160)] string? SourceOfIncome = null,
    [param: MaxLength(240)] string? PreferredLocation = null,
    [param: MaxLength(500)] string? Address = null,
    [param: MaxLength(160)] string? Industry = null,
    DateTimeOffset? MeetingDateTime = null,
    [param: MaxLength(4000)] string? QuestionsConcerns = null,
    int? ExpectedVersion = null);

public sealed record LeadSummary(
    Guid Id,
    string FullName,
    string ContactNumber,
    string Email,
    string SourceOfIncome,
    string LeadSource,
    int? Age,
    string? PreferredLocation,
    ProductLine? ProductLine,
    LeadState State,
    Guid AssignedAgentId,
    string AssignedAgentName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool DownPaymentSubmittedForFinance);

public sealed record LeadDetails(
    Guid Id,
    string FullName,
    int? Age,
    string ContactNumber,
    string Email,
    string SourceOfIncome,
    string LeadSource,
    string? Address,
    string? Industry,
    DateTimeOffset? MeetingDateTime,
    string? QuestionsConcerns,
    string? PreferredLocation,
    ProductLine? ProductLine,
    decimal? ListPrice,
    decimal? ActualPrice,
    string? WelcomeEmailReceived,
    string? GoodTimeToDiscuss,
    string? LastCallOutcome,
    LeadState State,
    Guid AssignedAgentId,
    string AssignedAgentName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool LocationAnalysisPending,
    bool DownPaymentSubmittedForFinance);

public sealed record ActivityResponse(Guid Id, ActivityType Type, string Message, LeadState? FromState, LeadState? ToState, DateTimeOffset CreatedAt, string ActorName);

public sealed record SubmitInquiryResponse(LeadDetails Lead, bool AcceptedForNurturing, IReadOnlyList<string> MissingFields);

public sealed record LeadPage(IReadOnlyList<LeadSummary> Items, int Page, int PageSize, int Total);
