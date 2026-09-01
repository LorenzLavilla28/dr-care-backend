using DrCare.Infrastructure.Documents;
using Microsoft.Extensions.Options;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ContractTemplatePreview <master.pdf> <overlay.html> <output.pdf>");
    return 2;
}

var values = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["fullNameUpper"] = "JOSEPHINE LAVILLA",
    ["franchiseeAddress"] = "Quezon City",
    ["franchisedSite"] = "Quezon City",
    ["effectiveDate"] = "September 1, 2026",
    ["initialFranchiseFee"] = "Php 290,000.00",
    ["signingDateLong"] = "1st day of September, 2026",
    ["seriesYear"] = "2026",
};

var overlayHtml = await File.ReadAllTextAsync(args[1]);
foreach (var value in values)
    overlayHtml = overlayHtml.Replace("{{" + value.Key + "}}", System.Net.WebUtility.HtmlEncode(value.Value), StringComparison.Ordinal);

var renderer = new ChromiumDocumentPdfRenderer(Options.Create(new DocumentRenderingOptions()));
await using var master = File.OpenRead(args[0]);
var pdf = await renderer.RenderPdfTemplateAsync(master, overlayHtml, CancellationToken.None);
var outputPath = Path.GetFullPath(args[2]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllBytesAsync(outputPath, pdf);
Console.WriteLine(outputPath);
return 0;
