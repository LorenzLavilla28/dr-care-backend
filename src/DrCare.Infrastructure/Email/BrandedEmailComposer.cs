using System.Net;
using System.Text;
using DrCare.Application;
using DrCare.Domain;

namespace DrCare.Infrastructure.Email;

public sealed class BrandedEmailComposer : IEmailComposer
{
    private readonly string template;

    public BrandedEmailComposer()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Templates", "Emails", "transactional.html");
        template = File.ReadAllText(path);
    }

    public ComposedEmail Compose(BrandedEmailRequest request)
    {
        var (product, accent, soft) = Brand(request.ProductLine);
        var paragraphs = string.Join("", request.Paragraphs.Select(x => $"<p style=\"margin:0 0 16px;color:#3f4652;line-height:1.65\">{E(x)}</p>"));
        var action = string.IsNullOrWhiteSpace(request.ActionUrl) ? "" : $"<tr><td style=\"padding:8px 0 24px\"><a href=\"{A(request.ActionUrl!)}\" style=\"display:inline-block;background:{accent};color:#fff;text-decoration:none;font-weight:700;border-radius:8px;padding:13px 20px\">{E(request.ActionLabel ?? "Continue")}</a></td></tr>";
        var details = request.Details is null or { Count: 0 } ? "" : "<table role=\"presentation\" width=\"100%\" style=\"background:" + soft + ";border-radius:10px;padding:8px 16px;margin:4px 0 20px\">" + string.Join("", request.Details.Select(x => $"<tr><td style=\"padding:8px 0;color:#727986;font-size:13px\">{E(x.Key)}</td><td align=\"right\" style=\"padding:8px 0;color:#1c2430;font-size:13px;font-weight:700\">{E(x.Value)}</td></tr>")) + "</table>";
        var html = template
            .Replace("{{PREHEADER}}", E(request.Preheader)).Replace("{{ACCENT}}", accent).Replace("{{ACCENT_SOFT}}", soft)
            .Replace("{{PRODUCT}}", E(product)).Replace("{{TITLE}}", E(request.Title)).Replace("{{GREETING}}", E(request.Greeting))
            .Replace("{{PARAGRAPHS}}", paragraphs).Replace("{{DETAILS}}", details).Replace("{{ACTION}}", action)
            .Replace("{{CLOSING}}", E(request.ClosingNote ?? "Thank you for choosing Dr. Care Medical Group."))
            .Replace("{{YEAR}}", DateTimeOffset.UtcNow.Year.ToString());
        var text = new StringBuilder().AppendLine(request.Title).AppendLine().AppendLine(request.Greeting)
            .AppendLine(string.Join(Environment.NewLine + Environment.NewLine, request.Paragraphs));
        if (request.Details is not null) foreach (var item in request.Details) text.AppendLine($"{item.Key}: {item.Value}");
        if (!string.IsNullOrWhiteSpace(request.ActionUrl)) text.AppendLine().AppendLine($"{request.ActionLabel}: {request.ActionUrl}");
        text.AppendLine().AppendLine(request.ClosingNote ?? "Thank you for choosing Dr. Care Medical Group.");
        return new ComposedEmail(html, text.ToString());
    }

    private static (string Product, string Accent, string Soft) Brand(ProductLine? product) => product switch
    {
        ProductLine.Abc => ("Dr. Care Animal Bite Clinic", "#e50914", "#fff1f2"),
        ProductLine.Pharmacy => ("Dr. Care Pharmacy", "#087f8c", "#edfafa"),
        ProductLine.Combo => ("Dr. Care ABC + Pharmacy", "#6d3fc0", "#f5f0ff"),
        _ => ("Dr. Care Medical Group", "#e50914", "#fff1f2")
    };
    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string A(string value) => WebUtility.HtmlEncode(value);
}
