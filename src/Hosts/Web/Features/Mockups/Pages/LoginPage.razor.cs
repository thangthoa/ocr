using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Sign-in mockup. There is no authentication yet: the demo account only moves the user on to
/// the dashboard, and every page stays reachable without signing in. ASP.NET Core Identity
/// replaces this when the Identity module is built.
/// </summary>
public sealed partial class LoginPage
{
    internal const string DemoUserName = "admin";
    internal const string DemoPassword = "admin";

    /// <summary>Whatever was typed is logged on failure, so it is cut short.</summary>
    private const int MaxLoggedUserName = 64;

    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string? _error;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private AuditLog Audit { get; set; } = default!;

    private void SignIn()
    {
        if (string.IsNullOrWhiteSpace(_userName) || string.IsNullOrEmpty(_password))
        {
            _error = Text["Login.Required"];
            return;
        }

        var userName = _userName.Trim();
        if (string.Equals(userName, DemoUserName, StringComparison.Ordinal) && string.Equals(_password, DemoPassword, StringComparison.Ordinal))
        {
            Audit.Record(DemoUserName, AuditAction.SignedIn);
            Navigation.NavigateTo(string.Empty);
            return;
        }

        Audit.Record(userName.Length > MaxLoggedUserName ? userName[..MaxLoggedUserName] : userName, AuditAction.SignInFailed);
        _error = Text["Login.Failed"];
        _password = string.Empty;
    }
}
