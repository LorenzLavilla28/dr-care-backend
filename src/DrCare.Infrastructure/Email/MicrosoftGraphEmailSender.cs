using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Email;

public sealed record EmailDeliveryAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailDeliveryMessage(
    string RecipientEmail,
    string? RecipientName,
    string Subject,
    string HtmlBody,
    string? TextBody,
    IReadOnlyList<EmailDeliveryAttachment> Attachments);

public interface IEmailDeliverySender
{
    Task SendAsync(EmailDeliveryMessage message, CancellationToken cancellationToken);
}

public sealed class MicrosoftGraphEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options) : IEmailDeliverySender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly EmailOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public async Task SendAsync(EmailDeliveryMessage message, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) throw new InvalidOperationException("Email delivery is disabled.");

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var attachments = message.Attachments.Count == 0
            ? null
            : message.Attachments.Select(x => new GraphFileAttachment(x.FileName, x.ContentType, Convert.ToBase64String(x.Content))).ToArray();
        var payload = new GraphSendMailRequest(
            new GraphMessage(
                message.Subject,
                new GraphBody("HTML", message.HtmlBody),
                [new GraphRecipient(new GraphEmailAddress(message.RecipientEmail, message.RecipientName))],
                new GraphRecipient(new GraphEmailAddress(_options.FromAddress, _options.FromName)),
                attachments),
            SaveToSentItems: true);

        var client = httpClientFactory.CreateClient("MicrosoftGraphEmail");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_options.FromAddress)}/sendMail")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Microsoft Graph sendMail failed ({(int)response.StatusCode}): {Limit(detail)}");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            var client = httpClientFactory.CreateClient("MicrosoftGraphEmail");
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            });
            using var response = await client.PostAsync(
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId)}/oauth2/v2.0/token",
                content,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Microsoft identity token request failed ({(int)response.StatusCode}): {Limit(body)}");
            var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                ?? throw new InvalidOperationException("Microsoft identity returned an empty token response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken)) throw new InvalidOperationException("Microsoft identity returned an empty access token.");
            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string Limit(string value) => value.Length <= 2000 ? value : value[..2000];

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
    private sealed record GraphSendMailRequest(GraphMessage Message, bool SaveToSentItems);
    private sealed record GraphMessage(
        string Subject,
        GraphBody Body,
        IReadOnlyList<GraphRecipient> ToRecipients,
        GraphRecipient From,
        IReadOnlyList<GraphFileAttachment>? Attachments);
    private sealed record GraphBody(string ContentType, string Content);
    private sealed record GraphRecipient(GraphEmailAddress EmailAddress);
    private sealed record GraphEmailAddress(string Address, string? Name);
    private sealed record GraphFileAttachment(
        string Name,
        string ContentType,
        string ContentBytes)
    {
        [JsonPropertyName("@odata.type")]
        public string ODataType => "#microsoft.graph.fileAttachment";
    }
}
