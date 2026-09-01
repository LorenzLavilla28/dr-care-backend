namespace DrCare.Domain;

public sealed class Lead
{
    private readonly List<ActivityLog> _activities = [];
    private readonly List<FollowUpTask> _tasks = [];

    private Lead() { }

    public Lead(Guid organizationId, string fullName, string contactNumber, string email, string sourceOfIncome, Guid assignedAgentId, string leadSource = "Manual")
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        FullName = fullName;
        ContactNumber = contactNumber;
        Email = email;
        SourceOfIncome = sourceOfIncome;
        LeadSource = leadSource.Trim();
        AssignedAgentId = assignedAgentId;
        State = LeadState.New;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public int? Age { get; private set; }
    public string ContactNumber { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string SourceOfIncome { get; private set; } = string.Empty;
    public string LeadSource { get; private set; } = "Manual";
    public string? Address { get; private set; }
    public string? Industry { get; private set; }
    public DateTimeOffset? MeetingDateTime { get; private set; }
    public string? QuestionsConcerns { get; private set; }
    public string? PreferredLocation { get; private set; }
    public ProductLine? ProductLine { get; private set; }
    public decimal? ListPrice { get; private set; }
    public decimal? ActualPrice { get; private set; }
    public Guid AssignedAgentId { get; private set; }
    public LeadState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }
    public string? NurturingNotes { get; private set; }
    public string? WelcomeEmailReceived { get; private set; }
    public string? GoodTimeToDiscuss { get; private set; }
    public string? LastCallOutcome { get; private set; }
    public DateTimeOffset? LastContactedAt { get; private set; }
    public LocationAnalysis? LocationAnalysis { get; private set; }
    public IReadOnlyCollection<ActivityLog> Activities => _activities;
    public IReadOnlyCollection<FollowUpTask> Tasks => _tasks;

    public void StartInquiry()
    {
        EnsureState(LeadState.New);
        TransitionTo(LeadState.Inquiry);
    }

    public void UpdateInquiry(string? fullName, int? age, string? contactNumber, string? email, string? sourceOfIncome, string? preferredLocation,
        string? address = null, string? industry = null, DateTimeOffset? meetingDateTime = null, string? questionsConcerns = null)
    {
        if (fullName is not null) FullName = fullName.Trim();
        if (age.HasValue) Age = age;
        if (contactNumber is not null) ContactNumber = contactNumber.Trim();
        if (email is not null) Email = email.Trim().ToLowerInvariant();
        if (sourceOfIncome is not null) SourceOfIncome = sourceOfIncome.Trim();
        if (preferredLocation is not null) PreferredLocation = preferredLocation.Trim();
        if (address is not null) Address = address.Trim();
        if (industry is not null) Industry = industry.Trim();
        if (meetingDateTime.HasValue) MeetingDateTime = meetingDateTime;
        if (questionsConcerns is not null) QuestionsConcerns = questionsConcerns.Trim();
        Touch();
    }

    public void SetProductLine(ProductLine productLine, decimal? listPrice = null, decimal? actualPrice = null)
    {
        ProductLine = productLine;
        if (listPrice.HasValue)
        {
            if (listPrice <= 0) throw new DomainRuleException("List price must be greater than zero.");
            if (actualPrice.HasValue && (actualPrice <= 0 || actualPrice > listPrice))
                throw new DomainRuleException("Actual price must be greater than zero and cannot exceed list price.");
            ListPrice = listPrice;
            ActualPrice = actualPrice ?? listPrice;
        }
        Touch();
    }

    public IReadOnlyList<string> MissingInquiryRequirements()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(FullName)) missing.Add("fullName");
        if (!Age.HasValue) missing.Add("age");
        if (string.IsNullOrWhiteSpace(ContactNumber)) missing.Add("contactNumber");
        if (string.IsNullOrWhiteSpace(Email)) missing.Add("email");
        if (string.IsNullOrWhiteSpace(SourceOfIncome)) missing.Add("sourceOfIncome");
        return missing;
    }

    public bool SubmitInquiry(out IReadOnlyList<string> missing)
    {
        EnsureState(LeadState.Inquiry, LeadState.InquiryIncomplete);
        missing = MissingInquiryRequirements();
        TransitionTo(missing.Count == 0 ? LeadState.Nurturing : LeadState.InquiryIncomplete);
        return missing.Count == 0;
    }

    public void SetLocationAnalysis()
    {
        if (LocationAnalysis is null && !string.IsNullOrWhiteSpace(PreferredLocation))
        {
            LocationAnalysis = new LocationAnalysis(Id, OrganizationId, PreferredLocation);
            Touch();
        }
    }

    public void AddActivity(ActivityLog activity) => _activities.Add(activity);
    public void AddTask(FollowUpTask task) => _tasks.Add(task);

    public void UpdateNurturing(string? notes)
    {
        NurturingNotes = notes?.Trim();
        LastContactedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void RecordCallOutcome(string outcome, string goodTimeToDiscuss, string? notes)
    {
        EnsureState(LeadState.Nurturing, LeadState.FollowUp);
        LastCallOutcome = outcome.Trim();
        GoodTimeToDiscuss = goodTimeToDiscuss.Trim();
        NurturingNotes = notes?.Trim();
        LastContactedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void EvaluateQualification(string decision)
    {
        EnsureState(LeadState.Nurturing, LeadState.FollowUp);
        if (!string.Equals(decision, "qualified", StringComparison.OrdinalIgnoreCase) && !string.Equals(decision, "follow_up", StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("Qualification decision must be 'qualified' or 'follow_up'.");
        TransitionTo(string.Equals(decision, "qualified", StringComparison.OrdinalIgnoreCase) ? LeadState.Qualified : LeadState.FollowUp);
    }

    public void MarkDownPaymentPending()
    {
        if (State is not (LeadState.Qualified or LeadState.DownPaymentPending))
            throw new DomainRuleException("A lead must be qualified before an invoice can be generated.");
        if (State != LeadState.DownPaymentPending) TransitionTo(LeadState.DownPaymentPending);
    }

    public void AssignTo(Guid agentId)
    {
        if (agentId == Guid.Empty) throw new DomainRuleException("An assigned agent is required.");
        AssignedAgentId = agentId;
        Touch();
    }

    public void MoveTo(LeadState expectedCurrentState, LeadState nextState)
    {
        EnsureState(expectedCurrentState);
        TransitionTo(nextState);
    }

    private void TransitionTo(LeadState nextState)
    {
        State = nextState;
        Touch();
    }

    private void EnsureState(params LeadState[] allowed)
    {
        if (!allowed.Contains(State))
            throw new DomainRuleException($"Lead action is not valid while in state '{State}'.");
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}

public sealed class LocationAnalysis
{
    private LocationAnalysis() { }

    public LocationAnalysis(Guid leadId, Guid organizationId, string location)
    {
        Id = Guid.NewGuid();
        LeadId = leadId;
        OrganizationId = organizationId;
        PreferredLocation = location;
        Status = "Draft";
        LeaseOwnershipStatus = "";
        AssessmentJson = "[]";
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string PreferredLocation { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Draft";
    public string LeaseOwnershipStatus { get; private set; } = string.Empty;
    public string AssessmentJson { get; private set; } = "[]";
    public string? Notes { get; private set; }
    public string? ReviewNotes { get; private set; }
    public DateTimeOffset? EvaluatedAt { get; private set; }
    public Guid? EvaluatedBy { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public string? RevisionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string preferredLocation, string? notes)
    {
        UpdateAssessment(preferredLocation, LeaseOwnershipStatus, notes, AssessmentJson);
    }

    public void UpdateAssessment(string preferredLocation, string? leaseOwnershipStatus, string? notes, string assessmentJson)
    {
        if (Status is "Submitted" or "Approved" or "Passed")
            throw new DomainRuleException("A submitted or approved location analysis cannot be edited.");
        PreferredLocation = preferredLocation.Trim();
        LeaseOwnershipStatus = leaseOwnershipStatus?.Trim() ?? string.Empty;
        Notes = notes?.Trim();
        AssessmentJson = string.IsNullOrWhiteSpace(assessmentJson) ? "[]" : assessmentJson;
        if (Status is "Returned" or "Failed") Status = "Draft";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Submit(Guid submitterId)
    {
        if (Status is "Approved" or "Passed")
            throw new DomainRuleException("An approved location analysis cannot be resubmitted.");
        if (Status is "Submitted")
            throw new DomainRuleException("This location analysis is already submitted for review.");
        Status = "Submitted";
        SubmittedBy = submitterId;
        SubmittedAt = UpdatedAt = DateTimeOffset.UtcNow;
        ReviewNotes = null;
        RevisionReason = null;
    }

    public void Evaluate(string decision, string? notes, Guid evaluatorId)
    {
        if (Status is not "Submitted")
            throw new DomainRuleException("Location analysis must be submitted before review.");
        var normalized = decision.Trim();
        if (normalized is not ("Pending" or "Passed" or "Failed" or "Approved" or "Returned"))
            throw new DomainRuleException("Location decision must be Pending, Passed, Failed, Approved, or Returned.");
        Status = normalized switch
        {
            "Passed" or "Approved" => "Approved",
            "Failed" or "Returned" => "Returned",
            _ => "Draft",
        };
        ReviewNotes = notes?.Trim();
        RevisionReason = Status == "Returned" ? ReviewNotes : null;
        EvaluatedBy = evaluatorId;
        EvaluatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class ActivityLog
{
    private ActivityLog() { }

    public ActivityLog(Guid organizationId, Guid leadId, Guid actorId, ActivityType type, string message, LeadState? fromState = null, LeadState? toState = null)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        ActorId = actorId;
        Type = type;
        Message = message;
        FromState = fromState;
        ToState = toState;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid ActorId { get; private set; }
    public ActivityType Type { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public LeadState? FromState { get; private set; }
    public LeadState? ToState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class FollowUpTask
{
    private FollowUpTask() { }

    public FollowUpTask(Guid organizationId, Guid leadId, Guid assignedTo, string title, DateTimeOffset? dueAt = null)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        AssignedTo = assignedTo;
        Title = title;
        DueAt = dueAt;
        Status = TaskStatus.Open;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid AssignedTo { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public TaskStatus Status { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete() => Status = TaskStatus.Completed;
    public void Snooze(DateTimeOffset dueAt) { DueAt = dueAt; Status = TaskStatus.Open; }
    public void Update(string? title, Guid? assignedTo, DateTimeOffset? dueAt)
    {
        if (Status == TaskStatus.Completed) throw new DomainRuleException("Completed tasks cannot be edited.");
        if (title is not null) Title = title.Trim();
        if (assignedTo.HasValue && assignedTo.Value != Guid.Empty) AssignedTo = assignedTo.Value;
        if (dueAt.HasValue) DueAt = dueAt;
    }
}

public sealed class User
{
    private User() { }

    public User(Guid organizationId, string email, string displayName, UserRole role, string passwordHash)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        Role = role;
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string? displayName, UserRole? role)
    {
        if (displayName is not null) DisplayName = displayName.Trim();
        if (role.HasValue) Role = role.Value;
    }

    public void Deactivate() => IsActive = false;
    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;
}

public sealed class WorkflowRecord
{
    private WorkflowRecord() { }

    public WorkflowRecord(Guid organizationId, Guid actorId, string operation, Guid? leadId, string payloadJson)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        ActorId = actorId;
        Operation = operation;
        LeadId = leadId;
        PayloadJson = payloadJson;
        Status = "RECORDED";
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid? LeadId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public void MarkCompleted()
    {
        Status = "COMPLETED";
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}

public sealed class DownPayment
{
    private DownPayment() { }

    public DownPayment(Guid organizationId, Guid leadId, decimal amount, string currency)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Status = DownPaymentStatus.Pending;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "PHP";
    public string? InvoiceNumber { get; private set; }
    public string? InvoiceObjectKey { get; private set; }
    public string? InvoiceSha256 { get; private set; }
    public DateTimeOffset? InvoiceDueAt { get; private set; }
    public string? ConfirmationReference { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DownPaymentStatus Status { get; private set; }
    public Guid? ConfirmedBy { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void IssueInvoice(string invoiceNumber, string objectKey, string sha256, DateTimeOffset dueAt)
    {
        InvoiceNumber = invoiceNumber;
        InvoiceObjectKey = objectKey;
        InvoiceSha256 = sha256;
        InvoiceDueAt = dueAt;
        Status = DownPaymentStatus.Invoiced;
        Touch();
    }

    public void UpdateDetails(decimal amount, string currency)
    {
        if (Status == DownPaymentStatus.Confirmed) throw new DomainRuleException("A confirmed down payment cannot be edited.");
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Touch();
    }

    public void SubmitForFinance(Guid actorId)
    {
        if (Status != DownPaymentStatus.Invoiced)
            throw new DomainRuleException("An issued invoice is required before submitting the down payment to Finance.");
        SubmittedBy = actorId;
        SubmittedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Confirm(Guid actorId, string reference)
    {
        ConfirmationReference = reference;
        ConfirmedBy = actorId;
        ConfirmedAt = DateTimeOffset.UtcNow;
        Status = DownPaymentStatus.Confirmed;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public sealed class LeadDocument
{
    private LeadDocument() { }

    public LeadDocument(Guid organizationId, Guid leadId, string documentType, string fileName, string contentType, long sizeBytes, string objectKey, DateTimeOffset uploadExpiresAt, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        DocumentType = documentType;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ObjectKey = objectKey;
        UploadExpiresAt = uploadExpiresAt;
        Status = DocumentStatus.UploadPending;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string? Sha256 { get; private set; }
    public DocumentStatus Status { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Complete(string sha256)
    {
        if (DateTimeOffset.UtcNow > UploadExpiresAt) throw new DomainRuleException("The upload intent has expired.");
        Sha256 = sha256;
        Status = DocumentStatus.Uploaded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Archive()
    {
        Status = DocumentStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class Contract
{
    private Contract() { }

    public Contract(Guid organizationId, Guid leadId, string templateCode, string templateVersion)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        TemplateCode = templateCode;
        TemplateVersion = templateVersion;
        Status = ContractStatus.Draft;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public string TemplateCode { get; private set; } = string.Empty;
    public string TemplateVersion { get; private set; } = string.Empty;
    public ContractStatus Status { get; private set; }
    public bool GmApproved { get; private set; }
    public DateTimeOffset? GmApprovedAt { get; private set; }
    public Guid? GmApprovedBy { get; private set; }
    public string? RevisionReason { get; private set; }
    public DateTimeOffset? RevisionRequestedAt { get; private set; }
    public Guid? RevisionRequestedBy { get; private set; }
    public string? RevisionRequestedByName { get; private set; }
    public DateTimeOffset? SignedAt { get; private set; }
    public string? FranchiseeSignerName { get; private set; }
    public DateTimeOffset? FranchiseeSignedAt { get; private set; }
    public string? DrCareSignerName { get; private set; }
    public DateTimeOffset? DrCareSignedAt { get; private set; }
    public string ReviewChecklistJson { get; private set; } = "[]";
    public string? RenderedHtml { get; private set; }
    public string? CurrentPdfObjectKey { get; private set; }
    public string? SignedPdfObjectKey { get; private set; }
    public string? SignedPdfSha256 { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SubmitReview() { Status = ContractStatus.InReview; Touch(); }
    public void RequestRevision(string reason, Guid actorId, string? actorName = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("A revision reason is required.");
        RevisionReason = reason.Trim();
        RevisionRequestedAt = DateTimeOffset.UtcNow;
        RevisionRequestedBy = actorId;
        RevisionRequestedByName = string.IsNullOrWhiteSpace(actorName) ? null : actorName.Trim();
        Status = ContractStatus.RevisionRequested;
        Touch();
    }
    public void Approve(Guid actorId) { GmApproved = true; GmApprovedBy = actorId; GmApprovedAt = DateTimeOffset.UtcNow; Status = ContractStatus.Approved; Touch(); }
    public void AddSignature(string signerRole, string signerName, string signatureHash, DateTimeOffset signedAt)
    {
        if (!GmApproved) throw new DomainRuleException("Contract cannot be signed before GM approval.");
        if (string.IsNullOrWhiteSpace(signatureHash)) throw new DomainRuleException("A signature proof is required.");
        if (signerRole.Equals("franchisee", StringComparison.OrdinalIgnoreCase))
        {
            FranchiseeSignerName = signerName.Trim();
            FranchiseeSignedAt = signedAt;
        }
        else if (signerRole.Equals("dr-care", StringComparison.OrdinalIgnoreCase))
        {
            DrCareSignerName = signerName.Trim();
            DrCareSignedAt = signedAt;
        }
        else throw new DomainRuleException("Signer role must be franchisee or dr-care.");
        if (FranchiseeSignedAt.HasValue && DrCareSignedAt.HasValue)
        {
            Status = ContractStatus.Signed;
            SignedAt = signedAt;
        }
        Touch();
    }
    public void UpdateReviewChecklist(string checklistJson)
    {
        if (Status != ContractStatus.InReview) throw new DomainRuleException("The contract review checklist can only be changed while the contract is in review.");
        ReviewChecklistJson = checklistJson; Touch();
    }
    public void SetRenderedDocument(string html, string objectKey)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.RevisionRequested)) throw new DomainRuleException("Only a draft or requested revision can be regenerated.");
        RenderedHtml = html;
        CurrentPdfObjectKey = objectKey;
        Touch();
    }
    public void ApplyElectronicSignature(string signerRole, string signerName, string signatureHash, DateTimeOffset signedAt, string pdfObjectKey, string? finalSha256 = null)
    {
        AddSignature(signerRole, signerName, signatureHash, signedAt);
        CurrentPdfObjectKey = pdfObjectKey;
        if (Status == ContractStatus.Signed)
        {
            SignedPdfObjectKey = pdfObjectKey;
            SignedPdfSha256 = finalSha256;
        }
        Touch();
    }
    public void UpdateTemplate(string templateCode, string templateVersion)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.RevisionRequested)) throw new DomainRuleException("Only a draft or requested revision can be edited.");
        TemplateCode = templateCode.Trim(); TemplateVersion = templateVersion.Trim(); Touch();
    }
    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public sealed class Endorsement
{
    private Endorsement() { }

    public Endorsement(Guid organizationId, Guid leadId, Guid actorId, string receivingTeam, string handoffNotes, string boundaryJson = "[]")
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        ActorId = actorId;
        ReceivingTeam = receivingTeam;
        HandoffNotes = handoffNotes;
        BoundaryJson = boundaryJson;
        Status = EndorsementStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid ActorId { get; private set; }
    public string ReceivingTeam { get; private set; } = string.Empty;
    public string HandoffNotes { get; private set; } = string.Empty;
    public string BoundaryJson { get; private set; } = "[]";
    public EndorsementStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public Guid? AcknowledgedBy { get; private set; }

    public void Acknowledge(Guid actorId) { Status = EndorsementStatus.Acknowledged; AcknowledgedBy = actorId; AcknowledgedAt = DateTimeOffset.UtcNow; }
}

public sealed class Notification
{
    private Notification() { }

    public Notification(Guid organizationId, Guid recipientId, string type, string title, string message)
    {
        Id = Guid.NewGuid(); OrganizationId = organizationId; RecipientId = recipientId; Type = type; Title = title; Message = message; CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RecipientId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool Read { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead() { Read = true; ReadAt = DateTimeOffset.UtcNow; }
}

public sealed class PreLaunchChecklist
{
    private readonly List<PreLaunchItem> _items = [];
    private PreLaunchChecklist() { }
    public PreLaunchChecklist(Guid organizationId, Guid leadId, ProductLine productLine)
    { Id = Guid.NewGuid(); OrganizationId = organizationId; LeadId = leadId; ProductLine = productLine; Status = "IN_PROGRESS"; CreatedAt = UpdatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public ProductLine ProductLine { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? VideoEmailId { get; private set; }
    public string? VideoUrl { get; private set; }
    public DateTimeOffset? VideoQueuedAt { get; private set; }
    public IReadOnlyCollection<PreLaunchItem> Items => _items;
    public void AddItem(PreLaunchItem item) => _items.Add(item);
    public void Touch()
    {
        if (Status != "COMPLETED") Status = "IN_PROGRESS";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    public void Complete()
    {
        if (!_items.Where(x => x.Required).All(x => x.Complete))
            throw new DomainRuleException("All required pre-launch items must be complete.");
        Status = "COMPLETED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    public void RecordVideoEmail(Guid emailId, string videoUrl)
    {
        VideoEmailId = emailId; VideoUrl = videoUrl; VideoQueuedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class PreLaunchItem
{
    private PreLaunchItem() { }
    public PreLaunchItem(Guid checklistId, string code, string name, bool required, bool paused = false)
    { Id = Guid.NewGuid(); ChecklistId = checklistId; Code = code; Name = name; Required = required; Paused = paused; }
    public Guid Id { get; private set; }
    public Guid ChecklistId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool Required { get; private set; }
    public bool Complete { get; private set; }
    public bool Paused { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public void Update(bool complete, string? notes)
    { if (Paused) throw new DomainRuleException("A paused checklist item cannot be completed until its condition is met."); Complete = complete; Notes = notes?.Trim(); CompletedAt = complete ? DateTimeOffset.UtcNow : null; }
}

public sealed class ContractSigningRequest
{
    private ContractSigningRequest() { }
    public ContractSigningRequest(Guid organizationId, Guid contractId, Guid leadId, string signerRole, string signerName, string signerEmail, string tokenHash, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid(); OrganizationId = organizationId; ContractId = contractId; LeadId = leadId;
        SignerRole = signerRole.Trim().ToLowerInvariant(); SignerName = signerName.Trim(); SignerEmail = signerEmail.Trim().ToLowerInvariant();
        TokenHash = tokenHash; ExpiresAt = expiresAt; Status = SigningRequestStatus.Pending; CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContractId { get; private set; }
    public Guid LeadId { get; private set; }
    public string SignerRole { get; private set; } = string.Empty;
    public string SignerName { get; private set; } = string.Empty;
    public string SignerEmail { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public SigningRequestStatus Status { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ViewedAt { get; private set; }
    public DateTimeOffset? SignedAt { get; private set; }
    public DateTimeOffset? DeclinedAt { get; private set; }
    public string? DeclineReason { get; private set; }
    public string? SignatureSha256 { get; private set; }
    public string? SignedDocumentSha256 { get; private set; }
    public long? SignedDocumentSizeBytes { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public Guid? InvitationEmailId { get; private set; }
    public Guid? ReminderEmailId { get; private set; }
    public void SetEmailDelivery(Guid invitationEmailId, Guid reminderEmailId) { InvitationEmailId = invitationEmailId; ReminderEmailId = reminderEmailId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void View() { if (Status == SigningRequestStatus.Pending) Status = SigningRequestStatus.Viewed; ViewedAt ??= DateTimeOffset.UtcNow; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Sign(string signatureSha256, string documentSha256, long documentSizeBytes, string? ipAddress, string? userAgent)
    {
        if (Status is not (SigningRequestStatus.Pending or SigningRequestStatus.Viewed)) throw new DomainRuleException("This signing request is no longer active.");
        if (ExpiresAt <= DateTimeOffset.UtcNow) { Status = SigningRequestStatus.Expired; throw new DomainRuleException("This signing request has expired."); }
        SignatureSha256 = signatureSha256; SignedDocumentSha256 = documentSha256; SignedDocumentSizeBytes = documentSizeBytes;
        IpAddress = ipAddress; UserAgent = userAgent; SignedAt = UpdatedAt = DateTimeOffset.UtcNow; Status = SigningRequestStatus.Signed;
    }
    public void Decline(string? reason) { if (Status is not (SigningRequestStatus.Pending or SigningRequestStatus.Viewed)) throw new DomainRuleException("This signing request is no longer active."); DeclineReason = reason?.Trim(); DeclinedAt = UpdatedAt = DateTimeOffset.UtcNow; Status = SigningRequestStatus.Declined; }
    public void Void() { if (Status is SigningRequestStatus.Pending or SigningRequestStatus.Viewed) { Status = SigningRequestStatus.Voided; UpdatedAt = DateTimeOffset.UtcNow; } }
}

public sealed class RefreshToken
{
    private RefreshToken() { }
    public RefreshToken(Guid userId, Guid organizationId, string tokenHash, DateTimeOffset expiresAt)
    { Id = Guid.NewGuid(); UserId = userId; OrganizationId = organizationId; TokenHash = tokenHash; ExpiresAt = expiresAt; CreatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsActive => !RevokedAt.HasValue && ExpiresAt > DateTimeOffset.UtcNow;
    public void Revoke() => RevokedAt ??= DateTimeOffset.UtcNow;
}

public sealed class PasswordResetToken
{
    private PasswordResetToken() { }
    public PasswordResetToken(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    { Id = Guid.NewGuid(); UserId = userId; TokenHash = tokenHash; ExpiresAt = expiresAt; CreatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public bool IsActive => !UsedAt.HasValue && ExpiresAt > DateTimeOffset.UtcNow;
    public void Use() => UsedAt ??= DateTimeOffset.UtcNow;
}

public sealed class AppSetting
{
    private AppSetting() { }
    public AppSetting(Guid organizationId, string key, string valueJson)
    { Id = Guid.NewGuid(); OrganizationId = organizationId; Key = key; ValueJson = valueJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string ValueJson { get; private set; } = "{}";
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Update(string valueJson) { ValueJson = valueJson; UpdatedAt = DateTimeOffset.UtcNow; }
}

public sealed class EmailOutboxMessage
{
    private EmailOutboxMessage() { }

    public EmailOutboxMessage(
        Guid organizationId,
        string recipientEmail,
        string? recipientName,
        string subject,
        string htmlBody,
        string? textBody,
        string category,
        string attachmentsJson,
        string? idempotencyKey,
        int maxAttempts,
        DateTimeOffset? availableAt = null)
    {
        if (organizationId == Guid.Empty) throw new DomainRuleException("An organization is required for queued email.");
        if (!System.Net.Mail.MailAddress.TryCreate(recipientEmail?.Trim(), out var parsedAddress)) throw new DomainRuleException("A valid recipient email address is required.");
        if (string.IsNullOrWhiteSpace(subject)) throw new DomainRuleException("An email subject is required.");
        if (subject.Contains('\r') || subject.Contains('\n')) throw new DomainRuleException("The email subject contains invalid characters.");
        if (string.IsNullOrWhiteSpace(htmlBody)) throw new DomainRuleException("An HTML email body is required.");
        if (maxAttempts < 1) throw new DomainRuleException("Email delivery must allow at least one attempt.");

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        RecipientEmail = parsedAddress.Address.ToLowerInvariant();
        RecipientName = recipientName?.Trim();
        Subject = subject.Trim();
        HtmlBody = htmlBody;
        TextBody = string.IsNullOrWhiteSpace(textBody) ? null : textBody;
        Category = string.IsNullOrWhiteSpace(category) ? "transactional" : category.Trim();
        AttachmentsJson = string.IsNullOrWhiteSpace(attachmentsJson) ? "[]" : attachmentsJson;
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        Status = EmailDeliveryStatus.Pending;
        MaxAttempts = maxAttempts;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        AvailableAt = availableAt ?? CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string RecipientEmail { get; private set; } = string.Empty;
    public string? RecipientName { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string HtmlBody { get; private set; } = string.Empty;
    public string? TextBody { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string AttachmentsJson { get; private set; } = "[]";
    public string? IdempotencyKey { get; private set; }
    public EmailDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }

    public void Cancel()
    {
        if (Status is EmailDeliveryStatus.Pending or EmailDeliveryStatus.Failed)
        {
            Status = EmailDeliveryStatus.Cancelled;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    public void StartAttempt(DateTimeOffset now)
    {
        if (Status is not (EmailDeliveryStatus.Pending or EmailDeliveryStatus.Processing))
            throw new DomainRuleException("Only pending email can begin delivery.");
        Status = EmailDeliveryStatus.Processing;
        AttemptCount++;
        ProcessingStartedAt = now;
        LastError = null;
        UpdatedAt = now;
    }

    public void MarkSent(DateTimeOffset now)
    {
        if (Status != EmailDeliveryStatus.Processing) throw new DomainRuleException("Only processing email can be marked sent.");
        Status = EmailDeliveryStatus.Sent;
        SentAt = now;
        ProcessingStartedAt = null;
        LastError = null;
        UpdatedAt = now;
    }

    public void RecordFailure(string error, DateTimeOffset now, DateTimeOffset retryAt)
    {
        if (Status != EmailDeliveryStatus.Processing) throw new DomainRuleException("Only processing email can record a delivery failure.");
        LastError = string.IsNullOrWhiteSpace(error) ? "Email delivery failed." : error.Trim()[..Math.Min(error.Trim().Length, 2000)];
        ProcessingStartedAt = null;
        UpdatedAt = now;
        if (AttemptCount >= MaxAttempts)
        {
            Status = EmailDeliveryStatus.Failed;
            return;
        }
        Status = EmailDeliveryStatus.Pending;
        AvailableAt = retryAt;
    }
}

public sealed class DomainRuleException(string message) : Exception(message);
