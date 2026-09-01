using DrCare.Application.Contracts;
using DrCare.Application.Processes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/leads"), Authorize, EnableRateLimiting("api")]
public sealed class LeadProcessController(IProcessService processes) : ControllerBase
{
    [HttpPatch("{leadId:guid}/down-payment")]
    public async Task<ActionResult<DownPaymentResponse>> UpdateDownPayment(Guid leadId, CreateInvoiceRequest request, CancellationToken ct) => Ok(await processes.UpdateDownPaymentAsync(leadId, request, ct));
    [HttpGet("{leadId:guid}/down-payment")]
    public async Task<ActionResult<DownPaymentResponse>> GetDownPayment(Guid leadId, CancellationToken ct) => Ok(await processes.GetDownPaymentAsync(leadId, ct));
    [HttpPost("{leadId:guid}/down-payment/generate-invoice")]
    public async Task<ActionResult<InvoiceResponse>> GenerateInvoice(Guid leadId, CreateInvoiceRequest request, CancellationToken ct) => Ok(await processes.GenerateInvoiceAsync(leadId, request, ct));
    [HttpPost("{leadId:guid}/down-payment/submit-finance")]
    public async Task<ActionResult<DownPaymentResponse>> SubmitForFinance(Guid leadId, SubmitDownPaymentRequest request, CancellationToken ct) => Ok(await processes.SubmitDownPaymentForFinanceAsync(leadId, request, ct));
    [HttpGet("{leadId:guid}/down-payment/invoice")]
    public async Task<ActionResult<DownPaymentResponse>> GetInvoice(Guid leadId, CancellationToken ct) => Ok(await processes.GetDownPaymentAsync(leadId, ct));
    [HttpGet("{leadId:guid}/down-payment/invoice/download-url")]
    public async Task<ActionResult<DocumentDownloadResponse>> GetInvoiceDownload(Guid leadId, CancellationToken ct) => Ok(await processes.GetInvoiceDownloadAsync(leadId, ct));
    [HttpPost("{leadId:guid}/down-payment/confirm"), Authorize(Roles = "Finance")]
    public async Task<ActionResult<ConfirmDownPaymentResponse>> ConfirmDownPayment(Guid leadId, ConfirmDownPaymentRequest request, CancellationToken ct) => Ok(await processes.ConfirmDownPaymentAsync(leadId, request, ct));

    [HttpGet("{leadId:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> ListDocuments(Guid leadId, CancellationToken ct) => Ok(await processes.ListDocumentsAsync(leadId, ct));
    [HttpPost("{leadId:guid}/documents/upload-intents")]
    public async Task<ActionResult<UploadIntentResponse>> CreateUploadIntent(Guid leadId, CreateUploadIntentRequest request, CancellationToken ct) => Ok(await processes.CreateUploadIntentAsync(leadId, request, ct));
    [HttpPost("{leadId:guid}/documents/{documentId:guid}/complete")]
    public async Task<ActionResult<CompleteUploadResponse>> CompleteUpload(Guid leadId, Guid documentId, CompleteUploadRequest request, CancellationToken ct) => Ok(await processes.CompleteUploadAsync(leadId, documentId, request, ct));
    [HttpGet("{leadId:guid}/documents/{documentId:guid}/download-url")]
    public async Task<ActionResult<DocumentDownloadResponse>> CreateDownloadUrl(Guid leadId, Guid documentId, CancellationToken ct) => Ok(await processes.CreateDownloadUrlAsync(leadId, documentId, ct));
    [HttpPost("{leadId:guid}/documents/{documentId:guid}/archive")]
    public async Task<ActionResult<DocumentResponse>> ArchiveDocument(Guid leadId, Guid documentId, CancellationToken ct) => Ok(await processes.ArchiveDocumentAsync(leadId, documentId, ct));

    [HttpGet("{leadId:guid}/contract")]
    public async Task<ActionResult<ContractResponse>> GetContract(Guid leadId, CancellationToken ct)
    {
        var result = await processes.GetContractAsync(leadId, ct);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("{leadId:guid}/contract/download-url")]
    public async Task<ActionResult<DocumentDownloadResponse>> GetContractDownload(Guid leadId, CancellationToken ct) => Ok(await processes.GetContractDownloadAsync(leadId, ct));
    [HttpPost("{leadId:guid}/contract/generate")]
    public async Task<ActionResult<ContractResponse>> GenerateContract(Guid leadId, GenerateContractRequest request, CancellationToken ct) => Ok(await processes.GenerateContractAsync(leadId, request, ct));
    [HttpPatch("{leadId:guid}/contract")]
    public async Task<ActionResult<ContractResponse>> UpdateContract(Guid leadId, GenerateContractRequest request, CancellationToken ct) => Ok(await processes.UpdateContractAsync(leadId, request, ct));
    [HttpPost("{leadId:guid}/contract/submit-review")]
    public async Task<ActionResult<ContractResponse>> SubmitContractReview(Guid leadId, SubmitContractReviewRequest request, CancellationToken ct) => Ok(await processes.SubmitContractReviewAsync(leadId, request, ct));
    [HttpGet("{leadId:guid}/contract/review-checklist")]
    public async Task<ActionResult<ContractChecklistResponse>> GetContractChecklist(Guid leadId, CancellationToken ct) => Ok(await processes.GetContractChecklistAsync(leadId, ct));
    [HttpPut("{leadId:guid}/contract/review-checklist")]
    public async Task<ActionResult<ContractChecklistResponse>> UpdateContractChecklist(Guid leadId, ContractChecklistResponse request, CancellationToken ct) => Ok(await processes.UpdateContractChecklistAsync(leadId, request, ct));
    [HttpPost("{leadId:guid}/contract/approve"), Authorize(Roles = "GeneralManager")]
    public async Task<ActionResult<ContractResponse>> ApproveContract(Guid leadId, ApproveContractRequest request, CancellationToken ct) => Ok(await processes.ApproveContractAsync(leadId, request, ct));
    [HttpPost("{leadId:guid}/contract/request-revision"), Authorize(Roles = "GeneralManager")]
    public async Task<ActionResult<ContractResponse>> RequestRevision(Guid leadId, RevisionRequest request, CancellationToken ct) => Ok(await processes.RequestRevisionAsync(leadId, request, ct));
    [HttpPost("{leadId:guid}/contract/signatures/franchisee")]
    public async Task<ActionResult<ContractSignatureResponse>> AddFranchiseeSignature(Guid leadId, ContractSignatureRequest request, CancellationToken ct) => Ok(await processes.AddSignatureAsync(leadId, "franchisee", request, ct));
    [HttpPost("{leadId:guid}/contract/signatures/dr-care")]
    public async Task<ActionResult<ContractSignatureResponse>> AddDrCareSignature(Guid leadId, ContractSignatureRequest request, CancellationToken ct) => Ok(await processes.AddSignatureAsync(leadId, "dr-care", request, ct));

    [HttpPost("{leadId:guid}/endorsement")]
    public async Task<ActionResult<EndorsementResponse>> CreateEndorsement(Guid leadId, CreateEndorsementRequest request, CancellationToken ct) => Ok(await processes.CreateEndorsementAsync(leadId, request, ct));
    [HttpGet("{leadId:guid}/endorsement")]
    public async Task<ActionResult<EndorsementResponse>> GetEndorsement(Guid leadId, CancellationToken ct)
    {
        var result = await processes.GetEndorsementAsync(leadId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

[ApiController, Route("api/v1/endorsements"), Authorize, EnableRateLimiting("api")]
public sealed class EndorsementsController(IProcessService processes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EndorsementResponse>>> List(CancellationToken ct) => Ok(await processes.ListEndorsementsAsync(ct));
    [HttpGet("{endorsementId:guid}")]
    public async Task<ActionResult<EndorsementResponse>> Get(Guid endorsementId, CancellationToken ct) => Ok((await processes.ListEndorsementsAsync(ct)).FirstOrDefault(x => x.Id == endorsementId));
    [HttpPost("{endorsementId:guid}/acknowledge"), Authorize(Roles = "AdminTeam")]
    public async Task<ActionResult<EndorsementResponse>> Acknowledge(Guid endorsementId, CancellationToken ct) => Ok(await processes.AcknowledgeEndorsementAsync(endorsementId, ct));
}

[ApiController, Route("api/v1/notifications"), Authorize, EnableRateLimiting("api")]
public sealed class NotificationsController(IProcessService processes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationResponse>>> List(CancellationToken ct) => Ok(await processes.ListNotificationsAsync(ct));
    [HttpPost("{notificationId:guid}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid notificationId, CancellationToken ct) => Ok(await processes.MarkNotificationReadAsync(notificationId, ct));
}
