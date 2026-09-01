using System.Text.Json;
using DrCare.Application.Contracts;
using DrCare.Domain;

namespace DrCare.Application.Workflows;

public sealed record WorkflowResponse(Guid Id, string Operation, Guid? LeadId, string Status, JsonElement Payload, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version);

public interface IWorkflowService
{
    Task<WorkflowResponse> RecordAsync(string operation, Guid? leadId, object? payload, CancellationToken cancellationToken);
    Task<WorkflowResponse?> GetAsync(Guid recordId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowResponse>> ListAsync(string? operation, Guid? leadId, CancellationToken cancellationToken);
}

public sealed class WorkflowService(IWorkflowRepository records, IProcessRepository processes, ILeadRepository leads, ICurrentUser currentUser) : IWorkflowService
{
    public async Task<WorkflowResponse> RecordAsync(string operation, Guid? leadId, object? payload, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated && !operation.Equals("auth.forgot-password", StringComparison.Ordinal))
            throw new UnauthorizedException("Authentication is required.");

        EnsureRole(operation);

        if (leadId.HasValue)
        {
            var lead = await leads.GetAsync(currentUser.OrganizationId, leadId.Value, cancellationToken) ?? throw new NotFoundException("Lead not found.");
            if (currentUser.Role == UserRole.MarketingAgent && lead.AssignedAgentId != currentUser.UserId)
                throw new ForbiddenException("You do not have access to this lead.");
        }

        var processResult = await ApplyProcessOperationAsync(operation, leadId, payload, cancellationToken);
        var json = operation.StartsWith("auth.", StringComparison.Ordinal)
            ? "{}"
            : JsonSerializer.Serialize(processResult ?? payload ?? new { });
        var record = new WorkflowRecord(currentUser.OrganizationId, currentUser.UserId, operation, leadId, json);
        record.MarkCompleted();
        await records.AddAsync(record, cancellationToken);
        await records.SaveChangesAsync(cancellationToken);
        return ToResponse(record);
    }

    private async Task<object?> ApplyProcessOperationAsync(string operation, Guid? leadId, object? payload, CancellationToken ct)
    {
        if (!leadId.HasValue) return null;
        var lead = await leads.GetAsync(currentUser.OrganizationId, leadId.Value, ct) ?? throw new NotFoundException("Lead not found.");
        switch (operation)
        {
            case "down-payment.update":
            case "down-payment.invoice":
            {
                throw new ValidationException("Use the dedicated down-payment invoice endpoint so the PDF artifact and audit event are created.");
            }
            case "down-payment.confirm":
            {
                if (currentUser.Role != UserRole.Finance) throw new ForbiddenException("Only Finance can confirm a down payment.");
                var request = Deserialize<ConfirmDownPaymentRequest>(payload) ?? throw new ValidationException("Payment confirmation is required.");
                var documents = await processes.ListDocumentsAsync(currentUser.OrganizationId, leadId.Value, ct);
                var combinedUploaded = documents.Any(x => x.DocumentType.Equals("VALID_ID_SIGNATURES", StringComparison.OrdinalIgnoreCase) && x.Status == DocumentStatus.Uploaded);
                var legacyUploaded = new[] { "VALID_ID", "SPECIMEN_SIGNATURE_1", "SPECIMEN_SIGNATURE_2", "SPECIMEN_SIGNATURE_3" }
                    .All(type => documents.Any(x => x.DocumentType.Equals(type, StringComparison.OrdinalIgnoreCase) && x.Status == DocumentStatus.Uploaded));
                if (!combinedUploaded && !legacyUploaded)
                    throw new ConflictException("Payment cannot be confirmed until the combined valid ID and three specimen signatures file is uploaded.");
                var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, leadId.Value, ct) ?? throw new ConflictException("An invoice must be generated before payment confirmation.");
                if (payment.Amount != request.Amount || !payment.Currency.Equals(request.Currency, StringComparison.OrdinalIgnoreCase)) throw new ValidationException("Payment amount or currency does not match the invoice.");
                payment.Confirm(currentUser.UserId, request.ReferenceNumber);
                await processes.SaveChangesAsync(ct);
                return new { payment.Id, payment.LeadId, payment.ConfirmationReference, ConfirmedAt = payment.ConfirmedAt, Status = payment.Status.ToString() };
            }
            case "documents.create-upload-intent":
            {
                var request = Deserialize<CreateUploadIntentRequest>(payload) ?? throw new ValidationException("Upload intent is required.");
                ValidateDocumentRequest(request);
                EnsureDocumentRole(request.DocumentType);
                var id = Guid.NewGuid();
                var expires = DateTimeOffset.UtcNow.AddMinutes(15);
                var objectKey = $"private/{currentUser.OrganizationId:N}/leads/{leadId.Value:N}/{id:N}";
                var document = new LeadDocument(currentUser.OrganizationId, leadId.Value, request.DocumentType.ToUpperInvariant(), request.FileName, request.ContentType, request.SizeBytes, objectKey, expires, id);
                await processes.AddDocumentAsync(document, ct);
                await processes.SaveChangesAsync(ct);
                return new UploadIntentResponse(id, objectKey, $"/api/v1/leads/{leadId.Value}/documents/{id}/upload", expires, request.ContentType, 25_000_000);
            }
            case "documents.complete-upload":
            {
                var wrapper = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload));
                var documentId = wrapper.GetProperty("documentId").GetGuid();
                var request = JsonSerializer.Deserialize<CompleteUploadRequest>(wrapper.GetProperty("request").GetRawText()) ?? throw new ValidationException("Upload completion is required.");
                var document = await processes.GetDocumentAsync(currentUser.OrganizationId, leadId.Value, documentId, ct) ?? throw new NotFoundException("Document not found.");
                EnsureDocumentRole(document.DocumentType);
                if (!document.ObjectKey.Equals(request.ObjectKey, StringComparison.Ordinal)) throw new ForbiddenException("The object key does not belong to this upload intent.");
                document.Complete(request.Sha256);
                await processes.SaveChangesAsync(ct);
                return new CompleteUploadResponse(document.Id, document.Status.ToString(), document.UpdatedAt);
            }
            case "documents.archive":
            {
                var wrapper = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload));
                var documentId = wrapper.GetProperty("documentId").GetGuid();
                var document = await processes.GetDocumentAsync(currentUser.OrganizationId, leadId.Value, documentId, ct) ?? throw new NotFoundException("Document not found.");
                EnsureDocumentRole(document.DocumentType);
                document.Archive();
                await processes.SaveChangesAsync(ct);
                return new { document.Id, Status = document.Status.ToString() };
            }
            case "contracts.generate":
            {
                var request = Deserialize<GenerateContractRequest>(payload) ?? throw new ValidationException("Contract template is required.");
                var contract = await processes.GetContractAsync(currentUser.OrganizationId, leadId.Value, ct);
                if (contract is null) { contract = new Contract(currentUser.OrganizationId, leadId.Value, request.TemplateCode, request.Version); await processes.AddContractAsync(contract, ct); }
                await processes.SaveChangesAsync(ct);
                return new ContractResponse(leadId.Value, contract.Id, contract.Status.ToString(), contract.TemplateCode, contract.TemplateVersion, contract.UpdatedAt);
            }
            case "contracts.submit-review":
            case "contracts.approve":
            case "contracts.request-revision":
            case "contracts.signatures.franchisee":
            case "contracts.signatures.dr-care":
            {
                var contract = await processes.GetContractAsync(currentUser.OrganizationId, leadId.Value, ct) ?? throw new NotFoundException("Contract not found.");
                if (operation == "contracts.submit-review") contract.SubmitReview();
                else if (operation == "contracts.approve") contract.Approve(currentUser.UserId);
                else if (operation == "contracts.request-revision")
                {
                    var revision = Deserialize<RevisionRequest>(payload) ?? throw new ValidationException("A revision reason is required.");
                    contract.RequestRevision(revision.Reason, currentUser.UserId, currentUser.DisplayName);
                }
                else
                {
                    var signature = Deserialize<ContractSignatureRequest>(payload) ?? throw new ValidationException("Contract signature is required.");
                    contract.AddSignature(operation.EndsWith("franchisee", StringComparison.Ordinal) ? "franchisee" : "dr-care", signature.SignerName, signature.SignatureHash, signature.SignedAt);
                }
                await processes.SaveChangesAsync(ct);
                return new ContractResponse(leadId.Value, contract.Id, contract.Status.ToString(), contract.TemplateCode, contract.TemplateVersion, contract.UpdatedAt);
            }
            case "endorsement.create":
            {
                var request = Deserialize<CreateEndorsementRequest>(payload) ?? throw new ValidationException("Endorsement details are required.");
                var endorsement = new Endorsement(currentUser.OrganizationId, leadId.Value, currentUser.UserId, request.ReceivingTeam, request.HandoffNotes);
                await processes.AddEndorsementAsync(endorsement, ct);
                await processes.SaveChangesAsync(ct);
                return new EndorsementResponse(endorsement.Id, endorsement.LeadId, endorsement.ReceivingTeam, endorsement.Status.ToString(), endorsement.HandoffNotes, [], endorsement.CreatedAt, endorsement.AcknowledgedAt);
            }
            case "endorsement.acknowledge":
            {
                var wrapper = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload));
                var endorsementId = wrapper.GetProperty("endorsementId").GetGuid();
                var endorsement = await processes.GetEndorsementAsync(currentUser.OrganizationId, endorsementId, ct) ?? throw new NotFoundException("Endorsement not found.");
                endorsement.Acknowledge(currentUser.UserId);
                await processes.SaveChangesAsync(ct);
                return new EndorsementResponse(endorsement.Id, endorsement.LeadId, endorsement.ReceivingTeam, endorsement.Status.ToString(), endorsement.HandoffNotes, [], endorsement.CreatedAt, endorsement.AcknowledgedAt);
            }
            default: return null;
        }
    }

    private static T? Deserialize<T>(object? value) => value is null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));

    private static void ValidateDocumentRequest(CreateUploadIntentRequest request)
    {
        var allowed = new[]
        {
            "VALID_ID_SIGNATURES", "FLOOR_PLAN", "PERSPECTIVE", "SIGNED_CONTRACT", "PAYMENT_RECEIPT",
            "DOH_FLOOR_PLAN", "LEASE_AND_ADDRESS", "DTI_REGISTRATION", "SITE_PHOTOS", "PERMITS",
            "BUSINESS_PLAN", "VALID_ID_TIN", "FRANCHISE_AGREEMENT", "TRAINING_APPLICATION", "PHARMACY_PERMITS"
        };
        if (!allowed.Contains(request.DocumentType.ToUpperInvariant())) throw new ValidationException("Unsupported document type.");
        var allowedTypes = new[] { "application/pdf", "image/jpeg", "image/png" };
        if (!allowedTypes.Contains(request.ContentType.ToLowerInvariant())) throw new ValidationException("Unsupported document content type.");
    }

    private void EnsureRole(string operation)
    {
        if (operation == "down-payment.confirm" && currentUser.Role != UserRole.Finance)
            throw new ForbiddenException("Only Finance can confirm a down payment.");
        if (operation is "contracts.approve" or "contracts.request-revision" && currentUser.Role != UserRole.GeneralManager)
            throw new ForbiddenException("Only the General Manager can approve or revise a contract.");
        if (operation == "endorsement.acknowledge" && currentUser.Role != UserRole.AdminTeam)
            throw new ForbiddenException("Only the Admin Team can acknowledge an endorsement.");
        if (operation.StartsWith("documents.", StringComparison.Ordinal) && currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.Finance))
            throw new ForbiddenException("Your role cannot upload or change documents.");
    }

    private void EnsureDocumentRole(string documentType)
    {
        var type = documentType.Trim().ToUpperInvariant();
        if (type == "PAYMENT_RECEIPT" && currentUser.Role == UserRole.Finance) return;
        if (type == "PAYMENT_RECEIPT") throw new ForbiddenException("Only Finance can upload payment evidence.");
        if (currentUser.Role is UserRole.MarketingAgent or UserRole.MarketingAdmin) return;
        throw new ForbiddenException("Your role cannot upload this type of document.");
    }

    public async Task<WorkflowResponse?> GetAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var record = await records.GetAsync(currentUser.OrganizationId, recordId, cancellationToken);
        if (record is null) return null;
        if (currentUser.Role == UserRole.MarketingAgent && record.ActorId != currentUser.UserId)
            throw new ForbiddenException("You do not have access to this workflow record.");
        return ToResponse(record);
    }

    public async Task<IReadOnlyList<WorkflowResponse>> ListAsync(string? operation, Guid? leadId, CancellationToken cancellationToken)
    {
        if (leadId.HasValue)
        {
            var lead = await leads.GetAsync(currentUser.OrganizationId, leadId.Value, cancellationToken) ?? throw new NotFoundException("Lead not found.");
            if (currentUser.Role == UserRole.MarketingAgent && lead.AssignedAgentId != currentUser.UserId)
                throw new ForbiddenException("You do not have access to this lead.");
        }

        var result = await records.ListAsync(currentUser.OrganizationId, operation, leadId, cancellationToken);
        if (currentUser.Role == UserRole.MarketingAgent) result = result.Where(x => x.ActorId == currentUser.UserId).ToArray();
        return result.Select(ToResponse).ToArray();
    }

    private static WorkflowResponse ToResponse(WorkflowRecord record) => new(
        record.Id, record.Operation, record.LeadId, record.Status,
        JsonSerializer.Deserialize<JsonElement>(record.PayloadJson), record.CreatedAt, record.UpdatedAt, record.Version);
}
