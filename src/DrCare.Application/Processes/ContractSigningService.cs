using System.Security.Cryptography;
using System.Text;
using DrCare.Application.Contracts;
using DrCare.Application.Notifications;
using DrCare.Domain;

namespace DrCare.Application.Processes;

public interface IContractSigningService
{
    Task<IReadOnlyList<SigningRequestResponse>> ListAsync(Guid leadId, CancellationToken ct);
    Task<SigningRequestResponse> CreateAsync(Guid leadId, CreateSigningRequest request, CancellationToken ct);
    Task<SigningRequestResponse> VoidAsync(Guid leadId, Guid requestId, CancellationToken ct);
    Task<PublicSigningResponse> GetPublicAsync(string token, CancellationToken ct);
    Task<PublicSigningResponse> SignPublicAsync(string token, SignContractRequest request, string? ipAddress, string? userAgent, CancellationToken ct);
    Task DeclinePublicAsync(string token, DeclineSigningRequest request, CancellationToken ct);
}

public sealed class ContractSigningService(IProcessRepository processes, ILeadRepository leads, IPrivateObjectStorage storage, IDocumentPdfRenderer renderer, ICurrentUser currentUser, INotificationService notifications, IEmailQueue emailQueue, IEmailComposer emailComposer, IApplicationLinks links) : IContractSigningService
{
    public async Task<IReadOnlyList<SigningRequestResponse>> ListAsync(Guid leadId, CancellationToken ct)
    {
        var (lead, contract) = await GetContractAsync(currentUser.OrganizationId, leadId, ct);
        EnsureContractRead(lead);
        return (await processes.ListSigningRequestsAsync(currentUser.OrganizationId, contract.Id, ct)).Select(x => ToResponse(x)).ToArray();
    }

    public async Task<SigningRequestResponse> CreateAsync(Guid leadId, CreateSigningRequest request, CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin)) throw new ForbiddenException("Only the Agent or Marketing Admin can create a contract signing request.");
        var (lead, contract) = await GetContractAsync(currentUser.OrganizationId, leadId, ct); EnsureContractRead(lead);
        if (lead.State != LeadState.ContractReview || !contract.GmApproved || contract.Status == ContractStatus.Signed) throw new ConflictException("The contract must be approved by the General Manager before signing.");
        if (string.IsNullOrWhiteSpace(contract.CurrentPdfObjectKey)) throw new ConflictException("Generate the contract document before requesting signatures.");
        var existing = await processes.ListSigningRequestsAsync(currentUser.OrganizationId, contract.Id, ct);
        foreach (var item in existing.Where(x => x.SignerRole.Equals(request.SignerRole, StringComparison.OrdinalIgnoreCase) && x.Status is SigningRequestStatus.Pending or SigningRequestStatus.Viewed)) { item.Void(); if (item.ReminderEmailId.HasValue) await emailQueue.CancelAsync(item.ReminderEmailId.Value, ct); }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entity = new ContractSigningRequest(currentUser.OrganizationId, contract.Id, lead.Id, request.SignerRole, request.SignerName, request.SignerEmail, HashToken(token), DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays));
        await processes.AddSigningRequestAsync(entity, ct);
        await AddAuditAsync(lead, currentUser.UserId, ActivityType.ContractSigningRequested, $"Electronic signing requested for {request.SignerRole}.", ct);
        var signingUrl = links.ContractSigning(token);
        var invite = emailComposer.Compose(new BrandedEmailRequest(lead.ProductLine, "A Dr. Care contract is ready for your signature.", "Contract signature requested", $"Hello {entity.SignerName},",
            [$"You have been invited to review and electronically sign the franchise agreement as {entity.SignerRole}.", "Use the secure link below before it expires."], "Review and sign", signingUrl,
            [new("Franchisee", lead.FullName), new("Expires", entity.ExpiresAt.ToString("MMMM d, yyyy 'at' h:mm tt 'UTC'"))]));
        var reminder = emailComposer.Compose(new BrandedEmailRequest(lead.ProductLine, "Reminder: your signature request will expire soon.", "Contract signature reminder", $"Hello {entity.SignerName},",
            ["Your Dr. Care contract is still waiting for your electronic signature.", "Please review and sign it before the secure request expires."], "Review and sign", signingUrl,
            [new("Franchisee", lead.FullName), new("Expires", entity.ExpiresAt.ToString("MMMM d, yyyy 'at' h:mm tt 'UTC'"))]));
        var reminderAt = entity.ExpiresAt.AddHours(-24);
        var ids = await emailQueue.EnqueueManyAsync([
            new QueueEmailRequest(entity.OrganizationId, entity.SignerEmail, entity.SignerName, "Dr. Care contract signature requested", invite.Html, invite.Text, "contract-signing", IdempotencyKey: $"signing:{entity.Id}:invite"),
            new QueueEmailRequest(entity.OrganizationId, entity.SignerEmail, entity.SignerName, "Reminder: Dr. Care contract signature", reminder.Html, reminder.Text, "contract-signing-reminder", IdempotencyKey: $"signing:{entity.Id}:reminder", NotBefore: reminderAt > DateTimeOffset.UtcNow ? reminderAt : DateTimeOffset.UtcNow)
        ], ct);
        entity.SetEmailDelivery(ids[0], ids[1]);
        await processes.SaveChangesAsync(ct);
        return ToResponse(entity, $"/sign-contract/{token}");
    }

    public async Task<SigningRequestResponse> VoidAsync(Guid leadId, Guid requestId, CancellationToken ct)
    {
        if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin)) throw new ForbiddenException("Only the Agent or Marketing Admin can void a signing request.");
        var (_, contract) = await GetContractAsync(currentUser.OrganizationId, leadId, ct);
        var item = (await processes.ListSigningRequestsAsync(currentUser.OrganizationId, contract.Id, ct)).SingleOrDefault(x => x.Id == requestId) ?? throw new NotFoundException("Signing request not found.");
        item.Void(); if (item.ReminderEmailId.HasValue) await emailQueue.CancelAsync(item.ReminderEmailId.Value, ct);
        var lead = await leads.GetAsync(item.OrganizationId, item.LeadId, ct) ?? throw new NotFoundException("Lead not found.");
        var notice = emailComposer.Compose(new BrandedEmailRequest(lead.ProductLine, "This signing request has been cancelled.", "Contract signing request cancelled", $"Hello {item.SignerName},",
            ["The Dr. Care contract signing request previously sent to you has been cancelled.", "No action is required. A replacement request will be sent if needed."]));
        await emailQueue.EnqueueAsync(new QueueEmailRequest(item.OrganizationId, item.SignerEmail, item.SignerName, "Dr. Care signing request cancelled", notice.Html, notice.Text,
            "contract-signing-voided", IdempotencyKey: $"signing:{item.Id}:voided"), ct); return ToResponse(item);
    }

    public async Task<PublicSigningResponse> GetPublicAsync(string token, CancellationToken ct)
    {
        var item = await GetActiveByTokenAsync(token, ct); item.View(); await processes.SaveChangesAsync(ct);
        var contract = await processes.GetContractAsync(item.OrganizationId, item.LeadId, ct) ?? throw new NotFoundException("Contract not found.");
        var key = contract.SignedPdfObjectKey ?? contract.CurrentPdfObjectKey;
        var documentUrl = string.IsNullOrWhiteSpace(key) ? null : await storage.CreateDownloadUrlAsync(
            key, contract.Status == ContractStatus.Signed ? "Dr-Care-Franchise-Agreement-Signed.pdf" : "Dr-Care-Franchise-Agreement-Draft.pdf",
            "application/pdf", DateTimeOffset.UtcNow.AddMinutes(10), ct);
        return new PublicSigningResponse(item.Id, item.SignerRole, item.SignerName, contract.Status.ToString(), item.ExpiresAt, false, documentUrl);
    }

    public async Task<PublicSigningResponse> SignPublicAsync(string token, SignContractRequest request, string? ipAddress, string? userAgent, CancellationToken ct)
    {
        if (!request.AcceptedTerms) throw new ValidationException("You must accept the electronic signing terms.");
        var item = await GetActiveByTokenAsync(token, ct);
        if (!item.SignerName.Equals(request.SignerName.Trim(), StringComparison.OrdinalIgnoreCase)) throw new ValidationException("Signer name does not match this request.");
        var signature = DecodeSignature(request.SignatureData);
        var contract = await processes.GetContractAsync(item.OrganizationId, item.LeadId, ct) ?? throw new NotFoundException("Contract not found.");
        var lead = await leads.GetAsync(item.OrganizationId, item.LeadId, ct) ?? throw new NotFoundException("Lead not found.");
        if (!contract.GmApproved || contract.Status == ContractStatus.Signed || string.IsNullOrWhiteSpace(contract.CurrentPdfObjectKey)) throw new ConflictException("This contract is not available for signing.");
        await using var currentPdf = await storage.OpenReadAsync(contract.CurrentPdfObjectKey, ct);
        var stampedPdf = await renderer.StampSignatureAsync(currentPdf, signature, item.SignerRole, ct);
        var documentHash = Hash(stampedPdf); var signatureHash = Hash(signature);
        var key = $"private/{item.OrganizationId:N}/leads/{item.LeadId:N}/contracts/{contract.Id:N}-{item.SignerRole}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.pdf";
        await storage.PutAsync(key, "application/pdf", stampedPdf, ct);
        try { contract.ApplyElectronicSignature(item.SignerRole, item.SignerName, signatureHash, DateTimeOffset.UtcNow, key, documentHash); item.Sign(signatureHash, documentHash, stampedPdf.LongLength, ipAddress, Truncate(userAgent, 500)); }
        catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        if (item.ReminderEmailId.HasValue) await emailQueue.CancelAsync(item.ReminderEmailId.Value, ct);
        if (contract.Status == ContractStatus.Signed)
        {
            try { lead.MoveTo(LeadState.ContractReview, LeadState.ContractSigned); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
            await AddAuditAsync(lead, Guid.Empty, ActivityType.ContractSigned, "Both parties electronically signed the contract; the final PDF was stored privately.", ct);
            await notifications.NotifyUserAsync(item.OrganizationId, lead.AssignedAgentId, "contract-signed", "Contract signed", $"Both parties signed the contract for {lead.FullName}. Review the final agreement, then start the pre-launch checklist.", ct);
            var recipients = (await processes.ListSigningRequestsAsync(item.OrganizationId, contract.Id, ct)).Where(x => x.Status == SigningRequestStatus.Signed).GroupBy(x => x.SignerEmail).Select(x => x.First()).ToArray();
            var messages = recipients.Select(signer =>
            {
                var final = emailComposer.Compose(new BrandedEmailRequest(lead.ProductLine, "Your fully signed Dr. Care agreement is attached.", "Franchise agreement completed", $"Hello {signer.SignerName},",
                    ["Both parties have completed the electronic signing process.", "A copy of the fully signed franchise agreement is attached for your records."]));
                return new QueueEmailRequest(item.OrganizationId, signer.SignerEmail, signer.SignerName, "Your signed Dr. Care franchise agreement", final.Html, final.Text,
                    "contract-final", [new QueuedEmailAttachment("Dr-Care-Franchise-Agreement-Signed.pdf", "application/pdf", contract.SignedPdfObjectKey!)], $"contract:{contract.Id}:final:{signer.Id}");
            }).ToArray();
            await emailQueue.EnqueueManyAsync(messages, ct);
        }
        else await AddAuditAsync(lead, Guid.Empty, ActivityType.ContractSigned, $"{item.SignerRole} electronic signature recorded.", ct);
        await processes.SaveChangesAsync(ct);
        return new PublicSigningResponse(item.Id, item.SignerRole, item.SignerName, contract.Status.ToString(), item.ExpiresAt, true);
    }

    public async Task DeclinePublicAsync(string token, DeclineSigningRequest request, CancellationToken ct)
    {
        var item = await GetActiveByTokenAsync(token, ct); try { item.Decline(request.Reason); } catch (DomainRuleException ex) { throw new ConflictException(ex.Message); }
        if (item.ReminderEmailId.HasValue) await emailQueue.CancelAsync(item.ReminderEmailId.Value, ct);
        var lead = await leads.GetAsync(item.OrganizationId, item.LeadId, ct) ?? throw new NotFoundException("Lead not found.");
        var notice = emailComposer.Compose(new BrandedEmailRequest(lead.ProductLine, "Your decision has been recorded.", "Contract signature declined", $"Hello {item.SignerName},",
            ["We recorded that you declined the Dr. Care contract signing request.", "The assigned representative will follow up if clarification or a revised agreement is needed."],
            Details: string.IsNullOrWhiteSpace(item.DeclineReason) ? null : [new("Reason", item.DeclineReason)]));
        await emailQueue.EnqueueAsync(new QueueEmailRequest(item.OrganizationId, item.SignerEmail, item.SignerName, "Dr. Care contract signing declined", notice.Html, notice.Text,
            "contract-signing-declined", IdempotencyKey: $"signing:{item.Id}:declined"), ct);
        await notifications.NotifyUserAsync(item.OrganizationId, lead.AssignedAgentId, "contract-signing-declined", "Contract signature declined", $"{item.SignerName} declined the signing request for {lead.FullName}.", ct);
    }

    private async Task<ContractSigningRequest> GetActiveByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 200) throw new NotFoundException("Signing request not found.");
        var item = await processes.GetSigningRequestByTokenHashAsync(HashToken(token), ct) ?? throw new NotFoundException("Signing request not found.");
        if (item.ExpiresAt <= DateTimeOffset.UtcNow || item.Status is SigningRequestStatus.Expired or SigningRequestStatus.Voided or SigningRequestStatus.Declined) throw new ConflictException("This signing request is no longer active.");
        if (item.Status == SigningRequestStatus.Signed) throw new ConflictException("This signing request has already been completed.");
        return item;
    }
    private async Task<(Lead Lead, Contract Contract)> GetContractAsync(Guid organizationId, Guid leadId, CancellationToken ct) =>
        (await leads.GetAsync(organizationId, leadId, ct) ?? throw new NotFoundException("Lead not found."), await processes.GetContractAsync(organizationId, leadId, ct) ?? throw new NotFoundException("Contract not found."));
    private void EnsureContractRead(Lead lead) { if (currentUser.Role == UserRole.MarketingAgent && lead.AssignedAgentId != currentUser.UserId) throw new ForbiddenException("You do not have access to this contract."); if (currentUser.Role is not (UserRole.MarketingAgent or UserRole.MarketingAdmin or UserRole.GeneralManager or UserRole.Leadership)) throw new ForbiddenException("This role cannot access contracts."); }
    private async Task AddAuditAsync(Lead lead, Guid actorId, ActivityType type, string message, CancellationToken ct) { var activity = new ActivityLog(lead.OrganizationId, lead.Id, actorId, type, message); lead.AddActivity(activity); await leads.AddActivityAsync(activity, ct); }
    private static byte[] DecodeSignature(string data)
    {
        const string prefix = "data:image/png;base64,"; if (!data.StartsWith(prefix, StringComparison.Ordinal)) throw new ValidationException("Signature must be a PNG data URL.");
        byte[] bytes; try { bytes = Convert.FromBase64String(data[prefix.Length..]); } catch (FormatException) { throw new ValidationException("Signature image is invalid."); }
        if (bytes.Length is < 100 or > 250_000 || !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ValidationException("Signature must be a valid PNG between 100 bytes and 250 KB."); return bytes;
    }
    private static string HashToken(string token) => Hash(Encoding.UTF8.GetBytes(token));
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string? Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
    private static SigningRequestResponse ToResponse(ContractSigningRequest x, string? url = null) => new(x.Id, x.LeadId, x.ContractId, x.SignerRole, x.SignerName, x.SignerEmail, x.Status.ToString(), x.ExpiresAt, x.SignedAt, x.InvitationEmailId.HasValue, url);
}
