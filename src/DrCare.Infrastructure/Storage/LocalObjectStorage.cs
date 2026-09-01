using System.Security.Cryptography;
using System.Text;
using DrCare.Application;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Storage;

public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage:Local";
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "dr-care-storage");
    public string Secret { get; set; } = "local-development-storage-secret-change-me";
}

public interface ILocalObjectStorage : IPrivateObjectStorage
{
    bool TryValidate(string method, string encodedKey, string? contentType, string? fileName, string expires, string signature, out string objectKey, out string validatedContentType, out string validatedFileName);
}

public sealed class LocalObjectStorage(IOptions<LocalStorageOptions> options) : ILocalObjectStorage
{
    private const long MaxObjectBytes = 25_000_000;
    private readonly LocalStorageOptions settings = options.Value;

    public Task<string> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        Task.FromResult(CreateUrl("PUT", objectKey, contentType, expiresAt));

    public Task<string> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        Task.FromResult(CreateUrl("GET", objectKey, contentType, expiresAt, fileName));

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(GetSafePath(objectKey)));

    public async Task PutAsync(string objectKey, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (content.Length > MaxObjectBytes) throw new InvalidOperationException("The private object exceeds the maximum allowed size.");
        var path = GetSafePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken);
        File.Move(temporary, path, true);
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = GetSafePath(objectKey);
        if (!File.Exists(path)) throw new FileNotFoundException("Private object was not found.", path);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true));
    }

    public bool TryValidate(string method, string encodedKey, string? contentType, string? fileName, string expires, string signature, out string objectKey, out string validatedContentType, out string validatedFileName)
    {
        objectKey = string.Empty;
        validatedContentType = string.Empty;
        validatedFileName = string.Empty;
        if (!long.TryParse(expires, out var unix) || unix < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        try
        {
            var bytes = Base64UrlDecode(encodedKey);
            objectKey = Encoding.UTF8.GetString(bytes);
            validatedContentType = contentType is null ? string.Empty : Encoding.UTF8.GetString(Base64UrlDecode(contentType));
            validatedFileName = fileName is null ? string.Empty : Encoding.UTF8.GetString(Base64UrlDecode(fileName));
        }
        catch (FormatException) { return false; }
        if (!IsSafeObjectKey(objectKey) || (method.Equals("PUT", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(validatedContentType))) return false;
        if (!string.IsNullOrEmpty(validatedFileName) && (validatedFileName != Path.GetFileName(validatedFileName) || validatedFileName.Length > 200 || validatedFileName.Contains('\r') || validatedFileName.Contains('\n'))) return false;
        var expected = Sign(method.ToUpperInvariant(), objectKey, validatedContentType, unix, validatedFileName);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }

    public static long MaxUploadBytes => MaxObjectBytes;

    private string CreateUrl(string method, string objectKey, string contentType, DateTimeOffset expiresAt, string fileName = "")
    {
        var unix = expiresAt.ToUnixTimeSeconds();
        var encodedKey = Base64Url(Encoding.UTF8.GetBytes(objectKey));
        var encodedContentType = Base64Url(Encoding.UTF8.GetBytes(contentType));
        var encodedFileName = Base64Url(Encoding.UTF8.GetBytes(fileName));
        var signature = Sign(method, objectKey, contentType, unix, fileName);
        return $"{settings.BaseUrl.TrimEnd('/')}/api/v1/local-storage/{(method == "PUT" ? "upload" : "download")}?key={Uri.EscapeDataString(encodedKey)}&contentType={Uri.EscapeDataString(encodedContentType)}&fileName={Uri.EscapeDataString(encodedFileName)}&expires={unix}&sig={Uri.EscapeDataString(signature)}";
    }

    private string Sign(string method, string objectKey, string contentType, long expires, string fileName = "") =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.Secret), Encoding.UTF8.GetBytes($"{method}\n{objectKey}\n{contentType}\n{fileName}\n{expires}"))).ToLowerInvariant();

    private string GetSafePath(string objectKey)
    {
        if (!IsSafeObjectKey(objectKey)) throw new InvalidOperationException("Invalid private object key.");
        var root = Path.GetFullPath(settings.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid private object path.");
        return path;
    }

    private static bool IsSafeObjectKey(string value) => value.StartsWith("private/", StringComparison.Ordinal) && !value.Contains("..", StringComparison.Ordinal) && !value.Contains('\\');
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
