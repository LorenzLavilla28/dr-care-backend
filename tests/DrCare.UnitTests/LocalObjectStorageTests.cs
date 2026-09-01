using DrCare.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DrCare.UnitTests;

public sealed class LocalObjectStorageTests
{
    [Fact]
    public async Task DownloadUrl_preserves_original_file_name_and_content_type()
    {
        var storage = CreateStorage();
        var url = await storage.CreateDownloadUrlAsync(
            "private/organization/leads/lead/document",
            "Valid ID with 3 specimen signatures.jpg",
            "image/jpeg",
            DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationToken.None);

        var query = ParseQuery(url);
        var valid = storage.TryValidate("GET", query["key"], query["contentType"], query["fileName"], query["expires"], query["sig"],
            out var objectKey, out var contentType, out var fileName);

        Assert.True(valid);
        Assert.Equal("private/organization/leads/lead/document", objectKey);
        Assert.Equal("image/jpeg", contentType);
        Assert.Equal("Valid ID with 3 specimen signatures.jpg", fileName);
    }

    [Fact]
    public async Task DownloadUrl_rejects_a_tampered_file_name()
    {
        var storage = CreateStorage();
        var url = await storage.CreateDownloadUrlAsync(
            "private/organization/leads/lead/document",
            "agreement.pdf",
            "application/pdf",
            DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationToken.None);

        var query = ParseQuery(url);
        query["fileName"] = ToBase64Url("different-name.pdf");

        Assert.False(storage.TryValidate("GET", query["key"], query["contentType"], query["fileName"], query["expires"], query["sig"],
            out _, out _, out _));
    }

    private static LocalObjectStorage CreateStorage() => new(Options.Create(new LocalStorageOptions
    {
        BaseUrl = "http://localhost:8080",
        RootPath = Path.Combine(Path.GetTempPath(), "dr-care-storage-tests"),
        Secret = "unit-test-storage-signing-secret"
    }));

    private static Dictionary<string, string> ParseQuery(string url) => new Uri(url).Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

    private static string ToBase64Url(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
