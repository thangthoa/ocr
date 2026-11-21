using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.Features.Mockups.Components;

/// <summary>
/// User management mockup: lists the accounts of <see cref="UserDirectory"/> and adds or changes one. The form shows
/// what the account would be allowed to do and how many documents it would see, from the role's permissions, the
/// departments and the clearance chosen.
/// </summary>
public sealed partial class UsersPanel : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private readonly TablePaging _paging = new();
    private readonly Dictionary<string, string> _errors = [];
    private string? _search;
    private UserRole? _roleFilter;
    private UserStatus? _statusFilter;
    private UserForm? _form;
    private string? _message;
    private ProjectFacts? _documents;

    [Inject] private UserDirectory Accounts { get; set; } = default!;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private ILogger<UsersPanel> Logger { get; set; } = default!;

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _documents = (await Catalog.SnapshotAsync(_disposed.Token)).Facts;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex);
        }
    }

    private List<AppUser> Matching(IEnumerable<AppUser> users)
    {
        var term = _search?.Trim();
        return users
            .Where(u => _roleFilter is null || u.Role == _roleFilter)
            .Where(u => _statusFilter is null || u.Status == _statusFilter)
            .Where(u => string.IsNullOrEmpty(term)
                || u.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || u.UserName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || u.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string Departments(AppUser user) => user.AllDepartments
        ? Text["Users.AllDepartments"]
        : string.Join(Text["ListSeparator"].Value, user.Departments.Select(d => Text.Department(d)));

    /// <summary>How many documents the account in the form would see, out of all the documents.</summary>
    private string VisibleDocuments
    {
        get
        {
            if (_documents is null || _form is null)
                return Text["Users.VisibleUnknown"];
            var user = _form.ToUser();
            var permissions = Accounts.PermissionsOf(user.Role);
            var visible = _documents.Count((department, _, confidentiality) => AccessRules.CanSee(user, permissions, department, confidentiality));
            return Text["Users.Visible", MockupTextExtensions.FormatNumber(visible), MockupTextExtensions.FormatNumber(_documents.AllTime.Documents)];
        }
    }

    private void Add()
    {
        _form = UserForm.New();
        _errors.Clear();
        _message = null;
    }

    private void Edit(AppUser user)
    {
        _form = UserForm.From(user);
        _errors.Clear();
        _message = null;
    }

    private void Cancel()
    {
        _form = null;
        _errors.Clear();
    }

    private void SetDepartment(DocumentDepartment department, bool selected)
    {
        if (selected)
            _form!.Departments.Add(department);
        else
            _form!.Departments.Remove(department);
    }

    private void Save()
    {
        var form = _form!;
        _errors.Clear();
        if (!form.AllDepartments && form.Departments.Count == 0)
            _errors["user-departments"] = Text["Users.Error.NoDepartment"];

        var user = form.ToUser();
        var problems = _errors.Count == 0
            ? Accounts.Save(user, form.IsNew ? null : form.OriginalUserName, LoginPage.DemoUserName)
            : AccessRules.Check(user, form.IsNew ? null : Accounts.Find(form.OriginalUserName!), Accounts.Users);
        foreach (var (field, error) in problems)
            _errors[FieldId(field)] = Text[$"Users.Error.{error}"];
        if (_errors.Count > 0)
            return;

        _message = Text["Users.Saved", user.DisplayName.Trim()];
        _form = null;
    }

    private static string FieldId(UserField field) => field switch
    {
        UserField.DisplayName => "user-name",
        UserField.UserName => "user-username",
        UserField.Email => "user-email",
        _ => "user-role",
    };

    private string? Invalid(string field) => _errors.ContainsKey(field) ? "is-invalid" : null;

    private string? ErrorId(string field) => _errors.ContainsKey(field) ? field + "-error" : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "User management UI: failed to list the documents")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);

    /// <summary>An account while it is added or changed.</summary>
    private sealed class UserForm
    {
        public bool IsNew => OriginalUserName is null;

        public string? OriginalUserName { get; private init; }

        public UserSource Source { get; private init; } = UserSource.Local;

        public string DisplayName { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public UserRole Role { get; set; } = UserRole.Reader;

        public bool AllDepartments { get; set; }

        public HashSet<DocumentDepartment> Departments { get; } = [];

        public DocumentConfidentiality Clearance { get; set; } = DocumentConfidentiality.Internal;

        public bool MfaRequired { get; set; } = true;

        public bool Active { get; set; } = true;

        public static UserForm New() => new();

        public static UserForm From(AppUser user)
        {
            var form = new UserForm
            {
                OriginalUserName = user.UserName,
                Source = user.Source,
                DisplayName = user.DisplayName,
                UserName = user.UserName,
                Email = user.Email,
                Role = user.Role,
                AllDepartments = user.AllDepartments,
                Clearance = user.Clearance,
                MfaRequired = user.MfaRequired,
                Active = user.IsActive,
            };
            form.Departments.UnionWith(user.Departments);
            return form;
        }

        public AppUser ToUser() => new(
            UserName,
            DisplayName,
            Email,
            Role,
            AllDepartments ? [] : [.. Departments.Order()],
            Clearance,
            MfaRequired,
            Active ? UserStatus.Active : UserStatus.Disabled,
            Source);
    }
}
