using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DrCare.Application;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Storage;

public sealed class S3Options
{
    public const string SectionName = "Storage:S3";
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "ap-southeast-1";
}

public sealed class S3ObjectStorage(IAmazonS3 client, IOptions<S3Options> options) : IPrivateObjectStorage
{
    public Task<string> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new GetPreSignedUrlRequest { BucketName = options.Value.BucketName, Key = objectKey, Verb = HttpVerb.PUT, ContentType = contentType, Expires = expiresAt.UtcDateTime, ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256 };
        return Task.FromResult(client.GetPreSignedURL(request));
    }

    public Task<string> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new GetPreSignedUrlRequest { BucketName = options.Value.BucketName, Key = objectKey, Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentType = contentType, ContentDisposition = $"attachment; filename=\"{fileName.Replace("\"", string.Empty)}\"" } };
        return Task.FromResult(client.GetPreSignedURL(request));
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try { await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = options.Value.BucketName, Key = objectKey }, cancellationToken); return true; }
        catch (AmazonS3Exception ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden) { return false; }
    }

    public async Task PutAsync(string objectKey, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Value.BucketName,
            Key = objectKey,
            ContentType = contentType,
            InputStream = stream,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await client.GetObjectAsync(options.Value.BucketName, objectKey, cancellationToken);
        var copy = new MemoryStream();
        await response.ResponseStream.CopyToAsync(copy, cancellationToken);
        response.Dispose();
        copy.Position = 0;
        return copy;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.Value.BucketName)) throw new InvalidOperationException("Storage:S3:BucketName must be configured before document endpoints are used.");
    }
}
