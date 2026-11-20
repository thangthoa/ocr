using System.Globalization;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Activity;

/// <summary>
/// A month of made-up activity for the activity log mockup, the same on every run for the same day. It follows the
/// digitization line: capture operators upload, the automatic check archives or flags, reviewers correct and approve,
/// readers search, open, download and print, administrators manage users and settings. It also has the security
/// events the customer asked about: failed sign-ins and one-time codes, a locked account, refused access, and an OCR
/// failure that was retried. The demo account (<c>admin</c>) is left out, so its own actions stand out.
/// </summary>
public sealed class SampleActivity(TimeProvider time)
{
    public const int Days = 30;

    private const string QualityCheck = "quality-check";
    private const string OcrService = "ocr-service";
    private const string AttackerIp = "185.220.101.47";

    private static readonly string[] SearchTerms =
        ["عقد صيانة", "أمر شراء ٣٨٨", "PV-2019", "قرار تعيين", "سند صرف", "invoice 2021", "إيجار مبنى", "ترقية موظف", "CT-2020"];

    private static readonly (DocumentDepartment Department, string Code)[] Codes =
    [
        (DocumentDepartment.Finance, "PV"),
        (DocumentDepartment.Procurement, "PO"),
        (DocumentDepartment.Legal, "CT"),
        (DocumentDepartment.HumanResources, "HR"),
        (DocumentDepartment.Administration, "CR"),
    ];

    private readonly Lazy<IReadOnlyList<AuditEntry>> _entries = new(() => Generate(time));

    /// <summary>Oldest first, up to the moment the app first asked for them.</summary>
    public IReadOnlyList<AuditEntry> Entries => _entries.Value;

    private static List<AuditEntry> Generate(TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone));
        var log = new Log(now, zone);
        var pool = new DocumentPool();

        for (var offset = Days - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            var dice = new Dice((ulong)day.DayNumber);
            Incidents(log, dice, day, offset);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            foreach (var actor in Actor.Staff)
            {
                if (actor.Role == UserRole.Administrator && !dice.Chance(60))
                    continue;
                WorkDay(log, dice, pool, actor, day);
            }
        }

        return log.Entries.OrderBy(e => e.AtUtc).ToList();
    }

    /// <summary>Signs in, works until the afternoon, usually signs out.</summary>
    private static void WorkDay(Log log, Dice dice, DocumentPool pool, Actor actor, DateOnly day)
    {
        var at = day.ToDateTime(new TimeOnly(7, 30)).AddMinutes(dice.Between(0, 70));
        if (dice.Chance(6))
            log.Add(at.AddMinutes(-2), actor, AuditAction.SignInFailed);
        if (dice.Chance(4))
            log.Add(at.AddMinutes(-1), actor, AuditAction.OtpFailed);
        log.Add(at, actor, AuditAction.SignedIn);

        var end = day.ToDateTime(new TimeOnly(15, 30)).AddMinutes(dice.Between(0, 60));
        at = at.AddMinutes(dice.Between(3, 12));
        while (at < end)
        {
            at = actor.Role switch
            {
                UserRole.CaptureOperator => Upload(log, dice, pool, actor, at),
                UserRole.Reviewer => Review(log, dice, pool, actor, at),
                UserRole.RecordsManager => ManageRecords(log, dice, pool, actor, at),
                UserRole.Administrator => Administer(log, dice, actor, at),
                _ => Read(log, dice, pool, actor, at),
            };
        }

        if (dice.Chance(75))
            log.Add(at.AddMinutes(dice.Between(1, 10)), actor, AuditAction.SignedOut);
    }

    private static DateTime Upload(Log log, Dice dice, DocumentPool pool, Actor actor, DateTime at)
    {
        var document = pool.Create(dice, at);
        log.Add(at, actor, AuditAction.Uploaded, document,
            batch: string.Create(CultureInfo.InvariantCulture, $"UP-{at:yyMMdd}-{dice.Between(1, 99):00}"));

        var checkedAt = at.AddMinutes(dice.Between(2, 6));
        if (dice.Chance(4))
        {
            log.Add(checkedAt, Actor.Ocr, AuditAction.OcrFailed, document, detail: dice.Pick(["gpu_worker_failed", "timeout", "remote_job_lost"]));
            checkedAt = checkedAt.AddMinutes(dice.Between(8, 15));
            log.Add(checkedAt, Actor.Ocr, AuditAction.OcrRetried, document);
            checkedAt = checkedAt.AddMinutes(dice.Between(2, 5));
        }

        if (dice.Chance(22))
        {
            log.Add(checkedAt, Actor.Quality, AuditAction.QualityChecked, document, status: DocumentStatus.NeedsReview,
                detail: dice.Between(1, 4).ToString(CultureInfo.InvariantCulture));
            pool.Flag(document, checkedAt);
        }
        else
        {
            log.Add(checkedAt, Actor.Quality, AuditAction.QualityChecked, document, status: DocumentStatus.Archived, detail: "0");
        }

        return at.AddMinutes(dice.Between(12, 30));
    }

    /// <summary>Takes the oldest flagged document they may see, corrects it in Word and approves it.</summary>
    private static DateTime Review(Log log, Dice dice, DocumentPool pool, Actor actor, DateTime at)
    {
        if (pool.NextFlagged(actor, at) is not { } document)
        {
            log.Add(at, actor, AuditAction.Viewed, pool.Archived(dice, actor));
            return at.AddMinutes(dice.Between(10, 25));
        }

        log.Add(at, actor, AuditAction.Viewed, document);
        at = at.AddMinutes(dice.Between(4, 12));
        log.Add(at, actor, AuditAction.EditedInWord, document, version: dice.Between(2, 3).ToString(CultureInfo.InvariantCulture));
        if (dice.Chance(50))
        {
            at = at.AddMinutes(dice.Between(1, 3));
            log.Add(at, actor, AuditAction.MetadataChanged, document, version: "1.2");
        }

        at = at.AddMinutes(dice.Between(1, 3));
        log.Add(at, actor, AuditAction.StatusChanged, document, status: DocumentStatus.Archived);
        return at.AddMinutes(dice.Between(5, 15));
    }

    private static DateTime ManageRecords(Log log, Dice dice, DocumentPool pool, Actor actor, DateTime at)
    {
        var document = pool.Archived(dice, actor);
        switch (dice.Next(10))
        {
            case 0:
                log.Add(at, actor, AuditAction.Exported, detail: dice.Between(20, 400).ToString(CultureInfo.InvariantCulture));
                break;
            case 1:
                log.Add(at, actor, AuditAction.ReportExported, detail: dice.Pick(["Progress", "Quality", "Archive", "Exceptions"]));
                break;
            case 2 when dice.Chance(30):
                log.Add(at, actor, AuditAction.Viewed, document);
                log.Add(at.AddMinutes(2), actor, AuditAction.Deleted, document);
                break;
            case 3 when dice.Chance(40):
                log.Add(at, actor, AuditAction.Viewed, document);
                log.Add(at.AddMinutes(3), actor, AuditAction.StatusChanged, document, status: DocumentStatus.NeedsReview);
                break;
            default:
                log.Add(at, actor, AuditAction.Searched, detail: dice.Pick(SearchTerms));
                log.Add(at.AddMinutes(1), actor, AuditAction.Viewed, document);
                log.Add(at.AddMinutes(3), actor, AuditAction.Downloaded, document, version: "1.2", detail: dice.Pick(["PDF", "DOCX"]));
                break;
        }

        return at.AddMinutes(dice.Between(20, 45));
    }

    private static DateTime Administer(Log log, Dice dice, Actor actor, DateTime at)
    {
        switch (dice.Next(12))
        {
            case 0:
                log.Add(at, actor, AuditAction.RolesChanged);
                break;
            case 1:
                log.Add(at, actor, AuditAction.QualitySettingsChanged);
                break;
            case 2:
                log.Add(at, actor, AuditAction.KpiSettingsChanged);
                break;
            case 3:
                log.Add(at, actor, AuditAction.ActivityExported, detail: dice.Between(800, 3_000).ToString(CultureInfo.InvariantCulture));
                break;
            default:
                log.Add(at, actor, AuditAction.UserChanged, detail: dice.Pick(Actor.Staff).User);
                break;
        }

        return at.AddMinutes(dice.Between(60, 150));
    }

    /// <summary>Searches, opens a document of their department, sometimes downloads or prints it; now and then tries one they may not see.</summary>
    private static DateTime Read(Log log, Dice dice, DocumentPool pool, Actor actor, DateTime at)
    {
        log.Add(at, actor, AuditAction.Searched, detail: dice.Pick(SearchTerms));
        at = at.AddMinutes(1);
        if (dice.Chance(7) && pool.OutOfReach(dice, actor) is { } refused)
        {
            log.Add(at, actor, AuditAction.AccessDenied, refused.Document, detail: refused.Reason);
            return at.AddMinutes(dice.Between(20, 50));
        }

        var document = pool.Archived(dice, actor);
        log.Add(at, actor, AuditAction.Viewed, document);
        if (dice.Chance(40))
            log.Add(at.AddMinutes(dice.Between(2, 6)), actor, AuditAction.Downloaded, document, version: "1.2", detail: dice.Pick(["PDF", "DOCX", "PDF"]));
        if (dice.Chance(25))
            log.Add(at.AddMinutes(dice.Between(6, 9)), actor, AuditAction.Printed, document, version: "1.2");
        return at.AddMinutes(dice.Between(20, 50));
    }

    /// <summary>Night-time password guessing from outside, ending in a locked account, on a few fixed days of the month.</summary>
    private static void Incidents(Log log, Dice dice, DateOnly day, int daysAgo)
    {
        if (daysAgo is not (4 or 16 or 25))
            return;

        var target = daysAgo == 16 ? Actor.Outsider("administrator") : Actor.Outsider("it.admin");
        var at = day.ToDateTime(new TimeOnly(2, 10)).AddMinutes(dice.Between(0, 40));
        for (var attempt = 0; attempt < 5; attempt++)
            log.Add(at.AddSeconds(attempt * dice.Between(4, 12)), target, AuditAction.SignInFailed);
        if (target.User == "it.admin")
            log.Add(at.AddSeconds(70), target, AuditAction.AccountLocked);
    }

    private sealed record Actor(string User, UserRole Role, string Ip, string Client, IReadOnlyList<DocumentDepartment> Departments,
        DocumentConfidentiality Clearance)
    {
        private static readonly DocumentDepartment[] All = [];

        /// <summary>The sample accounts of <see cref="UserDirectory"/>, without the demo account and the disabled one.</summary>
        public static readonly Actor[] Staff =
        [
            new("it.admin", UserRole.Administrator, "10.20.1.15", "Edge 131 · Windows 11", All, DocumentConfidentiality.Secret),
            new("capture01", UserRole.CaptureOperator, "10.20.4.21", "Chrome 131 · Windows 11", All, DocumentConfidentiality.Internal),
            new("capture02", UserRole.CaptureOperator, "10.20.4.22", "Chrome 131 · Windows 10", All, DocumentConfidentiality.Internal),
            new("review01", UserRole.Reviewer, "10.20.5.31", "Edge 131 · Windows 11", All, DocumentConfidentiality.Confidential),
            new("review02", UserRole.Reviewer, "10.20.5.32", "Edge 131 · Windows 11",
                [DocumentDepartment.Finance, DocumentDepartment.Procurement], DocumentConfidentiality.Confidential),
            new("records01", UserRole.RecordsManager, "10.20.2.11", "Edge 131 · Windows 11", All, DocumentConfidentiality.Secret),
            new("reader.finance", UserRole.Reader, "10.20.6.41", "Safari 18 · macOS 15", [DocumentDepartment.Finance], DocumentConfidentiality.Confidential),
            new("reader.hr", UserRole.Reader, "10.20.6.42", "Chrome 131 · Windows 11", [DocumentDepartment.HumanResources], DocumentConfidentiality.Confidential),
            new("reader.legal", UserRole.Reader, "10.20.6.43", "Edge 131 · Windows 11", [DocumentDepartment.Legal], DocumentConfidentiality.Secret),
        ];

        public static readonly Actor Quality = new(QualityCheck, UserRole.Administrator, "10.20.9.4", "OCR System (automatic check)", All, DocumentConfidentiality.Secret);

        public static readonly Actor Ocr = new(OcrService, UserRole.Administrator, "10.20.9.5", "OCR service API", All, DocumentConfidentiality.Secret);

        public static Actor Outsider(string userName) =>
            new(userName, UserRole.Reader, AttackerIp, "python-requests/2.32", All, DocumentConfidentiality.Internal);

        public bool CanSee(SampleDocument document) =>
            (Departments.Count == 0 || Departments.Contains(document.Department)) && document.Confidentiality <= Clearance;
    }

    private sealed record SampleDocument(string Id, string Name, DocumentDepartment Department, DocumentConfidentiality Confidentiality);

    /// <summary>The documents the month works on: the 14 sample documents, then everything uploaded during the month.</summary>
    private sealed class DocumentPool
    {
        private readonly List<SampleDocument> _archived;
        private readonly List<(SampleDocument Document, DateTime FlaggedAt)> _flagged = [];
        private int _next = 200_001;

        public DocumentPool() =>
            _archived = SampleDocumentSeed.Create()
                .Where(d => d.Metadata.Department is not null)
                .Select(d => new SampleDocument(d.Id, d.FileName, d.Metadata.Department!.Value, d.Metadata.Confidentiality))
                .ToList();

        public SampleDocument Create(Dice dice, DateTime at)
        {
            var (department, code) = dice.Pick(Codes);
            var confidentiality = dice.Next(100) switch
            {
                < 62 => DocumentConfidentiality.Internal,
                < 92 => DocumentConfidentiality.Confidential,
                _ => DocumentConfidentiality.Secret,
            };
            var name = string.Create(CultureInfo.InvariantCulture, $"{code}-{dice.Between(2010, 2024)}-{dice.Between(1, 999):0000}.pdf");
            var document = new SampleDocument(string.Create(CultureInfo.InvariantCulture, $"DOC-{at:yyyy}-{_next++:000000}"), name, department, confidentiality);
            _archived.Add(document);
            return document;
        }

        public void Flag(SampleDocument document, DateTime at) => _flagged.Add((document, at));

        public SampleDocument? NextFlagged(Actor reviewer, DateTime at)
        {
            var index = _flagged.FindIndex(f => f.FlaggedAt <= at && reviewer.CanSee(f.Document));
            if (index < 0)
                return null;
            var document = _flagged[index].Document;
            _flagged.RemoveAt(index);
            return document;
        }

        public SampleDocument Archived(Dice dice, Actor actor)
        {
            var visible = _archived.Where(actor.CanSee).ToList();
            return visible.Count > 0 ? dice.Pick(visible) : _archived[0];
        }

        /// <summary>A document the actor may not open, and why: another department, or above their clearance.</summary>
        public (SampleDocument Document, string Reason)? OutOfReach(Dice dice, Actor actor)
        {
            var refused = _archived.Where(d => !actor.CanSee(d)).ToList();
            if (refused.Count == 0)
                return null;
            var document = dice.Pick(refused);
            var reason = actor.Departments.Count > 0 && !actor.Departments.Contains(document.Department) ? "Department" : "Clearance";
            return (document, reason);
        }
    }

    private sealed class Log(DateTime nowUtc, TimeZoneInfo zone)
    {
        public List<AuditEntry> Entries { get; } = [];

        /// <param name="local">Local time of the event; events after now are left out (today is not over yet).</param>
        public void Add(DateTime local, Actor actor, AuditAction action, SampleDocument? document = null, string? batch = null,
            string? version = null, DocumentStatus? status = null, string? detail = null)
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
            if (utc > nowUtc)
                return;
            Entries.Add(new AuditEntry(utc, actor.User, action, document?.Id, document?.Name, batch, version, status, detail,
                actor.Ip, actor.Client));
        }
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
