using System.Globalization;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;

namespace OppOcr.Web.Features.Mockups.Activity;

/// <summary>
/// The activity log of the 30 days before today, made from the simulated project history (<see cref="ProjectHistory"/>),
/// so it names the same documents, people and times as the document lists: each capture operator's shift and
/// upload batches (one entry per batch, with its number of documents), OCR failures, the documents the automatic check
/// flagged, and the reviewers correcting them in Word and approving them. Office staff (records manager, readers,
/// administrator) work on weekdays: they search, open, download and print archived documents. It also has the security
/// events the customer asked about: failed sign-ins and one-time codes, a locked account and refused access. The demo
/// account (<c>admin</c>) is left out, so its own actions stand out. Nothing is simulated today, as in the history.
/// </summary>
/// <remarks>
/// The automatic check is logged for the documents it flagged only (a million "passed" entries would bury the rest).
/// With the history off (the component tests), the documents still come from it but exist only in the log.
/// </remarks>
public sealed class SampleActivity(TimeProvider time, ProjectHistory history)
{
    public const int Days = 30;

    /// <summary>The Word file a reviewer saves: version 1 is the file made from the OCR text.</summary>
    public const string ReviewWordVersion = "2";

    private const string QualityCheck = DocumentStore.QualityCheckAuthor;
    private const string OcrService = DocumentStore.OcrAuthor;
    private const string AttackerIp = "185.220.101.47";

    /// <summary>Documents whose first OCR attempt failed and was retried, among those that did not fail in the end.</summary>
    private const double RetriedShare = 0.001;

    private static readonly string[] SearchTerms =
        ["عقد صيانة", "أمر شراء", "PV-2019", "قرار تعيين", "سند صرف", "invoice 2021", "إيجار مبنى", "ترقية موظف", "CT-2020"];

    private static readonly string[] OcrErrors = ["gpu_worker_failed", "timeout", "remote_job_lost"];

    private readonly Lazy<IReadOnlyList<AuditEntry>> _entries = new(() => new Generator(time, history).Run());

    /// <summary>Oldest first, up to the end of yesterday.</summary>
    public IReadOnlyList<AuditEntry> Entries => _entries.Value;

    private sealed class Generator(TimeProvider time, ProjectHistory history)
    {
        private readonly DateTime _now = time.GetUtcNow().UtcDateTime;
        private readonly TimeZoneInfo _zone = time.LocalTimeZone;
        private readonly List<AuditEntry> _entries = [];
        private DateOnly _firstDay;

        public List<AuditEntry> Run()
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_now, _zone));
            _firstDay = ProjectHistory.FirstDay(today);
            var reviews = ReviewsByDay();
            for (var day = HistoryProfile.Days - Days; day < HistoryProfile.Days; day++)
            {
                var date = _firstDay.AddDays(day);
                var dice = new Dice((ulong)date.DayNumber);
                Incidents(dice, date, today.DayNumber - date.DayNumber);
                Scanning(dice, day);
                Reviewing(dice, day, reviews.GetValueOrDefault(day) ?? []);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                foreach (var actor in Actor.Office)
                {
                    if (actor.Role == UserRole.Administrator && !dice.Chance(60))
                        continue;
                    OfficeDay(dice, actor, day);
                }
            }

            return _entries.OrderBy(e => e.AtUtc).ToList();
        }

        /// <summary>The documents each reviewer approved, by history day of the approval, in time order.</summary>
        private Dictionary<int, List<int>> ReviewsByDay()
        {
            var byDay = new Dictionary<int, List<int>>();
            var (start, _) = history.DayRange(HistoryProfile.Days - Days - 1);
            for (var index = start; index < history.SimulatedCount; index++)
            {
                var entry = history.Entry(index);
                if (!entry.ApprovedByReviewer)
                    continue;
                var day = entry.Approved / HistoryProfile.SecondsPerDay;
                if (!byDay.TryGetValue(day, out var list))
                    byDay[day] = list = [];
                list.Add(index);
            }

            foreach (var list in byDay.Values)
                list.Sort((a, b) => history.Entry(a).Approved.CompareTo(history.Entry(b).Approved));
            return byDay;
        }

        /// <summary>
        /// Each operator's shift: sign-in, one upload entry per hour batch (as many documents as the history has), sign-out;
        /// and what the OCR service and the automatic check reported about those documents.
        /// </summary>
        private void Scanning(Dice dice, int day)
        {
            var (start, end) = history.DayRange(day);
            var batches = new SortedDictionary<(int Operator, int Hour), (int First, int Count)>();
            for (var index = start; index < end; index++)
            {
                var entry = history.Entry(index);
                var hour = entry.Uploaded % HistoryProfile.SecondsPerDay / 3600;
                var key = ((int)entry.Uploader, hour);
                batches[key] = batches.TryGetValue(key, out var batch) ? (batch.First, batch.Count + 1) : (index, 1);

                if (entry.Status == DocumentStatus.Failed)
                {
                    Add(entry.Checked, Actor.Ocr, AuditAction.OcrFailed, index, detail: OcrErrors[index % OcrErrors.Length]);
                    continue;
                }

                if (HistoryRandom.Unit(index, 51) < RetriedShare)
                {
                    Add(entry.Checked - 720, Actor.Ocr, AuditAction.OcrFailed, index, detail: OcrErrors[index % OcrErrors.Length]);
                    Add(entry.Checked - 600, Actor.Ocr, AuditAction.OcrRetried, index);
                }

                if (entry.Reason is not null)
                {
                    Add(entry.Checked, Actor.Quality, AuditAction.QualityChecked, index, status: DocumentStatus.NeedsReview,
                        detail: IssueCount(entry).ToString(CultureInfo.InvariantCulture));
                }
            }

            foreach (var group in batches.GroupBy(b => b.Key.Operator))
            {
                var actor = Actor.Operator(group.Key);
                var first = group.First();
                var shiftStart = (day * HistoryProfile.SecondsPerDay) + (first.Key.Hour * 3600) - dice.Between(240, 600);
                if (dice.Chance(5))
                    Add(shiftStart - 90, actor, AuditAction.SignInFailed);
                Add(shiftStart, actor, AuditAction.SignedIn);
                var last = shiftStart;
                foreach (var ((_, hour), (firstIndex, count)) in group)
                {
                    last = history.Entry(firstIndex + count - 1).Uploaded;
                    var date = _firstDay.AddDays(day);
                    Add(history.Entry(firstIndex).Uploaded, actor, AuditAction.Uploaded,
                        batch: ProjectHistory.BatchId(date, hour, group.Key), detail: count.ToString(CultureInfo.InvariantCulture));
                }

                if (dice.Chance(85))
                    Add(last + dice.Between(120, 900), actor, AuditAction.SignedOut);
            }
        }

        /// <summary>Each reviewer's day: sign-in, then every document they approved: corrected in Word (some), then approved.</summary>
        private void Reviewing(Dice dice, int day, List<int> approved)
        {
            foreach (var group in approved.GroupBy(i => history.Entry(i).Approver))
            {
                var actor = Actor.Reviewer(group.Key);
                var first = history.Entry(group.First()).Approved - ProjectHistory.ReviewSeconds(group.First(), group.Key);
                if (dice.Chance(4))
                    Add(first - 300, actor, AuditAction.OtpFailed);
                Add(first - dice.Between(60, 240), actor, AuditAction.SignedIn);
                var last = first;
                foreach (var index in group)
                {
                    var entry = history.Entry(index);
                    if (entry.Edited)
                        Add(entry.Approved - 60, actor, AuditAction.EditedInWord, index, version: ReviewWordVersion);
                    Add(entry.Approved, actor, AuditAction.StatusChanged, index, status: DocumentStatus.Archived);
                    last = entry.Approved;
                }

                if (dice.Chance(80))
                    Add(last + dice.Between(60, 600), actor, AuditAction.SignedOut);
            }
        }

        /// <summary>Signs in in the morning, works until the afternoon, usually signs out.</summary>
        private void OfficeDay(Dice dice, Actor actor, int day)
        {
            var at = (day * HistoryProfile.SecondsPerDay) + (7 * 3600) + 1800 + (dice.Between(0, 70) * 60);
            if (dice.Chance(6))
                Add(at - 120, actor, AuditAction.SignInFailed);
            if (dice.Chance(4))
                Add(at - 60, actor, AuditAction.OtpFailed);
            Add(at, actor, AuditAction.SignedIn);

            var end = (day * HistoryProfile.SecondsPerDay) + (15 * 3600) + 1800 + (dice.Between(0, 60) * 60);
            at += dice.Between(3, 12) * 60;
            while (at < end)
            {
                at = actor.Role switch
                {
                    UserRole.RecordsManager => ManageRecords(dice, actor, at),
                    UserRole.Administrator => Administer(dice, actor, at),
                    _ => Read(dice, actor, at),
                };
            }

            if (dice.Chance(75))
                Add(at + (dice.Between(1, 10) * 60), actor, AuditAction.SignedOut);
        }

        private int ManageRecords(Dice dice, Actor actor, int at)
        {
            switch (dice.Next(10))
            {
                case 0:
                    Add(at, actor, AuditAction.Exported, detail: dice.Between(20, 400).ToString(CultureInfo.InvariantCulture));
                    break;
                case 1:
                    Add(at, actor, AuditAction.ReportExported, detail: dice.Pick(["Progress", "Quality", "Archive", "Exceptions"]));
                    break;
                default:
                    var document = Archived(dice, actor, at);
                    Add(at, actor, AuditAction.Searched, detail: dice.Pick(SearchTerms));
                    Add(at + 60, actor, AuditAction.Viewed, document);
                    Add(at + 180, actor, AuditAction.Downloaded, document, version: Final(document), detail: dice.Pick(["PDF", "DOCX"]));
                    break;
            }

            return at + (dice.Between(20, 45) * 60);
        }

        private int Administer(Dice dice, Actor actor, int at)
        {
            switch (dice.Next(12))
            {
                case 0:
                    Add(at, actor, AuditAction.RolesChanged);
                    break;
                case 1:
                    Add(at, actor, AuditAction.QualitySettingsChanged);
                    break;
                case 2:
                    Add(at, actor, AuditAction.KpiSettingsChanged);
                    break;
                case 3:
                    Add(at, actor, AuditAction.ActivityExported, detail: dice.Between(800, 3_000).ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    Add(at, actor, AuditAction.UserChanged, detail: dice.Pick(Actor.Office).User);
                    break;
            }

            return at + (dice.Between(60, 150) * 60);
        }

        /// <summary>Searches, opens a document of their department, sometimes downloads or prints it; now and then tries one they may not see.</summary>
        private int Read(Dice dice, Actor actor, int at)
        {
            Add(at, actor, AuditAction.Searched, detail: dice.Pick(SearchTerms));
            at += 60;
            if (dice.Chance(7) && OutOfReach(dice, actor, at) is { } refused)
            {
                Add(at, actor, AuditAction.AccessDenied, refused.Index, detail: refused.Reason);
                return at + (dice.Between(20, 50) * 60);
            }

            var document = Archived(dice, actor, at);
            Add(at, actor, AuditAction.Viewed, document);
            if (dice.Chance(40))
                Add(at + (dice.Between(2, 6) * 60), actor, AuditAction.Downloaded, document, version: Final(document), detail: dice.Pick(["PDF", "DOCX", "PDF"]));
            if (dice.Chance(25))
                Add(at + (dice.Between(6, 9) * 60), actor, AuditAction.Printed, document, version: Final(document));
            return at + (dice.Between(20, 50) * 60);
        }

        /// <summary>Night-time password guessing from outside, ending in a locked account, on a few fixed days of the month.</summary>
        private void Incidents(Dice dice, DateOnly date, int daysAgo)
        {
            if (daysAgo is not (4 or 16 or 25))
                return;

            var target = daysAgo == 16 ? Actor.Outsider("administrator") : Actor.Outsider("it.admin");
            var at = ((date.DayNumber - _firstDay.DayNumber) * HistoryProfile.SecondsPerDay) + (2 * 3600) + (10 * 60) + (dice.Between(0, 40) * 60);
            for (var attempt = 0; attempt < 5; attempt++)
                Add(at + (attempt * dice.Between(4, 12)), target, AuditAction.SignInFailed);
            if (target.User == "it.admin")
                Add(at + 70, target, AuditAction.AccountLocked);
        }

        /// <summary>An archived document the actor may open, approved before <paramref name="at"/>.</summary>
        private int Archived(Dice dice, Actor actor, int at)
        {
            var (before, _) = history.DayRange(Math.Max(1, at / HistoryProfile.SecondsPerDay) - 1);
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var index = dice.Next(Math.Max(1, before));
                var entry = history.Entry(index);
                if (entry.Status == DocumentStatus.Archived && entry.Approved < at && actor.CanSee(entry))
                    return index;
            }

            return 0;
        }

        /// <summary>A document the actor may not open, and why: another department, or above their clearance.</summary>
        private (int Index, string Reason)? OutOfReach(Dice dice, Actor actor, int at)
        {
            var (before, _) = history.DayRange(Math.Max(1, at / HistoryProfile.SecondsPerDay) - 1);
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var index = dice.Next(Math.Max(1, before));
                var entry = history.Entry(index);
                if (entry.Status == DocumentStatus.Archived && !actor.CanSee(entry))
                    return (index, actor.Departments.Count > 0 && !actor.Departments.Contains(entry.Department) ? "Department" : "Clearance");
            }

            return null;
        }

        /// <summary>The version people take away: 1.1 when the reviewer corrected the text, otherwise the OCR text.</summary>
        private string Final(int index) => history.Entry(index).Edited ? "1.1" : "1.0";

        /// <summary>The issues <see cref="Quality.QualityCheck.Run"/> lists for the document (one per low page, empty page or warning).</summary>
        private static int IssueCount(HistoryEntry entry) =>
            (entry.Confidence < Quality.QualitySettings.Default.MinDocumentConfidence ? 1 : 0)
            + entry.LowPages
            + (entry.EmptyPage == HistoryEntry.NoPage ? 0 : 1)
            + (entry.WarningPage == HistoryEntry.NoPage ? 0 : 1);

        /// <param name="second">Seconds from midnight of the first history day, local time; later than now is left out.</param>
        private void Add(int second, Actor actor, AuditAction action, int? document = null, string? batch = null, string? version = null,
            DocumentStatus? status = null, string? detail = null)
        {
            var local = _firstDay.ToDateTime(TimeOnly.MinValue).AddSeconds(second);
            if (_zone.IsInvalidTime(local))
                local = local.AddHours(1);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, _zone);
            if (utc > _now)
                return;

            string? id = null, name = null;
            if (document is { } index)
            {
                var entry = history.Entry(index);
                id = HistoryText.Id(index);
                name = HistoryText.FileName(index, entry.Department, entry.Kind);
            }

            _entries.Add(new AuditEntry(utc, actor.User, action, id, name, batch, version, status, detail, actor.Ip, actor.Client));
        }
    }

    private sealed record Actor(string User, UserRole Role, string Ip, string Client, IReadOnlyList<DocumentDepartment> Departments,
        DocumentConfidentiality Clearance)
    {
        private static readonly DocumentDepartment[] All = [];

        /// <summary>The office accounts of <see cref="UserDirectory"/>, without the demo account.</summary>
        public static readonly Actor[] Office =
        [
            new("it.admin", UserRole.Administrator, "10.20.1.15", "Edge 131 · Windows 11", All, DocumentConfidentiality.Secret),
            new("records01", UserRole.RecordsManager, "10.20.2.11", "Edge 131 · Windows 11", All, DocumentConfidentiality.Secret),
            new("reader.finance", UserRole.Reader, "10.20.6.41", "Safari 18 · macOS 15", [DocumentDepartment.Finance], DocumentConfidentiality.Confidential),
            new("reader.hr", UserRole.Reader, "10.20.6.42", "Chrome 131 · Windows 11", [DocumentDepartment.HumanResources], DocumentConfidentiality.Confidential),
            new("reader.legal", UserRole.Reader, "10.20.6.43", "Edge 131 · Windows 11", [DocumentDepartment.Legal], DocumentConfidentiality.Secret),
        ];

        public static readonly Actor Quality = new(QualityCheck, UserRole.Administrator, "10.20.9.4", "OCR System (automatic check)", All, DocumentConfidentiality.Secret);

        public static readonly Actor Ocr = new(OcrService, UserRole.Administrator, "10.20.9.5", "OCR service API", All, DocumentConfidentiality.Secret);

        /// <summary>A capture operator of the history, at their scanning station.</summary>
        public static Actor Operator(int index) =>
            new(HistoryProfile.Operators[index].UserName, UserRole.CaptureOperator, string.Create(CultureInfo.InvariantCulture, $"10.20.4.{21 + index}"),
                index % 2 == 0 ? "Chrome 131 · Windows 11" : "Chrome 131 · Windows 10", All, DocumentConfidentiality.Internal);

        public static Actor Reviewer(int index)
        {
            var reviewer = HistoryProfile.Reviewers[index];
            return new(reviewer.UserName, UserRole.Reviewer, string.Create(CultureInfo.InvariantCulture, $"10.20.5.{31 + index}"),
                "Edge 131 · Windows 11", reviewer.Departments, reviewer.Clearance);
        }

        public static Actor Outsider(string userName) =>
            new(userName, UserRole.Reader, AttackerIp, "python-requests/2.32", All, DocumentConfidentiality.Internal);

        public bool CanSee(HistoryEntry entry) =>
            (Departments.Count == 0 || Departments.Contains(entry.Department)) && entry.Confidentiality <= Clearance;
    }

    /// <summary>A small seeded generator (SplitMix64), so the sample month is the same on every run.</summary>
    private sealed class Dice(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int max)
        {
            _state += 0x9E3779B97F4A7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            z ^= z >> 31;
            return (int)(z % (ulong)max);
        }

        public int Between(int min, int max) => min + Next(max - min + 1);

        public bool Chance(int percent) => Next(100) < percent;

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
    }
}
