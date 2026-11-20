using System.Net.Mail;
using System.Text.RegularExpressions;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Access;

// Users, roles and permissions of the mockup. Sign-in is still the demo account, so nothing here is enforced
// yet; with ASP.NET Core Identity these become authorization policies checked on the server (CLAUDE.md §8.3).

/// <summary>What a role allows; a user gets the permissions of their role.</summary>
public enum AccessRight
{
    ViewDocuments,
    Scan,
    Review,
    Approve,
    Download,
    Delete,
    ManageRetention,
    ViewReports,
    ExportData,
    ManageSettings,
    ManageUsers,
    ViewAuditLog,
}

/// <summary>The built-in roles, each mapped to an Active Directory group.</summary>
public enum UserRole
{
    CaptureOperator,
    Reviewer,
    RecordsManager,
    Reader,
    Administrator,
}

public enum UserStatus
{
    Active,
    Disabled,
}

/// <summary>Where an account comes from: synchronised from Active Directory, or created in this app.</summary>
public enum UserSource
{
    ActiveDirectory,
    Local,
}

public enum UserField
{
    DisplayName,
    UserName,
    Email,
    Role,
}

public enum UserError
{
    Required,
    InvalidUserName,
    UserNameTaken,
    InvalidEmail,
    LastAdministrator,
}

/// <param name="Departments">The departments whose documents the user sees; empty for every department.</param>
/// <param name="Clearance">The highest confidentiality the user may see.</param>
public sealed record AppUser(
    string UserName,
    string DisplayName,
    string Email,
    UserRole Role,
    IReadOnlyList<DocumentDepartment> Departments,
    DocumentConfidentiality Clearance,
    bool MfaRequired,
    UserStatus Status,
    UserSource Source,
    DateTime? LastSignInUtc = null)
{
    public bool IsActive => Status == UserStatus.Active;

    public bool AllDepartments => Departments.Count == 0;
}

public static partial class AccessRules
{
    /// <summary>The Active Directory group whose members get the role.</summary>
    public static string AdGroup(UserRole role) => role switch
    {
        UserRole.CaptureOperator => "ECM-Capture",
        UserRole.Reviewer => "ECM-Review",
        UserRole.RecordsManager => "ECM-Records",
        UserRole.Reader => "ECM-Read-<department>",
        _ => "ECM-Admin",
    };

    public static IReadOnlyDictionary<UserRole, IReadOnlySet<AccessRight>> DefaultPermissions { get; } =
        new Dictionary<UserRole, IReadOnlySet<AccessRight>>
        {
            [UserRole.CaptureOperator] = new HashSet<AccessRight> { AccessRight.ViewDocuments, AccessRight.Scan },
            [UserRole.Reviewer] = new HashSet<AccessRight> { AccessRight.ViewDocuments, AccessRight.Review, AccessRight.Approve, AccessRight.Download },
            [UserRole.RecordsManager] = new HashSet<AccessRight>
            {
                AccessRight.ViewDocuments, AccessRight.Approve, AccessRight.Download, AccessRight.Delete, AccessRight.ManageRetention,
                AccessRight.ViewReports, AccessRight.ExportData,
            },
            [UserRole.Reader] = new HashSet<AccessRight> { AccessRight.ViewDocuments, AccessRight.Download },
            [UserRole.Administrator] = Enum.GetValues<AccessRight>().ToHashSet(),
        };

    /// <summary>
    /// Permissions an administrator always keeps, so nobody can lock everyone out of user management.
    /// </summary>
    public static bool IsLocked(UserRole role, AccessRight permission) =>
        role == UserRole.Administrator && permission is AccessRight.ManageUsers or AccessRight.ManageSettings;

    /// <summary>Whether a user may see a document: active, allowed to view, in their departments, within their clearance.</summary>
    /// <remarks>A document without a department (a fresh OCR job) is seen only by users of every department.</remarks>
    public static bool CanSee(AppUser user, IReadOnlySet<AccessRight> permissions, DocumentRecord document)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(document);
        var metadata = document.Metadata;
        return user.IsActive
            && permissions.Contains(AccessRight.ViewDocuments)
            && (user.AllDepartments || (metadata.Department is { } department && user.Departments.Contains(department)))
            && metadata.Confidentiality <= user.Clearance;
    }

    /// <summary>
    /// The problems with <paramref name="user"/>, saved over <paramref name="original"/> (null for a new account), among
    /// the <paramref name="others"/> accounts.
    /// </summary>
    public static IReadOnlyDictionary<UserField, UserError> Check(AppUser user, AppUser? original, IReadOnlyCollection<AppUser> others)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(others);
        var errors = new Dictionary<UserField, UserError>();
        if (string.IsNullOrWhiteSpace(user.DisplayName))
            errors[UserField.DisplayName] = UserError.Required;

        if (string.IsNullOrWhiteSpace(user.UserName))
            errors[UserField.UserName] = UserError.Required;
        else if (!UserNamePattern().IsMatch(user.UserName))
            errors[UserField.UserName] = UserError.InvalidUserName;
        else if (original is null && others.Any(u => string.Equals(u.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)))
            errors[UserField.UserName] = UserError.UserNameTaken;

        if (string.IsNullOrWhiteSpace(user.Email))
            errors[UserField.Email] = UserError.Required;
        else if (!MailAddress.TryCreate(user.Email, out var address) || address.Address != user.Email.Trim() || !address.Host.Contains('.', StringComparison.Ordinal))
            errors[UserField.Email] = UserError.InvalidEmail;

        // Demoting or disabling the last active administrator would leave nobody to manage users.
        var wasAdministrator = original is { Role: UserRole.Administrator, IsActive: true };
        var staysAdministrator = user is { Role: UserRole.Administrator, IsActive: true };
        if (wasAdministrator && !staysAdministrator
            && !others.Any(u => u.UserName != original!.UserName && u is { Role: UserRole.Administrator, IsActive: true }))
        {
            errors[UserField.Role] = UserError.LastAdministrator;
        }

        return errors;
    }

    /// <summary>Lower-case letters, digits, dots, hyphens and underscores, as Active Directory account names.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();
}
