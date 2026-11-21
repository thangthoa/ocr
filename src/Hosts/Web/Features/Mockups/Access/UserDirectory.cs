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
        _users = Seed(time.GetUtcNow().UtcDateTime, time.LocalTimeZone);
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

    /// <summary>
    /// The sample accounts: the demo administrator, and AD accounts for each role. The capture operators and reviewers
    /// are the people of the simulated history (<see cref="History.HistoryProfile"/>): they last signed in for their
    /// shift yesterday, since nothing is simulated today; capture03 was disabled 40 days ago.
    /// </summary>
    private static List<AppUser> Seed(DateTime now, TimeZoneInfo zone)
    {
        DocumentDepartment[] all = [];
        const DocumentConfidentiality Internal = DocumentConfidentiality.Internal;
        const DocumentConfidentiality Confidential = DocumentConfidentiality.Confidential;
        const DocumentConfidentiality Secret = DocumentConfidentiality.Secret;
        AppUser Ad(string userName, string name, UserRole role, DocumentDepartment[] departments, DocumentConfidentiality clearance,
            double? hoursAgo, bool mfa = false, UserStatus status = UserStatus.Active) =>
            new(userName, name, userName + "@example.org", role, departments, clearance, mfa, status, UserSource.ActiveDirectory,
                hoursAgo is { } hours ? now.AddHours(-hours) : null);

        // Hours since yesterday's shift started (local time), for the people of the simulated line.
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone)).AddDays(-1);
        double Shift(int hour, int minute) =>
            (now - TimeZoneInfo.ConvertTimeToUtc(yesterday.ToDateTime(new TimeOnly(hour, minute)), zone)).TotalHours;
        var morning = Shift(6, 52);
        var evening = Shift(14, 50);
        var reviewers = Shift(7, 24);

        return
        [
            new("admin", "Demo administrator", "admin@example.org", UserRole.Administrator, all, Secret, true, UserStatus.Active,
                UserSource.Local, now.AddMinutes(-5)),
            Ad("it.admin", "Rashid Al Dhaheri", UserRole.Administrator, all, Secret, 26, mfa: true),
            Ad("capture01", "Ahmed Al Hammadi", UserRole.CaptureOperator, all, Internal, morning),
            Ad("capture02", "Fatima Al Suwaidi", UserRole.CaptureOperator, all, Internal, morning + 0.05),
            Ad("capture03", "Khalid Al Nuaimi", UserRole.CaptureOperator, all, Internal, 24 * 40, status: UserStatus.Disabled),
            Ad("capture04", "Mohammed Al Mansoori", UserRole.CaptureOperator, all, Internal, morning + 0.1),
            Ad("capture05", "Aisha Al Marzooqi", UserRole.CaptureOperator, all, Internal, morning + 0.02),
            Ad("capture06", "Sultan Al Qubaisi", UserRole.CaptureOperator, all, Internal, morning + 0.12),
            Ad("capture07", "Shamma Al Muhairi", UserRole.CaptureOperator, all, Internal, evening),
            Ad("capture08", "Hamad Al Kaabi", UserRole.CaptureOperator, all, Internal, evening + 0.07),
            Ad("capture09", "Maitha Al Rumaithi", UserRole.CaptureOperator, all, Internal, evening + 0.03),
            Ad("capture10", "Abdullah Al Hosani", UserRole.CaptureOperator, all, Internal, evening + 0.1),
            Ad("capture11", "Reem Al Junaibi", UserRole.CaptureOperator, all, Internal, evening + 0.05),
            Ad("capture12", "Saif Al Ameri", UserRole.CaptureOperator, all, Internal, evening + 0.13),
            Ad("review01", "Mariam Al Ketbi", UserRole.Reviewer, all, Confidential, reviewers),
            Ad("review02", "Omar Al Shamsi", UserRole.Reviewer, [DocumentDepartment.Finance, DocumentDepartment.Procurement], Confidential,
                reviewers + 0.08),
            Ad("review03", "Khadija Al Hashemi", UserRole.Reviewer, all, Secret, reviewers + 0.03, mfa: true),
            Ad("review04", "Ali Al Tunaiji", UserRole.Reviewer, all, Confidential, reviewers + 0.1),
            Ad("review05", "Moza Al Shehhi", UserRole.Reviewer,
                [DocumentDepartment.HumanResources, DocumentDepartment.Legal, DocumentDepartment.Administration], Confidential, reviewers + 0.05),
            Ad("review06", "Juma Al Kindi", UserRole.Reviewer, all, Confidential, reviewers + 0.12),
            Ad("records01", "Noura Al Mazrouei", UserRole.RecordsManager, all, Secret, 30, mfa: true),
            Ad("reader.finance", "Saeed Al Falasi", UserRole.Reader, [DocumentDepartment.Finance], Confidential, 50),
            Ad("reader.hr", "Hessa Al Mheiri", UserRole.Reader, [DocumentDepartment.HumanResources], Confidential, 75),
            Ad("reader.legal", "Yousef Al Blooshi", UserRole.Reader, [DocumentDepartment.Legal], Secret, 8),
            Ad("reader.admin", "Latifa Al Zaabi", UserRole.Reader, [DocumentDepartment.Administration], Internal, null),
        ];
    }
}
