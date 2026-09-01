namespace DrCare.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Dr. Care Medical Group";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public int SigningReminderHoursBeforeExpiry { get; set; } = 24;
    public EmailDigestOptions Digests { get; set; } = new();
    public EmailQueueOptions Queue { get; set; } = new();
}

public sealed class EmailDigestOptions
{
    public bool Enabled { get; set; } = true;
    public int HourLocal { get; set; } = 8;
    public int MinimumUnread { get; set; } = 1;
}

public sealed class EmailQueueOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 10;
    public int MaxAttempts { get; set; } = 5;
    public int BaseRetrySeconds { get; set; } = 30;
    public int ProcessingTimeoutMinutes { get; set; } = 10;
    public int MaxTotalAttachmentBytes { get; set; } = 2_500_000;
}
