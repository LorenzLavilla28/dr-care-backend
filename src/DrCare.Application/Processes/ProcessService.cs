using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Net;
using System.Security.Cryptography;
using DrCare.Application.Contracts;
using DrCare.Application.Notifications;
using DrCare.Application.Settings;
using DrCare.Domain;

namespace DrCare.Application.Processes;

public interface IProcessService
{
    Task<DownPaymentResponse> GetDownPaymentAsync(Guid leadId, CancellationToken ct);
    Task<DownPaymentResponse> UpdateDownPaymentAsync(Guid leadId, CreateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceResponse> GenerateInvoiceAsync(Guid leadId, CreateInvoiceRequest request, CancellationToken ct);
    Task<DownPaymentResponse> SubmitDownPaymentForFinanceAsync(Guid leadId, SubmitDownPaymentRequest request, CancellationToken ct);
    Task<DocumentDownloadResponse> GetInvoiceDownloadAsync(Guid leadId, CancellationToken ct);
    Task<ConfirmDownPaymentResponse> ConfirmDownPaymentAsync(Guid leadId, ConfirmDownPaymentRequest request, CancellationToken ct);
    Task<IReadOnlyList<DocumentResponse>> ListDocumentsAsync(Guid leadId, CancellationToken ct);
    Task<UploadIntentResponse> CreateUploadIntentAsync(Guid leadId, CreateUploadIntentRequest request, CancellationToken ct);
    Task<CompleteUploadResponse> CompleteUploadAsync(Guid leadId, Guid documentId, CompleteUploadRequest request, CancellationToken ct);
    Task<DocumentDownloadResponse> CreateDownloadUrlAsync(Guid leadId, Guid documentId, CancellationToken ct);
    Task<DocumentResponse> ArchiveDocumentAsync(Guid leadId, Guid documentId, CancellationToken ct);
    Task<ContractResponse?> GetContractAsync(Guid leadId, CancellationToken ct);
    Task<DocumentDownloadResponse> GetContractDownloadAsync(Guid leadId, CancellationToken ct);
    Task<ContractResponse> GenerateContractAsync(Guid leadId, GenerateContractRequest request, CancellationToken ct);
    Task<ContractResponse> UpdateContractAsync(Guid leadId, GenerateContractRequest request, CancellationToken ct);
    Task<ContractResponse> SubmitContractReviewAsync(Guid leadId, SubmitContractReviewRequest request, CancellationToken ct);
    Task<ContractResponse> ApproveContractAsync(Guid leadId, ApproveContractRequest request, CancellationToken ct);
    Task<ContractResponse> RequestRevisionAsync(Guid leadId, RevisionRequest request, CancellationToken ct);
    Task<ContractChecklistResponse> GetContractChecklistAsync(Guid leadId, CancellationToken ct);
    Task<ContractChecklistResponse> UpdateContractChecklistAsync(Guid leadId, ContractChecklistResponse request, CancellationToken ct);
    Task<ContractSignatureResponse> AddSignatureAsync(Guid leadId, string routeRole, ContractSignatureRequest request, CancellationToken ct);
    Task<EndorsementResponse> CreateEndorsementAsync(Guid leadId, CreateEndorsementRequest request, CancellationToken ct);
    Task<EndorsementResponse?> GetEndorsementAsync(Guid leadId, CancellationToken ct);
    Task<IReadOnlyList<EndorsementResponse>> ListEndorsementsAsync(CancellationToken ct);
    Task<EndorsementResponse> AcknowledgeEndorsementAsync(Guid endorsementId, CancellationToken ct);
    Task<IReadOnlyList<NotificationResponse>> ListNotificationsAsync(CancellationToken ct);
    Task<NotificationResponse> MarkNotificationReadAsync(Guid notificationId, CancellationToken ct);
}

public sealed class ProcessService(IProcessRepository processes, ILeadRepository leads, IPrivateObjectStorage storage, IDocumentPdfRenderer pdfRenderer, ISettingsService settings, ICurrentUser currentUser, INotificationService notifications, IPreLaunchRepository checklists) : IProcessService
{
    public async Task<DownPaymentResponse> GetDownPaymentAsync(Guid leadId, CancellationToken ct)
    {
        EnsureDownPaymentRead();
        var lead = await GetLeadAsync(leadId, ct);
        var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, lead.Id, ct);
        if (payment is null) throw new NotFoundException("Down payment has not been configured.");
        return ToPayment(payment, await DocumentsCompleteAsync(leadId, ct));
    }

    public async Task<DownPaymentResponse> UpdateDownPaymentAsync(Guid leadId, CreateInvoiceRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite();
        var lead = await GetLeadAsync(leadId, ct);
        EnsureExpectedVersion(lead, request.ExpectedVersion);
        EnsureDownPaymentStage(lead);
        var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, lead.Id, ct);
        var invoiceSettings = await settings.GetInvoiceAsync(ct);
        if (payment is null) { payment = new DownPayment(currentUser.OrganizationId, lead.Id, request.Amount ?? invoiceSettings.DefaultDownPayment, NormalizeCurrency(request.Currency ?? invoiceSettings.Currency)); await processes.AddDownPaymentAsync(payment, ct); }
        else payment.UpdateDetails(request.Amount ?? payment.Amount, NormalizeCurrency(request.Currency));
        await processes.SaveChangesAsync(ct);
        return ToPayment(payment, await DocumentsCompleteAsync(leadId, ct));
    }

    public async Task<InvoiceResponse> GenerateInvoiceAsync(Guid leadId, CreateInvoiceRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite();
        var lead = await GetLeadAsync(leadId, ct);
        EnsureExpectedVersion(lead, request.ExpectedVersion);
        EnsureDownPaymentStage(lead);
        if (!await DocumentsCompleteAsync(leadId, ct))
            throw new ConflictException("Upload the combined valid ID and three specimen signatures file before generating the invoice.");
        var invoiceSettings = await settings.GetInvoiceAsync(ct);
        var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, lead.Id, ct);
        if (payment is null) { payment = new DownPayment(currentUser.OrganizationId, lead.Id, request.Amount ?? invoiceSettings.DefaultDownPayment, NormalizeCurrency(request.Currency ?? invoiceSettings.Currency)); await processes.AddDownPaymentAsync(payment, ct); }
        if (payment.Status == DownPaymentStatus.Confirmed) throw new ConflictException("An invoice cannot be regenerated after payment confirmation.");
        payment.UpdateDetails(request.Amount ?? payment.Amount, NormalizeCurrency(request.Currency ?? payment.Currency));
        try { lead.MarkDownPaymentPending(); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        var invoiceNumber = payment.InvoiceNumber ?? $"DP-{DateTimeOffset.UtcNow:yyyyMMdd}-{lead.Id.ToString("N")[..8]}".ToUpperInvariant();
        var dueAt = DateTimeOffset.UtcNow.AddDays(invoiceSettings.PaymentTermDays);
        var html = RenderTemplate("Invoices", "down-payment-invoice.html", new Dictionary<string, string?>
        {
            ["invoiceNumber"] = invoiceNumber, ["issuedDate"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), ["dueDate"] = dueAt.ToString("yyyy-MM-dd"),
            ["fullName"] = lead.FullName, ["email"] = lead.Email, ["productLine"] = lead.ProductLine?.ToString() ?? "Pending", ["amount"] = payment.Amount.ToString("N2"), ["currency"] = payment.Currency
        });
        var pdf = await pdfRenderer.RenderHtmlAsync(html, ct);
        var key = $"private/{currentUser.OrganizationId:N}/leads/{lead.Id:N}/invoices/{invoiceNumber}.pdf";
        await storage.PutAsync(key, "application/pdf", pdf, ct);
        payment.IssueInvoice(invoiceNumber, key, Sha256(pdf), dueAt);
        await AddAuditAsync(lead, ActivityType.InvoiceGenerated, $"Down payment invoice {invoiceNumber} generated.", ct);
        await processes.SaveChangesAsync(ct);
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        return new InvoiceResponse(lead.Id, payment.InvoiceNumber!, payment.Amount, payment.Currency, payment.Status.ToString(), payment.UpdatedAt, payment.InvoiceDueAt, await storage.CreateDownloadUrlAsync(key, expires, ct));
    }

    public async Task<DownPaymentResponse> SubmitDownPaymentForFinanceAsync(Guid leadId, SubmitDownPaymentRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite();
        var lead = await GetLeadAsync(leadId, ct);
        EnsureExpectedVersion(lead, request.ExpectedVersion);
        if (lead.State != LeadState.DownPaymentPending)
            throw new ConflictException("Generate the invoice before submitting the down payment to Finance.");
        var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, lead.Id, ct)
            ?? throw new ConflictException("Generate the invoice before submitting the down payment to Finance.");
        if (payment.Status != DownPaymentStatus.Invoiced)
            throw new ConflictException("An issued invoice is required before submitting the down payment to Finance.");
        if (!await DocumentsCompleteAsync(leadId, ct))
            throw new ConflictException("The combined valid ID and three specimen signatures file is required before submitting to Finance.");
        payment.SubmitForFinance(currentUser.UserId);
        await AddAuditAsync(lead, ActivityType.DownPaymentSubmittedForFinance, "Down payment invoice and required documents submitted to Finance for verification.", ct);
        await notifications.NotifyRoleAsync(currentUser.OrganizationId, UserRole.Finance, "down-payment-submitted", "Down payment ready for verification", $"{lead.FullName}'s invoice and required document are ready for payment verification.", ct);
        await processes.SaveChangesAsync(ct);
        return ToPayment(payment, true);
    }

    public async Task<DocumentDownloadResponse> GetInvoiceDownloadAsync(Guid leadId, CancellationToken ct)
    {
        EnsureDownPaymentRead();
        await GetLeadAsync(leadId, ct);
        var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Invoice not found.");
        if (string.IsNullOrWhiteSpace(payment.InvoiceObjectKey)) throw new NotFoundException("Invoice artifact has not been generated.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        return new DocumentDownloadResponse(payment.Id, await storage.CreateDownloadUrlAsync(payment.InvoiceObjectKey, expires, ct), expires);
    }

    public async Task<ConfirmDownPaymentResponse> ConfirmDownPaymentAsync(Guid leadId, ConfirmDownPaymentRequest request, CancellationToken ct)
    {
        if (currentUser.Role != UserRole.Finance) throw new ForbiddenException("Only Finance can confirm a down payment.");
        var lead = await GetLeadAsync(leadId, ct);
        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != lead.Version) throw new ConflictException("The lead was changed by another user. Refresh and try again.");
        var payment = await processes.GetDownPaymentAsync(currentUser.OrganizationId, lead.Id, ct) ?? throw new ConflictException("An invoice must be generated before payment confirmation.");
        if (payment.Status != DownPaymentStatus.Invoiced) throw new ConflictException("Only an invoiced payment can be confirmed.");
        if (payment.SubmittedAt is null) throw new ConflictException("The down payment must be submitted by the Agent before Finance can verify it.");
        if (payment.Amount != request.Amount || !payment.Currency.Equals(request.Currency, StringComparison.OrdinalIgnoreCase)) throw new ValidationException("Payment amount or currency does not match the invoice.");
        if (!await DocumentsCompleteAsync(leadId, ct)) throw new ConflictException("The combined valid ID and three specimen signatures file is required before payment confirmation.");
        payment.Confirm(currentUser.UserId, request.ReferenceNumber);
        try { lead.MoveTo(LeadState.DownPaymentPending, LeadState.DownPaymentConfirmed); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        await AddAuditAsync(lead, ActivityType.PaymentConfirmed, "Finance confirmed the down payment and required documents.", ct);
        await notifications.NotifyUserAsync(currentUser.OrganizationId, lead.AssignedAgentId, "down-payment-confirmed", "Down payment confirmed", $"Finance confirmed the down payment for {lead.FullName}. Contract drafting can begin.", ct);
        await notifications.NotifyRoleAsync(currentUser.OrganizationId, UserRole.MarketingAdmin, "down-payment-confirmed", "Down payment confirmed", $"Finance confirmed the down payment for {lead.FullName}. Contract drafting can begin.", ct);
        await processes.SaveChangesAsync(ct);
        return new ConfirmDownPaymentResponse(lead.Id, payment.Status.ToString(), payment.ConfirmedAt!.Value, payment.ConfirmationReference!);
    }

    public async Task<IReadOnlyList<DocumentResponse>> ListDocumentsAsync(Guid leadId, CancellationToken ct)
    {
        EnsureDocumentRead();
        await GetLeadAsync(leadId, ct);
        return (await processes.ListDocumentsAsync(currentUser.OrganizationId, leadId, ct)).Select(ToDocument).ToArray();
    }

    public async Task<UploadIntentResponse> CreateUploadIntentAsync(Guid leadId, CreateUploadIntentRequest request, CancellationToken ct)
    {
        EnsureDocumentWrite(request.DocumentType);
        await GetLeadAsync(leadId, ct);
        ValidateDocumentRequest(request);
        if (currentUser.Role == UserRole.MarketingAgent && request.DocumentType.Trim().ToUpperInvariant() != "VALID_ID_SIGNATURES")
            throw new ForbiddenException("Marketing Agents can upload only the combined valid ID and specimen signatures file.");
        var id = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var key = $"private/{currentUser.OrganizationId:N}/leads/{leadId:N}/{id:N}";
        var document = new LeadDocument(currentUser.OrganizationId, leadId, request.DocumentType.ToUpperInvariant(), request.FileName.Trim(), request.ContentType.ToLowerInvariant(), request.SizeBytes, key, expires, id);
        var uploadUrl = await storage.CreateUploadUrlAsync(document.ObjectKey, document.ContentType, expires, ct);
        await processes.AddDocumentAsync(document, ct);
        await processes.SaveChangesAsync(ct);
        return new UploadIntentResponse(id, document.ObjectKey, uploadUrl, expires, document.ContentType, 25_000_000);
    }

    public async Task<CompleteUploadResponse> CompleteUploadAsync(Guid leadId, Guid documentId, CompleteUploadRequest request, CancellationToken ct)
    {
        await GetLeadAsync(leadId, ct);
        var document = await processes.GetDocumentAsync(currentUser.OrganizationId, leadId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        EnsureDocumentWrite(document.DocumentType);
        if (!document.ObjectKey.Equals(request.ObjectKey, StringComparison.Ordinal)) throw new ForbiddenException("The object key does not belong to this upload intent.");
        if (request.DocumentId != documentId) throw new ValidationException("Document id does not match the route.");
        if (!await storage.ExistsAsync(document.ObjectKey, ct)) throw new ConflictException("The upload object was not found in private storage.");
        if (string.IsNullOrWhiteSpace(request.Sha256) || !request.Sha256.All(Uri.IsHexDigit) || request.Sha256.Length != 64) throw new ValidationException("Sha256 must be a 64-character hexadecimal digest.");
        await using var uploaded = await storage.OpenReadAsync(document.ObjectKey, ct);
        if (uploaded.Length != document.SizeBytes) throw new ConflictException("The uploaded object size does not match the upload intent.");
        var actualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(uploaded, ct)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(actualSha256), System.Text.Encoding.UTF8.GetBytes(request.Sha256.ToLowerInvariant()))) throw new ConflictException("The uploaded object checksum does not match. Upload the file again.");
        document.Complete(actualSha256);
        var lead = await GetLeadAsync(leadId, ct);
        await AddAuditAsync(lead, ActivityType.DocumentUploaded, $"{DocumentTypeName(document.DocumentType)} uploaded.", ct);
        await processes.SaveChangesAsync(ct);
        return new CompleteUploadResponse(document.Id, document.Status.ToString(), document.UpdatedAt);
    }

    public async Task<DocumentDownloadResponse> CreateDownloadUrlAsync(Guid leadId, Guid documentId, CancellationToken ct)
    {
        EnsureDocumentRead();
        var lead = await GetLeadAsync(leadId, ct);
        var document = await processes.GetDocumentAsync(currentUser.OrganizationId, leadId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        if (document.Status != DocumentStatus.Uploaded) throw new ConflictException("Only uploaded documents can be downloaded.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        return new DocumentDownloadResponse(document.Id, await storage.CreateDownloadUrlAsync(document.ObjectKey, expires, ct), expires);
    }

    public async Task<DocumentResponse> ArchiveDocumentAsync(Guid leadId, Guid documentId, CancellationToken ct)
    {
        var lead = await GetLeadAsync(leadId, ct);
        var document = await processes.GetDocumentAsync(currentUser.OrganizationId, leadId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        EnsureDocumentWrite(document.DocumentType);
        document.Archive();
        await AddAuditAsync(lead, ActivityType.DocumentArchived, $"{DocumentTypeName(document.DocumentType)} archived.", ct);
        await processes.SaveChangesAsync(ct);
        return ToDocument(document);
    }

    public async Task<ContractResponse?> GetContractAsync(Guid leadId, CancellationToken ct)
    {
        EnsureContractRead();
        await GetLeadAsync(leadId, ct);
        var contract = await processes.GetContractAsync(currentUser.OrganizationId, leadId, ct);
        return contract is null ? null : ToContract(contract);
    }

    public async Task<DocumentDownloadResponse> GetContractDownloadAsync(Guid leadId, CancellationToken ct)
    {
        EnsureContractRead();
        await GetLeadAsync(leadId, ct); var contract = await GetContractEntityAsync(leadId, ct);
        var key = contract.SignedPdfObjectKey ?? contract.CurrentPdfObjectKey;
        if (string.IsNullOrWhiteSpace(key)) throw new NotFoundException("Contract document has not been generated.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        return new DocumentDownloadResponse(contract.Id, await storage.CreateDownloadUrlAsync(key, expires, ct), expires);
    }

    public async Task<ContractResponse> GenerateContractAsync(Guid leadId, GenerateContractRequest request, CancellationToken ct)
    {
        EnsureContractDraftAccess(); var lead = await GetLeadAsync(leadId, ct); EnsureExpectedVersion(lead, request.ExpectedVersion); EnsureContractDraftStage(lead);
        var contract = await processes.GetContractAsync(currentUser.OrganizationId, leadId, ct);
        var created = contract is null;
        if (contract is null) { contract = new Contract(currentUser.OrganizationId, leadId, request.TemplateCode.Trim(), request.Version.Trim()); await processes.AddContractAsync(contract, ct); }
        else contract.UpdateTemplate(request.TemplateCode, request.Version);
        var html = RenderTemplate("Contracts", "franchise-agreement.html", new Dictionary<string, string?>
        {
            ["contractNumber"] = contract.Id.ToString("N").ToUpperInvariant(), ["fullName"] = lead.FullName, ["email"] = lead.Email, ["contactNumber"] = lead.ContactNumber,
            ["address"] = lead.Address ?? "Not provided", ["productLine"] = lead.ProductLine?.ToString() ?? "Not selected", ["listPrice"] = lead.ListPrice?.ToString("N2") ?? "0.00",
            ["actualPrice"] = lead.ActualPrice?.ToString("N2") ?? "0.00", ["date"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
        });
        var pdf = await pdfRenderer.RenderHtmlAsync(html, ct);
        var key = $"private/{currentUser.OrganizationId:N}/leads/{lead.Id:N}/contracts/{contract.Id:N}-draft.pdf";
        await storage.PutAsync(key, "application/pdf", pdf, ct); contract.SetRenderedDocument(html, key);
        if (lead.State == LeadState.DownPaymentConfirmed) lead.MoveTo(LeadState.DownPaymentConfirmed, LeadState.ContractDrafting);
        else if (lead.State == LeadState.ContractReview && contract.Status == ContractStatus.RevisionRequested) lead.MoveTo(LeadState.ContractReview, LeadState.ContractDrafting);
        await AddAuditAsync(lead, ActivityType.ContractGenerated, "Contract generated from the configured template.", ct);
        await notifications.NotifyRoleAsync(currentUser.OrganizationId, UserRole.GeneralManager, "contract-drafted", "Contract drafted", $"A contract for {lead.FullName} is ready to be submitted for General Manager review.", ct);
        await processes.SaveChangesAsync(ct); return ToContract(contract);
    }

    public async Task<ContractResponse> UpdateContractAsync(Guid leadId, GenerateContractRequest request, CancellationToken ct)
    {
        EnsureContractDraftAccess(); var lead = await GetLeadAsync(leadId, ct); EnsureExpectedVersion(lead, request.ExpectedVersion); if (lead.State != LeadState.ContractDrafting) throw new ConflictException("The contract can only be edited while it is being drafted."); var contract = await GetContractEntityAsync(leadId, ct);
        try { contract.UpdateTemplate(request.TemplateCode, request.Version); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        await processes.SaveChangesAsync(ct); return ToContract(contract);
    }

    public async Task<ContractResponse> SubmitContractReviewAsync(Guid leadId, SubmitContractReviewRequest request, CancellationToken ct)
    {
        EnsureContractDraftAccess(); var lead = await GetLeadAsync(leadId, ct); EnsureExpectedVersion(lead, request.ExpectedVersion); if (lead.State != LeadState.ContractDrafting) throw new ConflictException("Only a drafted contract can be submitted for review."); var contract = await GetContractEntityAsync(leadId, ct);
        var documents = await processes.ListDocumentsAsync(currentUser.OrganizationId, leadId, ct);
        if (!new[] { "FLOOR_PLAN", "PERSPECTIVE" }.All(type => documents.Any(x => x.DocumentType == type && x.Status == DocumentStatus.Uploaded))) throw new ConflictException("A floor plan and perspective document are required before contract review.");
        contract.SubmitReview(); lead.MoveTo(LeadState.ContractDrafting, LeadState.ContractReview); await AddAuditAsync(lead, ActivityType.ContractSubmitted, "Contract submitted for General Manager review.", ct); await notifications.NotifyRoleAsync(currentUser.OrganizationId, UserRole.GeneralManager, "contract-submitted-for-review", "Contract review required", $"The contract for {lead.FullName} is ready for General Manager review.", ct); await processes.SaveChangesAsync(ct); return ToContract(contract);
    }

    public async Task<ContractResponse> ApproveContractAsync(Guid leadId, ApproveContractRequest request, CancellationToken ct)
    {
        if (currentUser.Role != UserRole.GeneralManager) throw new ForbiddenException("Only the General Manager can approve a contract.");
        var lead = await GetLeadAsync(leadId, ct); EnsureExpectedVersion(lead, request.ExpectedVersion); if (lead.State != LeadState.ContractReview) throw new ConflictException("Only a contract in review can be approved.");
        var contract = await GetContractEntityAsync(leadId, ct); if (!ChecklistIsComplete(contract)) throw new ConflictException("All required contract review checklist items must be complete before approval."); contract.Approve(currentUser.UserId); await AddAuditAsync(lead, ActivityType.ContractApproved, $"Contract approved. {request.Notes}", ct); await notifications.NotifyUserAsync(currentUser.OrganizationId, lead.AssignedAgentId, "contract-approved", "Contract approved for signing", $"The General Manager approved the contract for {lead.FullName}. Create the secure signing links for both parties.", ct); await processes.SaveChangesAsync(ct); return ToContract(contract);
    }

    public async Task<ContractResponse> RequestRevisionAsync(Guid leadId, RevisionRequest request, CancellationToken ct)
    {
        if (currentUser.Role != UserRole.GeneralManager) throw new ForbiddenException("Only the General Manager can request a contract revision.");
        var lead = await GetLeadAsync(leadId, ct); EnsureExpectedVersion(lead, request.ExpectedVersion); if (lead.State != LeadState.ContractReview) throw new ConflictException("Only a contract in review can be returned for revision.");
        var contract = await GetContractEntityAsync(leadId, ct); contract.RequestRevision(); lead.MoveTo(LeadState.ContractReview, LeadState.ContractDrafting); await AddAuditAsync(lead, ActivityType.ContractRevisionRequested, $"Contract returned for revision: {request.Reason}", ct); await processes.SaveChangesAsync(ct); return ToContract(contract);
    }

    public async Task<ContractChecklistResponse> GetContractChecklistAsync(Guid leadId, CancellationToken ct)
    {
        EnsureContractRead();
        var contract = await GetContractEntityAsync(leadId, ct);
        var items = string.IsNullOrWhiteSpace(contract.ReviewChecklistJson) || contract.ReviewChecklistJson == "[]" ? DefaultChecklist() : JsonSerializer.Deserialize<List<ContractChecklistItem>>(contract.ReviewChecklistJson) ?? [];
        return new ContractChecklistResponse(leadId, items, items.Where(x => x.Required).All(x => x.Complete));
    }

    public async Task<ContractChecklistResponse> UpdateContractChecklistAsync(Guid leadId, ContractChecklistResponse request, CancellationToken ct)
    {
        EnsureContractChecklistAccess();
        var lead = await GetLeadAsync(leadId, ct);
        EnsureExpectedVersion(lead, request.ExpectedVersion);
        var contract = await GetContractEntityAsync(leadId, ct);
        if (request.LeadId != leadId || request.Items.Count == 0 || request.Items.Count > 50) throw new ValidationException("A valid contract checklist is required.");
        if (request.Items.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name))) throw new ValidationException("Checklist item code and name are required.");
        contract.UpdateReviewChecklist(JsonSerializer.Serialize(request.Items)); await processes.SaveChangesAsync(ct);
        return new ContractChecklistResponse(leadId, request.Items, request.Items.Where(x => x.Required).All(x => x.Complete), lead.Version);
    }

    public async Task<ContractSignatureResponse> AddSignatureAsync(Guid leadId, string routeRole, ContractSignatureRequest request, CancellationToken ct)
    {
        await Task.CompletedTask;
        throw new ValidationException("Manual signature proof is disabled. Create a secure electronic signing request instead.");
    }

    public async Task<EndorsementResponse> CreateEndorsementAsync(Guid leadId, CreateEndorsementRequest request, CancellationToken ct)
    {
        EnsureMarketingWrite(); var lead = await GetLeadAsync(leadId, ct); EnsureExpectedVersion(lead, request.ExpectedVersion); if (lead.State != LeadState.PreLaunch) throw new ConflictException("An opportunity must complete pre-launch before it can be endorsed.");
        var checklist = await checklists.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new ConflictException("Pre-launch must be initialized and completed before endorsement.");
        if (checklist.Status != "COMPLETED") throw new ConflictException("Pre-launch must be explicitly completed before endorsement.");
        if (!checklist.Items.Where(x => x.Required).All(x => x.Complete)) throw new ConflictException("All required pre-launch items must be complete before endorsement.");
        if ((await processes.ListEndorsementsAsync(currentUser.OrganizationId, ct)).Any(x => x.LeadId == leadId && x.Status == EndorsementStatus.Pending)) throw new ConflictException("A pending endorsement already exists for this opportunity.");
        if (!request.ReceivingTeam.Trim().Equals("Admin Team", StringComparison.OrdinalIgnoreCase) &&
            !request.ReceivingTeam.Trim().Equals("AdminTeam", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("The marketing workflow can only be endorsed to the Admin Team.");
        var boundary = request.Items is { Count: > 0 }
            ? request.Items
            : checklist.Items
                .Select(x => new HandoffBoundaryItem(x.Code, x.Name, x.Complete, false, x.Notes))
                .Append(new HandoffBoundaryItem("ADMIN_ONBOARDING", "Admin onboarding and downstream operations", false, true))
                .ToArray();
        var endorsement = new Endorsement(currentUser.OrganizationId, leadId, currentUser.UserId, "Admin Team", request.HandoffNotes.Trim(), JsonSerializer.Serialize(boundary));
        await processes.AddEndorsementAsync(endorsement, ct); lead.MoveTo(LeadState.PreLaunch, LeadState.EndorsedToAdmin); await AddAuditAsync(lead, ActivityType.EndorsementCreated, "Endorsement record created and handed to Admin.", ct); await notifications.NotifyRoleAsync(currentUser.OrganizationId, UserRole.AdminTeam, "endorsement-created", "Endorsement ready", $"{lead.FullName} has been endorsed to the Admin Team.", ct); await processes.SaveChangesAsync(ct); return ToEndorsement(endorsement);
    }

    public async Task<EndorsementResponse?> GetEndorsementAsync(Guid leadId, CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.AdminTeam or UserRole.Leadership)) throw new ForbiddenException("This role cannot view endorsements.");
        await GetLeadAsync(leadId, ct);
        return (await processes.ListEndorsementsAsync(currentUser.OrganizationId, ct)).Where(x => x.LeadId == leadId).OrderByDescending(x => x.CreatedAt).Select(ToEndorsement).FirstOrDefault();
    }

    public async Task<IReadOnlyList<EndorsementResponse>> ListEndorsementsAsync(CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.AdminTeam or UserRole.Leadership)) throw new ForbiddenException("This role cannot view endorsements.");
        var items = await processes.ListEndorsementsAsync(currentUser.OrganizationId, ct);
        if (currentUser.Role == UserRole.MarketingAgent)
        {
            var visible = new List<Endorsement>();
            foreach (var item in items)
            {
                var lead = await leads.GetAsync(currentUser.OrganizationId, item.LeadId, ct);
                if (lead?.AssignedAgentId == currentUser.UserId) visible.Add(item);
            }
            items = visible;
        }
        return items.Select(ToEndorsement).ToArray();
    }

    public async Task<EndorsementResponse> AcknowledgeEndorsementAsync(Guid endorsementId, CancellationToken ct)
    {
        if (currentUser.Role != UserRole.AdminTeam) throw new ForbiddenException("Only the Admin Team can acknowledge an endorsement.");
        var item = await processes.GetEndorsementAsync(currentUser.OrganizationId, endorsementId, ct) ?? throw new NotFoundException("Endorsement not found."); item.Acknowledge(currentUser.UserId); var lead = await GetLeadAsync(item.LeadId, ct); await AddAuditAsync(lead, ActivityType.EndorsementAcknowledged, "Admin acknowledged the endorsement.", ct); await processes.SaveChangesAsync(ct); return ToEndorsement(item);
    }

    public async Task<IReadOnlyList<NotificationResponse>> ListNotificationsAsync(CancellationToken ct) => (await processes.ListNotificationsAsync(currentUser.OrganizationId, currentUser.UserId, ct)).Select(ToNotification).ToArray();

    public async Task<NotificationResponse> MarkNotificationReadAsync(Guid notificationId, CancellationToken ct)
    {
        var item = await processes.GetNotificationAsync(currentUser.OrganizationId, currentUser.UserId, notificationId, ct) ?? throw new NotFoundException("Notification not found."); item.MarkRead(); await processes.SaveChangesAsync(ct); return ToNotification(item);
    }

    private async Task<Lead> GetLeadAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Lead not found.");
        if (currentUser.Role == UserRole.MarketingAgent && lead.AssignedAgentId != currentUser.UserId) throw new ForbiddenException("You do not have access to this lead.");
        return lead;
    }
    private async Task<Contract> GetContractEntityAsync(Guid leadId, CancellationToken ct) => await processes.GetContractAsync(currentUser.OrganizationId, leadId, ct) ?? throw new NotFoundException("Contract not found.");
    private void EnsureMarketingWrite() { if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin)) throw new ForbiddenException("Only marketing users can perform this action."); }
    private void EnsureDocumentWrite(string documentType)
    {
        var type = documentType.Trim().ToUpperInvariant();
        if (currentUser.Role == UserRole.Finance && type == "PAYMENT_RECEIPT") return;
        if (type == "PAYMENT_RECEIPT") throw new ForbiddenException("Only Finance can upload payment evidence.");
        if (currentUser.Role is UserRole.MarketingAgent or UserRole.MarketingAdmin) return;
        throw new ForbiddenException("Your role cannot upload this type of document.");
    }
    private void EnsureDownPaymentRead() { if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Finance or UserRole.AdminTeam or UserRole.Leadership)) throw new ForbiddenException("This role cannot view down-payment status."); }
    private void EnsureDocumentRead() { if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Finance or UserRole.Leadership)) throw new ForbiddenException("This role cannot view franchise documents."); }
    private void EnsureContractRead() { if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership)) throw new ForbiddenException("This role cannot view contracts."); }
    private void EnsureContractChecklistAccess() { if (currentUser.Role is not (UserRole.MarketingAdmin or UserRole.GeneralManager)) throw new ForbiddenException("Only the Marketing Admin or General Manager can update the contract review checklist."); }
    private void EnsureContractDraftAccess() { if (currentUser.Role != UserRole.MarketingAdmin) throw new ForbiddenException("Only the Marketing Admin can draft or submit a contract for review."); }
    private static void EnsureExpectedVersion(Lead lead, int? expectedVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != lead.Version)
            throw new ConflictException("The lead was changed by another user. Refresh and try again.");
    }
    private static void EnsureDownPaymentStage(Lead lead)
    {
        if (lead.State is not (LeadState.Qualified or LeadState.DownPaymentPending)) throw new ConflictException("The lead must be qualified before the down payment can be prepared.");
    }
    private static void EnsureContractDraftStage(Lead lead)
    {
        if (lead.State is not (LeadState.DownPaymentConfirmed or LeadState.ContractDrafting or LeadState.ContractReview)) throw new ConflictException("Payment must be confirmed before the contract can be drafted.");
    }
    private static bool ChecklistIsComplete(Contract contract)
    {
        if (string.IsNullOrWhiteSpace(contract.ReviewChecklistJson) || contract.ReviewChecklistJson == "[]") return false;
        var items = JsonSerializer.Deserialize<List<ContractChecklistItem>>(contract.ReviewChecklistJson) ?? [];
        return items.Count > 0 && items.Where(x => x.Required).All(x => x.Complete);
    }
    private async Task<bool> DocumentsCompleteAsync(Guid leadId, CancellationToken ct)
    {
        var docs = await processes.ListDocumentsAsync(currentUser.OrganizationId, leadId, ct);
        if (docs.Any(x => x.DocumentType == "VALID_ID_SIGNATURES" && x.Status == DocumentStatus.Uploaded)) return true;
        // Keep previously uploaded legacy records valid during the document-model transition.
        return new[] { "VALID_ID", "SPECIMEN_SIGNATURE_1", "SPECIMEN_SIGNATURE_2", "SPECIMEN_SIGNATURE_3" }
            .All(type => docs.Any(x => x.DocumentType == type && x.Status == DocumentStatus.Uploaded));
    }
    private static string NormalizeCurrency(string currency) { var value = currency.Trim().ToUpperInvariant(); if (value.Length != 3 || value.Any(c => c < 'A' || c > 'Z')) throw new ValidationException("Currency must be a three-letter code."); return value; }
    private static void ValidateDocumentRequest(CreateUploadIntentRequest request)
    {
        var types = new[] { "VALID_ID_SIGNATURES", "FLOOR_PLAN", "PERSPECTIVE", "SIGNED_CONTRACT", "PAYMENT_RECEIPT" };
        var contentTypes = new[] { "application/pdf", "image/jpeg", "image/png" };
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        if (!types.Contains(request.DocumentType.Trim().ToUpperInvariant())) throw new ValidationException("Unsupported document type.");
        if (!contentTypes.Contains(request.ContentType.Trim().ToLowerInvariant())) throw new ValidationException("Unsupported content type.");
        if (!new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(extension)) throw new ValidationException("Unsupported file extension.");
        if ((request.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) && extension != ".pdf") || (request.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) && extension != ".png") || (request.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) && extension is not (".jpg" or ".jpeg"))) throw new ValidationException("File extension does not match content type.");
    }
    private static DownPaymentResponse ToPayment(DownPayment x, bool complete) => new(x.LeadId, x.Amount, x.Currency, x.Status.ToString(), x.InvoiceNumber, x.ConfirmedAt, complete, x.SubmittedAt is not null, x.SubmittedAt, x.SubmittedBy);
    private static DocumentResponse ToDocument(LeadDocument x) => new(x.Id, x.LeadId, x.DocumentType, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString(), x.CreatedAt);
    private static string DocumentTypeName(string type) => type.ToUpperInvariant() switch
    {
        "VALID_ID_SIGNATURES" => "Valid ID and specimen signatures",
        "FLOOR_PLAN" => "Floor plan",
        "PERSPECTIVE" => "Perspective",
        "SIGNED_CONTRACT" => "Signed contract",
        "PAYMENT_RECEIPT" => "Payment receipt or bank confirmation",
        _ => "Document"
    };
    private static ContractResponse ToContract(Contract x) => new(x.LeadId, x.Id, x.Status.ToString(), x.TemplateCode, x.TemplateVersion, x.UpdatedAt, null, x.GmApproved, x.FranchiseeSignerName, x.FranchiseeSignedAt, x.DrCareSignerName, x.DrCareSignedAt);
    private static List<ContractChecklistItem> DefaultChecklist() => [
        new(Guid.NewGuid(), "IDENTITY_VERIFIED", "Franchisee identity verified", true, false, null),
        new(Guid.NewGuid(), "COMMERCIAL_TERMS_REVIEWED", "Commercial terms reviewed", true, false, null),
        new(Guid.NewGuid(), "CONTRACT_DATA_COMPLETE", "Contract data complete", true, false, null)];
    private static EndorsementResponse ToEndorsement(Endorsement x) => new(x.Id, x.LeadId, x.ReceivingTeam, x.Status.ToString(), x.HandoffNotes, JsonSerializer.Deserialize<List<HandoffBoundaryItem>>(x.BoundaryJson) ?? [], x.CreatedAt, x.AcknowledgedAt);
    private async Task AddAuditAsync(Lead lead, ActivityType type, string message, CancellationToken ct) { var activity = new ActivityLog(currentUser.OrganizationId, lead.Id, currentUser.UserId, type, message); lead.AddActivity(activity); await leads.AddActivityAsync(activity, ct); }
    private static string Sha256(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string RenderTemplate(string folder, string fileName, IReadOnlyDictionary<string, string?> values)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Templates", folder, fileName);
        if (!File.Exists(path)) throw new InvalidOperationException($"Document template '{fileName}' is missing.");
        var html = File.ReadAllText(path);
        foreach (var pair in values) html = html.Replace("{{" + pair.Key + "}}", WebUtility.HtmlEncode(pair.Value ?? string.Empty), StringComparison.Ordinal);
        return html;
    }
    private static NotificationResponse ToNotification(Notification x) => new(x.Id, x.Type, x.Title, x.Message, x.Read, x.CreatedAt);
}
