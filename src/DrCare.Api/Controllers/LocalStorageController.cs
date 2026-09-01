using DrCare.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/local-storage"), AllowAnonymous, EnableRateLimiting("signing")]
public sealed class LocalStorageController(ILocalObjectStorage storage) : ControllerBase
{
    [HttpPut("upload")]
    public async Task<IActionResult> Upload([FromQuery] string key, [FromQuery] string contentType, [FromQuery] string expires, [FromQuery] string sig, CancellationToken ct)
    {
        if (!storage.TryValidate("PUT", key, contentType, null, expires, sig, out var objectKey, out var validatedContentType, out _)) return Unauthorized();
        if (!string.Equals(Request.ContentType, validatedContentType, StringComparison.OrdinalIgnoreCase)) return BadRequest("Content type does not match the upload intent.");
        if (Request.ContentLength.HasValue && Request.ContentLength.Value > LocalObjectStorage.MaxUploadBytes) return BadRequest("The uploaded object is too large.");
        await using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length > LocalObjectStorage.MaxUploadBytes) return BadRequest("The uploaded object is too large.");
        await storage.PutAsync(objectKey, validatedContentType, buffer.ToArray(), ct);
        return NoContent();
    }

    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string key, [FromQuery] string? contentType, [FromQuery] string? fileName, [FromQuery] string expires, [FromQuery] string sig, CancellationToken ct)
    {
        if (!storage.TryValidate("GET", key, contentType, fileName, expires, sig, out var objectKey, out var validatedContentType, out var validatedFileName)) return Unauthorized();
        var stream = await storage.OpenReadAsync(objectKey, ct);
        return File(stream, string.IsNullOrWhiteSpace(validatedContentType) ? "application/octet-stream" : validatedContentType,
            string.IsNullOrWhiteSpace(validatedFileName) ? "download" : validatedFileName, enableRangeProcessing: true);
    }
}
