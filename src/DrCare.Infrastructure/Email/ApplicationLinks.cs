using DrCare.Application;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Email;

public sealed class ApplicationLinks(IOptions<EmailOptions> options) : IApplicationLinks
{
    private string Base => options.Value.FrontendBaseUrl.TrimEnd('/');
    public string Login() => Base;
    public string PasswordReset(string token, string email) => $"{Base}/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
    public string ContractSigning(string token) => $"{Base}/sign-contract/{Uri.EscapeDataString(token)}";
}
