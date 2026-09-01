using System.Diagnostics;
using System.Text;
using DrCare.Application;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace DrCare.Infrastructure.Documents;

public sealed class DocumentRenderingOptions
{
    public const string SectionName = "DocumentRendering";
    public string? ChromiumExecutablePath { get; set; }
    public int MaxDocumentBytes { get; set; } = 10_000_000;
}

public sealed class ChromiumDocumentPdfRenderer(IOptions<DocumentRenderingOptions> options) : IDocumentPdfRenderer
{
    private static readonly SemaphoreSlim RenderLock = new(1, 1);
    private readonly DocumentRenderingOptions settings = options.Value;

    public async Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "drcare-pdf");
        var work = Path.Combine(root, Guid.NewGuid().ToString("N"));
        await RenderLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(work);
            var htmlPath = Path.Combine(work, "document.html");
            var pdfPath = Path.Combine(work, "document.pdf");
            var profile = Path.Combine(work, "profile");
            Directory.CreateDirectory(profile);
            await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false), cancellationToken);
            var start = new ProcessStartInfo { FileName = ResolveChromium(), UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--disable-extensions", "--disable-background-networking", "--disable-dev-shm-usage", "--no-sandbox", "--allow-file-access-from-files", "--no-pdf-header-footer", $"--user-data-dir={profile}", $"--print-to-pdf={pdfPath}", new Uri(htmlPath).AbsoluteUri }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Chrome or Chromium could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { TryKill(process); throw new InvalidOperationException("PDF generation timed out."); }
            if (process.ExitCode != 0 || !File.Exists(pdfPath)) throw new InvalidOperationException("The document template could not be rendered as PDF.");
            var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
            if (bytes.Length < 1_000 || bytes.Length > settings.MaxDocumentBytes || !bytes.AsSpan().StartsWith("%PDF"u8)) throw new InvalidOperationException("The generated PDF is invalid.");
            return bytes;
        }
        finally
        {
            RenderLock.Release();
            TryDelete(root, work);
        }
    }

    public async Task<byte[]> StampSignatureAsync(Stream pdf, ReadOnlyMemory<byte> signaturePng, string signerRole, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new MemoryStream(); await pdf.CopyToAsync(input, cancellationToken); input.Position = 0;
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        if (document.PageCount == 0) throw new InvalidOperationException("The contract PDF has no pages.");
        using var signatureStream = new MemoryStream(signaturePng.ToArray(), writable: false);
        using var image = XImage.FromStream(signatureStream);
        var page = document.Pages[^1];
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var isFranchisee = signerRole.Equals("franchisee", StringComparison.OrdinalIgnoreCase);
        var x = isFranchisee ? page.Width.Point * .10 : page.Width.Point * .55;
        var y = page.Height.Point * .78;
        var width = page.Width.Point * .34;
        var height = 58d;
        var ratio = image.PixelWidth / (double)image.PixelHeight;
        var drawWidth = width; var drawHeight = drawWidth / ratio;
        if (drawHeight > height) { drawHeight = height; drawWidth = drawHeight * ratio; }
        graphics.DrawImage(image, x + (width - drawWidth) / 2, y + height - drawHeight, drawWidth, drawHeight);
        using var output = new MemoryStream(); document.Save(output, closeStream: false); return output.ToArray();
    }

    private string ResolveChromium()
    {
        if (!string.IsNullOrWhiteSpace(settings.ChromiumExecutablePath) && File.Exists(settings.ChromiumExecutablePath)) return settings.ChromiumExecutablePath;
        var env = Environment.GetEnvironmentVariable("CHROME_BIN"); if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe") }
            : new[] { "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable" };
        return candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("Chrome or Chromium is required. Configure DocumentRendering:ChromiumExecutablePath.");
    }
    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch { } }
    private static void TryDelete(string root, string work) { try { var safeRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; var path = Path.GetFullPath(work); if (path.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
