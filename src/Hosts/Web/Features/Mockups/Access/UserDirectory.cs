using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Access;

/// <summary>
/// The users of the mockup and the permissions of each role: in memory like the rest of the mockup (back to the
/// sample accounts after a restart) and shared by everyone. Every change is written to the activity log.
/// </summary>
public sealed class UserDirectory
{
    private readonly Lock _lock = new();
    private readonly AuditLog _audit;
    private readonly List<AppUser> _users;
    private Dictionary<UserRole, IReadOnlySet<AccessRight>> _permissions;

    public UserDirectory(AuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _audit = audit;
        _users = Seed(time.GetUtcNow().UtcDateTime);
        _permissions = new(AccessRules.DefaultPermissions);
    }

    /// <summary>Every account, by display name.</summary>
    public IReadOnlyList<AppUser> Users
    {
        get
        {
            lock (_lock)
            {
                return [.. _users.OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
            }
        }
    }

    public IReadOnlyDictionary<UserRole, IReadOnlySet<AccessRight>> Permissions
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<UserRole, IReadOnlySet<AccessRight>>(_permissions);
            }
        }
    }

    public AppUser? Find(string userName)
    {
        lock (_lock)
        {
            return _users.Find(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlySet<AccessRight> PermissionsOf(UserRole role)
    {
        lock (_lock)
        {
            return _permissions[role];
        }
    }

    /// <summary>Adds the account (when <paramref name="originalUserName"/> is null) or replaces it.</summary>
    /// <returns>The problems that kept it from being saved; empty when it was saved.</returns>
    public IReadOnlyDictionary<UserField, UserError> Save(AppUser user, string? originalUserName, string author)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (_lock)
        {
            var original = originalUserName is null ? null : _users.Find(u => u.UserName == originalUserName)
                ?? throw new InvalidOperationException($"The user '{originalUserName}' does not exist.");
            var saved = user with
            {
                UserName = user.UserName.Trim(),
                DisplayName = user.DisplayName.Trim(),
                Email = user.Email.Trim(),
                Departments = [.. user.Departments.Distinct().Order()],
            };
            // An existing account keeps its user name, where it comes from and when it last signed in.
            if (original is not null)
                saved = saved with { UserName = original.UserName, Source = original.Source, LastSignInUtc = original.LastSignInUtc };

            var errors = AccessRules.Check(saved, original, _users);
            if (errors.Count > 0)
                return errors;

            if (original is null)
            {
                _users.Add(saved);
                _audit.Record(author, AuditAction.UserAdded, detail: saved.UserName);
            }
            else
            {
                _users[_users.IndexOf(original)] = saved;
                _audit.Record(author, AuditAction.UserChanged, detail: saved.UserName);
            }

            return errors;
        }
    }

    /// <summary>Replaces the permissions of every role; an administrator keeps the locked ones whatever is passed.</summary>
    public void SavePermissions(IReadOnlyDictionary<UserRole, IReadOnlySet<AccessRight>> permissions, string author)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var next = Enum.GetValues<UserRole>().ToDictionary(
            role => role,
            role => (IReadOnlySet<AccessRight>)(permissions.TryGetValue(role, out var granted) ? granted : new HashSet<AccessRight>())
                .Concat(Enum.GetValues<AccessRight>().Where(p => AccessRules.IsLocked(role, p)))
                .ToHashSet());
        lock (_lock)
        {
            _permissions = next;
        }

        _audit.Record(author, AuditAction.RolesChanged);
    }

    /// <summary>The sample accounts: the demo administrator, and AD accounts for each role.</summary>
    private static List<AppUser> Seed(DateTime now)
    {
        DocumentDepartment[] all = [];
        const DocumentConfidentiality Internal = DocumentConfidentiality.Internal;
        const DocumentConfidentiality Confidential = DocumentConfidentiality.Confidential;
        const DocumentConfidentiality Secret = DocumentConfidentiality.Secret;
        AppUser Ad(string userName, string name, UserRole role, DocumentDepartment[] departments, DocumentConfidentiality clearance,
            double? hoursAgo, bool mfa = false, UserStatus status = UserStatus.Active) =>
            new(userName, name, userName + "@example.org", role, departments, clearance, mfa, status, UserSource.ActiveDirectory,
                hoursAgo is { } hours ? now.AddHours(-hours) : null);

        return
        [
            new("admin", "Demo administrator", "admin@example.org", UserRole.Administrator, all, Secret, true, UserStatus.Active,
                UserSource.Local, now.AddMinutes(-5)),
            Ad("it.admin", "Rashid Al Dhaheri", UserRole.Administrator, all, Secret, 26, mfa: true),
            Ad("capture01", "Ahmed Al Hammadi", UserRole.CaptureOperator, all, Internal, 2),
            Ad("capture02", "Fatima Al Suwaidi", UserRole.CaptureOperator, all, Internal, 3),
            Ad("capture03", "Khalid Al Nuaimi", UserRole.CaptureOperator, all, Internal, 24 * 40, status: UserStatus.Disabled),
            Ad("review01", "Mariam Al Ketbi", UserRole.Reviewer, all, Confidential, 1),
            Ad("review02", "Omar Al Shamsi", UserRole.Reviewer, [DocumentDepartment.Finance, DocumentDepartment.Procurement], Confidential, 5),
            Ad("records01", "Noura Al Mazrouei", UserRole.RecordsManager, all, Secret, 30, mfa: true),
            Ad("reader.finance", "Saeed Al Falasi", UserRole.Reader, [DocumentDepartment.Finance], Confidential, 50),
            Ad("reader.hr", "Hessa Al Mheiri", UserRole.Reader, [DocumentDepartment.HumanResources], Confidential, 75),
            Ad("reader.legal", "Yousef Al Blooshi", UserRole.Reader, [DocumentDepartment.Legal], Secret, 8),
            Ad("reader.admin", "Latifa Al Zaabi", UserRole.Reader, [DocumentDepartment.Administration], Internal, null),
        ];
    }
}
