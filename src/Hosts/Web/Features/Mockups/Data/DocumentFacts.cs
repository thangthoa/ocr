using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>Why a document needed a person, as the exceptions report groups them.</summary>
public enum ExceptionReason
{
    LowPageConfidence,
    LowDocumentConfidence,
    PageWarnings,
    EmptyPage,
    MissingMetadata,
    OcrFailed,
}

/// <summary>
/// What the documents uploaded on some days add up to: the dashboard, the KPIs and every report read these figures,
/// so they agree with each other and with the document lists. Built by <see cref="FactsBuilder"/>.
/// </summary>
public sealed class DayFacts
{
    private static readonly int ReasonCount = Enum.GetValues<ExceptionReason>().Length;

    private readonly int[] _statuses = new int[Enum.GetValues<DocumentStatus>().Length];
    private readonly int[] _archivedByConfidentiality = new int[Enum.GetValues<DocumentConfidentiality>().Length];
    private readonly int[] _raised = new int[ReasonCount];
    private readonly int[] _open = new int[ReasonCount];
    private readonly int[] _resolved = new int[ReasonCount];
    private readonly double[] _resolveHours = new double[ReasonCount];

    public int Documents { get; private set; }

    public int Pages { get; private set; }

    /// <summary>Pages with a confidence, and the sum of those confidences (the average is weighted by page).</summary>
    public int ConfidencePages { get; private set; }

    public double ConfidenceSum { get; private set; }

    /// <summary>Pages under the page confidence threshold of the automatic check.</summary>
    public int LowPages { get; private set; }

    /// <summary>Approved (and so archived) by the automatic check, without a person.</summary>
    public int AutoArchived { get; private set; }

    /// <summary>Sent to the review queue by the automatic check.</summary>
    public int Flagged { get; private set; }

    /// <summary>Approved by a person, and the hours from the automatic check to that approval.</summary>
    public int Reviewed { get; private set; }

    public double ReviewHours { get; private set; }

    public int ArchivedPages { get; private set; }

    public double? Confidence => ConfidencePages == 0 ? null : ConfidenceSum / ConfidencePages;

    /// <summary>Documents the automatic check looked at: archived by it or sent to review.</summary>
    public int Checked => AutoArchived + Flagged;

    public int Status(DocumentStatus status) => _statuses[(int)status];

    public int Archived(DocumentConfidentiality confidentiality) => _archivedByConfidentiality[(int)confidentiality];

    /// <summary>Documents that needed a person for this reason (OCR failures included).</summary>
    public int Raised(ExceptionReason reason) => _raised[(int)reason];

    /// <summary>Still waiting: in the review queue, or failed.</summary>
    public int Open(ExceptionReason reason) => _open[(int)reason];

    public int Resolved(ExceptionReason reason) => _resolved[(int)reason];

    public double ResolveHours(ExceptionReason reason) => _resolveHours[(int)reason];

    internal void Add(DayFacts other, int sign = 1)
    {
        Documents += sign * other.Documents;
        Pages += sign * other.Pages;
        ConfidencePages += sign * other.ConfidencePages;
        ConfidenceSum += sign * other.ConfidenceSum;
        LowPages += sign * other.LowPages;
        AutoArchived += sign * other.AutoArchived;
        Flagged += sign * other.Flagged;
        Reviewed += sign * other.Reviewed;
        ReviewHours += sign * other.ReviewHours;
        ArchivedPages += sign * other.ArchivedPages;
        for (var i = 0; i < _statuses.Length; i++)
            _statuses[i] += sign * other._statuses[i];
        for (var i = 0; i < _archivedByConfidentiality.Length; i++)
            _archivedByConfidentiality[i] += sign * other._archivedByConfidentiality[i];
        for (var i = 0; i < ReasonCount; i++)
        {
            _raised[i] += sign * other._raised[i];
            _open[i] += sign * other._open[i];
            _resolved[i] += sign * other._resolved[i];
            _resolveHours[i] += sign * other._resolveHours[i];
        }
    }

    /// <param name="confidences">Confidence of each page that has text.</param>
    /// <param name="reason">The first issue of the automatic check; null when it passed or did not run.</param>
    /// <param name="reviewHours">Hours from the check to a person's approval; null when no person approved it.</param>
    internal void AddDocument(int sign, int pages, DocumentStatus status, DocumentConfidentiality confidentiality, double confidenceSum,
        int confidencePages, int lowPages, bool autoArchived, ExceptionReason? reason, double? reviewHours)
    {
        Documents += sign;
        Pages += sign * pages;
        _statuses[(int)status] += sign;
        ConfidenceSum += sign * confidenceSum;
        ConfidencePages += sign * confidencePages;
        LowPages += sign * lowPages;
        if (autoArchived)
            AutoArchived += sign;
        if (reason is { } flaggedFor)
        {
            Flagged += sign;
            _raised[(int)flaggedFor] += sign;
            if (status == DocumentStatus.NeedsReview)
                _open[(int)flaggedFor] += sign;
            if (reviewHours is { } hours)
            {
                _resolved[(int)flaggedFor] += sign;
                _resolveHours[(int)flaggedFor] += sign * hours;
            }
        }

        if (reviewHours is { } reviewed)
        {
            Reviewed += sign;
            ReviewHours += sign * reviewed;
        }

        if (status == DocumentStatus.Failed)
        {
            _raised[(int)ExceptionReason.OcrFailed] += sign;
            _open[(int)ExceptionReason.OcrFailed] += sign;
        }

        if (status == DocumentStatus.Archived)
        {
            ArchivedPages += sign * pages;
            _archivedByConfidentiality[(int)confidentiality] += sign;
        }
    }
}

/// <summary>What one person did on some days: documents scanned (on their upload day) and reviewed (on their approval day).</summary>
public sealed class UserFacts
{
    public int Uploaded { get; private set; }

    public int UploadedPages { get; private set; }

    /// <summary>Days with at least one upload, for the pages per hour of a shift.</summary>
    public int UploadDays { get; private set; }

    public int Reviewed { get; private set; }

    /// <summary>Reviews whose working time is known (the simulated ones), and that time.</summary>
    public int TimedReviews { get; private set; }

    public double ReviewSeconds { get; private set; }

    internal void Add(UserFacts other, int sign = 1)
    {
        Uploaded += sign * other.Uploaded;
        UploadedPages += sign * other.UploadedPages;
        Reviewed += sign * other.Reviewed;
        TimedReviews += sign * other.TimedReviews;
        ReviewSeconds += sign * other.ReviewSeconds;
    }

    internal void AddUpload(int sign, int pages)
    {
        Uploaded += sign;
        UploadedPages += sign * pages;
    }

    internal void AddReview(int sign, double? seconds)
    {
        Reviewed += sign;
        if (seconds is { } value)
        {
            TimedReviews += sign;
            ReviewSeconds += sign * value;
        }
    }

    internal void CountUploadDay() => UploadDays++;
}

/// <summary>The figures of every document known now: the simulated history, the samples and the OCR jobs of this app.</summary>
public sealed class ProjectFacts
{
    private readonly IReadOnlyDictionary<(int Day, DocumentDepartment? Department), DayFacts> _days;
    private readonly IReadOnlyDictionary<(int Day, string User), UserFacts> _users;
    private readonly IReadOnlyDictionary<(int Day, int Hour), int> _hourPages;
    private readonly IReadOnlyDictionary<(DocumentDepartment? Department, DocumentKind? Kind, DocumentConfidentiality Confidentiality), int> _cube;

    internal ProjectFacts(DateOnly today, DateOnly firstDay,
        IReadOnlyDictionary<(int Day, DocumentDepartment? Department), DayFacts> days,
        IReadOnlyDictionary<(int Day, string User), UserFacts> users,
        IReadOnlyDictionary<(int Day, int Hour), int> hourPages,
        IReadOnlyDictionary<(DocumentDepartment? Department, DocumentKind? Kind, DocumentConfidentiality Confidentiality), int> cube)
    {
        Today = today;
        FirstDay = firstDay;
        _days = days;
        _users = users;
        _hourPages = hourPages;
        _cube = cube;
        AllTime = new DayFacts();
        foreach (var facts in days.Values)
            AllTime.Add(facts);
    }

    public DateOnly Today { get; }

    /// <summary>The first day with documents: the project start when the history is on.</summary>
    public DateOnly FirstDay { get; }

    /// <summary>Every document, whatever its day.</summary>
    public DayFacts AllTime { get; }

    /// <summary>The review queue now.</summary>
    public int WaitingReview => AllTime.Status(DocumentStatus.NeedsReview);

    /// <summary>Documents in the review queue because the automatic check flagged them (the rest were not checked yet).</summary>
    public int WaitingFlagged =>
        Enum.GetValues<ExceptionReason>().Where(r => r != ExceptionReason.OcrFailed).Sum(AllTime.Open);

    public DayFacts Sum(DateOnly from, DateOnly to)
    {
        var total = new DayFacts();
        foreach (var (_, facts) in ByDepartment(from, to))
            total.Add(facts);
        return total;
    }

    /// <summary>Every department (with zeros), then documents whose department is not set yet when there are any.</summary>
    public IReadOnlyList<(DocumentDepartment? Department, DayFacts Facts)> ByDepartment(DateOnly from, DateOnly to)
    {
        var rows = new List<(DocumentDepartment? Department, DayFacts Facts)>();
        foreach (var department in Departments)
        {
            var facts = new DayFacts();
            for (var day = from.DayNumber; day <= to.DayNumber; day++)
            {
                if (_days.TryGetValue((day, department), out var found))
                    facts.Add(found);
            }

            if (department is not null || facts.Documents != 0)
                rows.Add((department, facts));
        }

        return rows;
    }

    /// <summary>People who scanned or reviewed in the period, by user name.</summary>
    public IReadOnlyList<(string User, UserFacts Facts)> ByUser(DateOnly from, DateOnly to)
    {
        var totals = new Dictionary<string, UserFacts>(StringComparer.OrdinalIgnoreCase);
        foreach (var ((day, user), facts) in _users)
        {
            if (day < from.DayNumber || day > to.DayNumber)
                continue;
            if (!totals.TryGetValue(user, out var total))
                totals[user] = total = new UserFacts();
            total.Add(facts);
            if (facts.Uploaded > 0)
                total.CountUploadDay();
        }

        return totals.Where(t => t.Value.Uploaded != 0 || t.Value.Reviewed != 0)
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => (t.Key, t.Value))
            .ToList();
    }

    /// <summary>People who uploaded at least one document in the period.</summary>
    public int Operators(DateOnly from, DateOnly to) => ByUser(from, to).Count(u => u.Facts.Uploaded > 0);

    public int HourPages(DateOnly day, int hour) => _hourPages.GetValueOrDefault((day.DayNumber, hour));

    /// <summary>Hours of <paramref name="day"/> with pages, for the hourly chart.</summary>
    public IEnumerable<int> HoursWithPages(DateOnly day) =>
        _hourPages.Where(h => h.Key.Day == day.DayNumber && h.Value != 0).Select(h => h.Key.Hour);

    /// <summary>Documents of every status whose department, type and confidentiality match.</summary>
    public int Count(Func<DocumentDepartment?, DocumentKind?, DocumentConfidentiality, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _cube.Where(c => predicate(c.Key.Department, c.Key.Kind, c.Key.Confidentiality)).Sum(c => c.Value);
    }

    private static readonly DocumentDepartment?[] Departments =
        [.. Enum.GetValues<DocumentDepartment>().Select(d => (DocumentDepartment?)d), null];
}

/// <summary>
/// Adds documents up into <see cref="ProjectFacts"/>. A document counts on the day it was uploaded (local time); a
/// review counts for the reviewer on the day of the approval. The sign lets a stored copy of a simulated document
/// replace the simulated one (taken off with -1, the stored copy added with +1).
/// </summary>
internal sealed class FactsBuilder
{
    private readonly Dictionary<(int Day, DocumentDepartment? Department), DayFacts> _days = [];
    private readonly Dictionary<(int Day, string User), UserFacts> _users = [];
    private readonly Dictionary<(int Day, int Hour), int> _hourPages = [];
    private readonly Dictionary<(DocumentDepartment? Department, DocumentKind? Kind, DocumentConfidentiality Confidentiality), int> _cube = [];

    public static ExceptionReason ReasonOf(QualityIssueKind kind) => kind switch
    {
        QualityIssueKind.LowDocumentConfidence => ExceptionReason.LowDocumentConfidence,
        QualityIssueKind.LowPageConfidence => ExceptionReason.LowPageConfidence,
        QualityIssueKind.PageWarnings or QualityIssueKind.DuplicatePage => ExceptionReason.PageWarnings,
        QualityIssueKind.MissingMetadata => ExceptionReason.MissingMetadata,
        _ => ExceptionReason.EmptyPage,
    };

    /// <summary>Adds the figures of another builder with its days moved by <paramref name="dayShift"/> (the history is kept by day index).</summary>
    public void Add(FactsBuilder other, int dayShift)
    {
        foreach (var ((day, department), facts) in other._days)
            Day(day + dayShift, department).Add(facts);
        foreach (var ((day, user), facts) in other._users)
            User(day + dayShift, user).Add(facts);
        foreach (var ((day, hour), pages) in other._hourPages)
            AddHour(day + dayShift, hour, pages);
        foreach (var (key, count) in other._cube)
            _cube[key] = _cube.GetValueOrDefault(key) + count;
    }

    /// <param name="firstDay">Day number of the first history day; history times count seconds from its midnight.</param>
    public void Add(in HistoryEntry entry, int index, int firstDay, int sign)
    {
        var day = firstDay + (entry.Uploaded / HistoryProfile.SecondsPerDay);
        var confidence = entry.Confidence ?? 0;
        double? reviewHours = entry.ApprovedByReviewer ? (entry.Approved - entry.Checked) / 3600.0 : null;
        Day(day, entry.Department).AddDocument(sign, entry.Pages, entry.Status, entry.Confidentiality, confidence * entry.TextPages,
            entry.TextPages, entry.LowPages, entry.ApprovedByCheck, entry.Reason is { } reason ? ReasonOf(reason) : null, reviewHours);

        User(day, HistoryProfile.Operators[entry.Uploader].UserName).AddUpload(sign, entry.Pages);
        if (entry.ApprovedByReviewer)
        {
            var approvalDay = firstDay + (entry.Approved / HistoryProfile.SecondsPerDay);
            User(approvalDay, HistoryProfile.Reviewers[entry.Approver].UserName).AddReview(sign, ProjectHistory.ReviewSeconds(index, entry.Approver));
        }

        AddHour(day, (entry.Uploaded % HistoryProfile.SecondsPerDay) / 3600, sign * entry.Pages);
        AddCube(entry.Department, entry.Kind, entry.Confidentiality, sign);
    }

    public void Add(DocumentRecord record, int sign, TimeZoneInfo zone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(record);
        var uploadedUtc = record.UploadedAtUtc ?? record.Job?.CreatedAtUtc;
        var uploaded = uploadedUtc is { } utc ? Local(utc, zone) : today.ToDateTime(TimeOnly.MinValue);
        var day = DateOnly.FromDateTime(uploaded).DayNumber;
        var metadata = record.Metadata;

        var confidences = record.PageConfidences?.OfType<double>().ToList() ?? [];
        var threshold = record.Quality?.Settings.MinPageConfidence ?? QualitySettings.Default.MinPageConfidence;
        var approval = record.Approval;
        var byPerson = approval is not null && approval.By != DocumentStore.QualityCheckAuthor;
        var checkedUtc = record.Quality?.CheckedAtUtc ?? record.Job?.FinishedAtUtc ?? uploadedUtc;
        double? reviewHours = byPerson && checkedUtc is { } from ? Math.Max(0, (approval!.AtUtc - from).TotalHours) : null;
        var reason = record.Quality is { Passed: false } check ? ReasonOf(check.Issues[0].Kind) : (ExceptionReason?)null;

        Day(day, metadata.Department).AddDocument(sign, record.Pages, record.Status, metadata.Confidentiality, confidences.Sum(),
            confidences.Count, confidences.Count(c => c < threshold), approval?.By == DocumentStore.QualityCheckAuthor, reason, reviewHours);

        if (!string.IsNullOrWhiteSpace(record.UploadedBy))
            User(day, record.UploadedBy).AddUpload(sign, record.Pages);
        if (byPerson)
            User(DateOnly.FromDateTime(Local(approval!.AtUtc, zone)).DayNumber, approval.By).AddReview(sign, null);

        AddHour(day, uploaded.Hour, sign * record.Pages);
        AddCube(metadata.Department, metadata.Kind, metadata.Confidentiality, sign);
    }

    public ProjectFacts Build(DateOnly today, DateOnly firstDay) => new(today, firstDay, _days, _users, _hourPages, _cube);

    /// <summary>The first day any document was uploaded, or null when there are none.</summary>
    public DateOnly? FirstDocumentDay()
    {
        var days = _days.Where(d => d.Value.Documents != 0).Select(d => d.Key.Day).ToList();
        return days.Count == 0 ? null : DateOnly.FromDayNumber(days.Min());
    }

    private DayFacts Day(int day, DocumentDepartment? department)
    {
        if (!_days.TryGetValue((day, department), out var facts))
            _days[(day, department)] = facts = new DayFacts();
        return facts;
    }

    private UserFacts User(int day, string user)
    {
        if (!_users.TryGetValue((day, user), out var facts))
            _users[(day, user)] = facts = new UserFacts();
        return facts;
    }

    private void AddHour(int day, int hour, int pages) => _hourPages[(day, hour)] = _hourPages.GetValueOrDefault((day, hour)) + pages;

    private void AddCube(DocumentDepartment? department, DocumentKind? kind, DocumentConfidentiality confidentiality, int sign) =>
        _cube[(department, kind, confidentiality)] = _cube.GetValueOrDefault((department, kind, confidentiality)) + sign;

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
}
