namespace DrCare.Domain;

public enum UserRole
{
    MarketingAgent,
    MarketingAdmin,
    GeneralManager,
    Finance,
    AdminTeam,
    Leadership
}

public enum LeadState
{
    New,
    Inquiry,
    InquiryIncomplete,
    Nurturing,
    FollowUp,
    Qualified,
    DownPaymentPending,
    DownPaymentConfirmed,
    ContractDrafting,
    ContractReview,
    ContractSigned,
    PreLaunch,
    EndorsedToAdmin
}

public enum ProductLine
{
    Abc,
    Pharmacy,
    Combo
}

public enum ActivityType
{
    LeadCreated,
    StateChanged,
    InquiryUpdated,
    FollowUpCreated,
    WelcomeEmailQueued,
    NoteAdded,
    Call,
    ZoomMeeting,
    OfficeVisit,
    DocumentUploaded,
    NurturingUpdated,
    CallOutcomeRecorded,
    InvoiceGenerated,
    DownPaymentSubmittedForFinance,
    PaymentConfirmed,
    ContractGenerated,
    ContractSubmitted,
    ContractApproved,
    ContractRevisionRequested,
    ContractSigningRequested,
    ContractSigned,
    PreLaunchInitialized,
    PreLaunchUpdated,
    PreLaunchCompleted,
    EndorsementCreated,
    EndorsementAcknowledged,
    DocumentArchived,
    LeadAssigned,
    LocationAnalysisUpdated,
    LocationAnalysisSubmitted,
    LocationAnalysisApproved,
    LocationAnalysisReturned
}

public enum TaskStatus
{
    Open,
    Completed,
    Cancelled
}

public enum DownPaymentStatus
{
    Pending,
    Invoiced,
    Confirmed
}

public enum DocumentStatus
{
    UploadPending,
    Uploaded,
    Archived
}

public enum ContractStatus
{
    Draft,
    InReview,
    RevisionRequested,
    Approved,
    Signed
}

public enum EndorsementStatus
{
    Pending,
    Acknowledged
}

public enum SigningRequestStatus
{
    Pending,
    Viewed,
    Signed,
    Declined,
    Voided,
    Expired
}

public enum EmailDeliveryStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    Cancelled
}
