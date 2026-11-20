using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Localization;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Sign-in mockup in two steps: the demo account's user name and password, then a one-time code. The session is
/// kept in the browser's localStorage by <c>wwwroot/js/session.js</c>, which sends visitors without one to this page.
/// That is a mockup, not security: the server checks nothing. ASP.NET Core Identity (cookie, MFA, authorization
/// policies on the server) replaces it when the Identity module is built.
/// </summary>
public sealed partial class LoginPage : IDisposable
{
    internal const string DemoUserName = "admin";
    internal const string DemoPassword = "admin";

    /// <summary>The code the mockup accepts; the real one will be sent by e-mail or come from an authenticator app.</summary>
    internal const string DemoCode = "123456";

    internal const int CodeLength = 6;

    /// <summary>Wrong codes allowed before the user has to enter the password again.</summary>
    internal const int MaxCodeAttempts = 5;

    /// <summary>How long after a code is sent a new one can be asked for.</summary>
    internal static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(30);

    /// <summary>Whatever was typed is logged on failure, so it is cut short.</summary>
    private const int MaxLoggedUserName = 64;

    private Step _step = Step.Password;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _code = string.Empty;
    private int _codeAttemptsLeft;
    private bool _resent;
    private readonly ElementReference[] _digitBoxes = new ElementReference[CodeLength];

    /// <summary>Changes to empty the code boxes (they are made again under a new key).</summary>
    private int _codeVersion;

    /// <summary>Puts the cursor in the first code box after the next render.</summary>
    private bool _focusCode;

    /// <summary>When "Send a new code" can be used again; a timer redraws the countdown every second until then.</summary>
    private DateTimeOffset _resendAt;
    private ITimer? _resendTimer;
    private bool _disposed;

    /// <summary>Keeps the session for 30 days instead of 8 hours (see <c>session.js</c>).</summary>
    private bool _rememberMe;

    private string? _error;

    private enum Step
    {
        Password,
        Code,
    }

    /// <summary>Where the visitor was going when session.js sent them here; only paths on this site are used.</summary>
    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private AuditLog Audit { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    private int ResendSecondsLeft => Math.Max(0, (int)Math.Ceiling((_resendAt - Time.GetUtcNow()).TotalSeconds));

    public void Dispose()
    {
        _disposed = true;
        _resendTimer?.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusCode && _step == Step.Code)
        {
            _focusCode = false;
            await _digitBoxes[0].FocusAsync();
        }
    }

    /// <summary>First step: the password. The sign-in is logged only once the code is right too.</summary>
    private void SignIn()
    {
        if (string.IsNullOrWhiteSpace(_userName) || string.IsNullOrEmpty(_password))
        {
            _error = Text["Login.Required"];
            return;
        }

        var userName = _userName.Trim();
        var passwordMatches = string.Equals(_password, DemoPassword, StringComparison.Ordinal);
        _password = string.Empty;
        if (string.Equals(userName, DemoUserName, StringComparison.Ordinal) && passwordMatches)
        {
            _step = Step.Code;
            _codeAttemptsLeft = MaxCodeAttempts;
            _resent = false;
            _error = null;
            ClearCode();
            StartResendCountdown();
            return;
        }

        Audit.Record(userName.Length > MaxLoggedUserName ? userName[..MaxLoggedUserName] : userName, AuditAction.SignInFailed);
        _error = Text["Login.Failed"];
    }

    /// <summary>Second step: the one-time code. Too many wrong codes send the user back to the password.</summary>
    private async Task VerifyAsync()
    {
        var code = _code.Trim();
        _resent = false;
        if (code.Length != CodeLength || !code.All(char.IsAsciiDigit))
        {
            _error = Text["Otp.Format", CodeLength];
            return;
        }

        if (!string.Equals(code, DemoCode, StringComparison.Ordinal))
        {
            Audit.Record(DemoUserName, AuditAction.OtpFailed);
            ClearCode();
            if (--_codeAttemptsLeft > 0)
            {
                _error = Text["Otp.Wrong", _codeAttemptsLeft];
                return;
            }

            _step = Step.Password;
            _error = Text["Otp.TooMany"];
            return;
        }

        await JS.InvokeVoidAsync("ocrSession.start", DemoUserName, _rememberMe);
        Audit.Record(DemoUserName, AuditAction.SignedIn);
        Navigation.NavigateTo(LocalizationSetup.IsLocalPath(ReturnUrl) && !IsLoginPath(ReturnUrl!) ? ReturnUrl! : string.Empty);
    }

    /// <summary>Mockup: no code is really sent, the page only says so. Refused until the countdown is over.</summary>
    private void Resend()
    {
        if (ResendSecondsLeft > 0)
            return;

        _resent = true;
        _error = null;
        ClearCode();
        StartResendCountdown();
    }

    private void StartResendCountdown()
    {
        _resendAt = Time.GetUtcNow() + ResendDelay;
        _resendTimer?.Dispose();
        _resendTimer = Time.CreateTimer(_ =>
        {
            if (!_disposed)
                _ = InvokeAsync(TickResendCountdown);
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void TickResendCountdown()
    {
        if (ResendSecondsLeft == 0)
        {
            _resendTimer?.Dispose();
            _resendTimer = null;
        }

        StateHasChanged();
    }

    private void Back()
    {
        _resendTimer?.Dispose();
        _resendTimer = null;
        _step = Step.Password;
        _error = null;
        _code = string.Empty;
    }

    /// <summary>Empties the code and its boxes, and puts the cursor back in the first box.</summary>
    private void ClearCode()
    {
        _code = string.Empty;
        _codeVersion++;
        _focusCode = true;
    }

    private static bool IsLoginPath(string path) =>
        path.Equals("/login", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/login?", StringComparison.OrdinalIgnoreCase);
}
