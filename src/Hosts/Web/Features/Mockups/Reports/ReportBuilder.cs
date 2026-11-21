using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Kpi;

namespace OppOcr.Web.Features.Mockups.Reports;

/// <summary>
/// Builds the reports of the Reports page and their CSV files. Every report but the activity report adds up the
/// figures of every document (<see cref="DocumentSnapshot.Facts"/>: the simulated history, the samples and the files
/// uploaded in this app), the same figures as the dashboard; the activity report counts the entries of the activity
/// log. Headers and labels are in the UI language of the request.
/// </summary>
public sealed class ReportBuilder(
    IStringLocalizer<MockupText> text,
    KpiSettingsStore kpis,
    AuditLog audit,
    UserDirectory users,
    DocumentCatalog catalog,
    ProjectHistory history,
    TimeProvider time)
{
    /// <summary>A working shift of a capture operator, for the pages per hour.</summary>
    private const double ShiftHours = 8;

    /// <summary>Image, searchable PDF and DOCX of one page, in MB.</summary>
    private const double MegabytesPerPage = 0.42;

    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public async Task<ReportTable> BuildAsync(ReportKind kind, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (ReportRange.Check(from, to, Today) is { } error)
            throw new ArgumentOutOfRangeException(nameof(to), error, "The report period is not valid.");
        if (kind == ReportKind.Activity)
            return Activity(from, to);

        var snapshot = await catalog.SnapshotAsync(cancellationToken);
        var facts = snapshot.Facts;
        var simulated = history.Enabled || snapshot.Stored.Any(d => d.IsSample);
        var (columns, rows, total) = kind switch
        {
            ReportKind.Progress => Progress(facts, from, to),
            ReportKind.Quality => Quality(facts, from, to),
            ReportKind.Productivity => Productivity(facts, from, to),
            ReportKind.Exceptions => Exceptions(facts, from, to),
            ReportKind.Archive => Archive(facts, from, to),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return new ReportTable(kind, from, to, simulated, columns, rows, total);
    }

    /// <summary>The report as CSV (UTF-8 with a BOM so Excel reads Arabic); the export is written to the activity log.</summary>
    internal async Task<DownloadFile> ExportAsync(ReportKind kind, DateOnly from, DateOnly to, string user, CancellationToken cancellationToken)
    {
        var table = await BuildAsync(kind, from, to, cancellationToken);
        var csv = new StringBuilder();
        AppendRow(csv, table.Columns.Select(c => c.Header));
        foreach (var row in table.Rows)
            AppendRow(csv, row.Select(c => c.Csv()));
        if (table.Total is { } total)
            AppendRow(csv, total.Select(c => c.Csv()));

        audit.Record(user, AuditAction.ReportExported, detail: kind.ToString());
        var fileName = string.Create(CultureInfo.InvariantCulture,
            $"report-{kind.ToString().ToLowerInvariant()}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        return new DownloadFile([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())], "text/csv; charset=utf-8", fileName);
    }

    private Report Progress(ProjectFacts facts, DateOnly from, DateOnly to)
    {
        var settings = kpis.Current;
        var target = settings.For(KpiKind.PagesPerDay).Target;
        var project = settings.Project;
        var cumulative = project.Start < from ? facts.Sum(project.Start, from.AddDays(-1)).Pages : 0;

        var rows = new List<IReadOnlyList<ReportCell>>();
        int pagesTotal = 0, documentsTotal = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var figures = facts.Sum(day, day);
            pagesTotal += figures.Pages;
            documentsTotal += figures.Documents;
            var inProject = day >= project.Start;
            if (inProject)
                cumulative += figures.Pages;

            rows.Add(
            [
                ReportCell.Day(day),
                ReportCell.Count(figures.Pages),
                ReportCell.Count(figures.Documents),
                ReportCell.Count(target),
                PercentOf(figures.Pages, target),
                inProject ? ReportCell.Count(cumulative) : ReportCell.Empty,
                inProject ? ReportCell.Count(project.PlannedBy(day)) : ReportCell.Empty,
            ]);
        }

        var targetTotal = target * rows.Count;
        return new Report(
            [
                new(text["Report.Column.Date"], Numeric: false),
                new(text["Report.Column.PagesScanned"]),
                new(text["Report.Column.Documents"]),
                new(text["Report.Column.DailyTarget"]),
                new(text["Report.Column.OfTarget"]),
                new(text["Report.Column.ProjectPages"]),
                new(text["Report.Column.ProjectPlan"]),
            ],
            rows,
            [TotalLabel, ReportCell.Count(pagesTotal), ReportCell.Count(documentsTotal), ReportCell.Count(targetTotal),
                PercentOf(pagesTotal, targetTotal), ReportCell.Empty, ReportCell.Empty]);
    }

    private Report Quality(ProjectFacts facts, DateOnly from, DateOnly to)
    {
        static IReadOnlyList<ReportCell> Row(ReportCell label, DayFacts figures) =>
        [
            label,
            ReportCell.Count(figures.Documents),
            ReportCell.Count(figures.Pages),
            figures.Confidence is { } confidence ? ReportCell.Percent(confidence) : ReportCell.Empty,
            Share(figures.LowPages, figures.ConfidencePages),
            Share(figures.AutoArchived, figures.Documents),
            Share(figures.Flagged, figures.Documents),
            Share(figures.Status(DocumentStatus.Failed), figures.Documents),
        ];

        var rows = facts.ByDepartment(from, to).Select(d => Row(DepartmentLabel(d.Department), d.Facts)).ToList();
        return new Report(
            [
                new(text["Report.Column.Department"], Numeric: false),
                new(text["Report.Column.Documents"]),
                new(text["Report.Column.Pages"]),
                new(text["Report.Column.Confidence"]),
                new(text["Report.Column.LowPages"]),
                new(text["Report.Column.AutoArchived"]),
                new(text["Report.Column.SentToReview"]),
                new(text["Report.Column.Failed"]),
            ],
            rows,
            Row(TotalLabel, facts.Sum(from, to)));
    }

    /// <summary>The people who scanned and reviewed in the period, from the documents themselves: operators first, then reviewers.</summary>
    private Report Productivity(ProjectFacts facts, DateOnly from, DateOnly to)
    {
        var people = facts.ByUser(from, to)
            .OrderBy(p => p.Facts.Uploaded > 0 ? 0 : 1)
            .ThenBy(p => p.User, StringComparer.Ordinal)
            .ToList();

        var rows = new List<IReadOnlyList<ReportCell>>();
        int documents = 0, pages = 0, uploadDays = 0, reviewed = 0, timed = 0;
        double reviewSeconds = 0;
        foreach (var (user, figures) in people)
        {
            documents += figures.Uploaded;
            pages += figures.UploadedPages;
            uploadDays += figures.UploadDays;
            reviewed += figures.Reviewed;
            timed += figures.TimedReviews;
            reviewSeconds += figures.ReviewSeconds;
            var scanned = figures.Uploaded > 0;
            rows.Add(
            [
                ReportCell.Of(user),
                users.Find(user) is { } account ? ReportCell.Of(text.RoleName(account.Role)) : ReportCell.Empty,
                scanned ? ReportCell.Count(figures.Uploaded) : ReportCell.Empty,
                scanned ? ReportCell.Count(figures.UploadedPages) : ReportCell.Empty,
                scanned ? ReportCell.Amount(figures.UploadedPages / (figures.UploadDays * ShiftHours)) : ReportCell.Empty,
                figures.Reviewed > 0 ? ReportCell.Count(figures.Reviewed) : ReportCell.Empty,
                figures.TimedReviews > 0 ? ReportCell.Amount(figures.ReviewSeconds / figures.TimedReviews / 60) : ReportCell.Empty,
            ]);
        }

        return new Report(
            [
                new(text["Report.Column.User"], Numeric: false),
                new(text["Report.Column.Role"], Numeric: false),
                new(text["Report.Column.DocumentsScanned"]),
                new(text["Report.Column.PagesScanned"]),
                new(text["Report.Column.PagesPerHour"]),
                new(text["Report.Column.DocumentsReviewed"]),
                new(text["Report.Column.MinutesPerReview"]),
            ],
            rows,
            [TotalLabel, ReportCell.Empty, ReportCell.Count(documents), ReportCell.Count(pages),
                uploadDays == 0 ? ReportCell.Empty : ReportCell.Amount(pages / (uploadDays * ShiftHours)), ReportCell.Count(reviewed),
                timed == 0 ? ReportCell.Empty : ReportCell.Amount(reviewSeconds / timed / 60)]);
    }

    /// <summary>Documents that needed a person, by the first issue the automatic check found (or a failed OCR job).</summary>
    private Report Exceptions(ProjectFacts facts, DateOnly from, DateOnly to)
    {
        var figures = facts.Sum(from, to);
        var rows = new List<IReadOnlyList<ReportCell>>();
        int raised = 0, resolved = 0, open = 0;
        double hours = 0;
        foreach (var reason in Enum.GetValues<ExceptionReason>())
        {
            raised += figures.Raised(reason);
            resolved += figures.Resolved(reason);
            open += figures.Open(reason);
            hours += figures.ResolveHours(reason);
            rows.Add(
            [
                ReportCell.Of(text[$"Report.Reason.{reason}"]),
                ReportCell.Count(figures.Raised(reason)),
                ReportCell.Count(figures.Resolved(reason)),
                ReportCell.Count(figures.Open(reason)),
                figures.Resolved(reason) == 0 ? ReportCell.Empty : ReportCell.Amount(figures.ResolveHours(reason) / figures.Resolved(reason)),
            ]);
        }

        return new Report(
            [
                new(text["Report.Column.Reason"], Numeric: false),
                new(text["Report.Column.Flagged"]),
                new(text["Report.Column.Resolved"]),
                new(text["Report.Column.Open"]),
                new(text["Report.Column.HoursToResolve"]),
            ],
            rows,
            [TotalLabel, ReportCell.Count(raised), ReportCell.Count(resolved), ReportCell.Count(open),
                resolved == 0 ? ReportCell.Empty : ReportCell.Amount(hours / resolved)]);
    }

    private Report Archive(ProjectFacts facts, DateOnly from, DateOnly to)
    {
        static IReadOnlyList<ReportCell> Row(ReportCell label, DayFacts figures) =>
        [
            label,
            ReportCell.Count(figures.Status(DocumentStatus.Archived)),
            ReportCell.Count(figures.ArchivedPages),
            ReportCell.Count(figures.Archived(DocumentConfidentiality.Internal)),
            ReportCell.Count(figures.Archived(DocumentConfidentiality.Confidential)),
            ReportCell.Count(figures.Archived(DocumentConfidentiality.Secret)),
            ReportCell.Amount(figures.ArchivedPages * MegabytesPerPage / 1024),
        ];

        var rows = facts.ByDepartment(from, to).Select(d => Row(DepartmentLabel(d.Department), d.Facts)).ToList();
        return new Report(
            [
                new(text["Report.Column.Department"], Numeric: false),
                new(text["Report.Column.DocumentsArchived"]),
                new(text["Report.Column.Pages"]),
                new(text.Confidentiality(DocumentConfidentiality.Internal)),
                new(text.Confidentiality(DocumentConfidentiality.Confidential)),
                new(text.Confidentiality(DocumentConfidentiality.Secret)),
                new(text["Report.Column.Storage"]),
            ],
            rows,
            Row(TotalLabel, facts.Sum(from, to)));
    }

    private ReportTable Activity(DateOnly from, DateOnly to)
    {
        AuditAction[][] columns =
        [
            [AuditAction.SignedIn],
            [AuditAction.Uploaded],
            [AuditAction.Edited, AuditAction.EditedInWord, AuditAction.Restored],
            [AuditAction.StatusChanged],
            [AuditAction.Deleted],
            [AuditAction.Downloaded, AuditAction.Exported, AuditAction.ReportExported],
            [AuditAction.QualitySettingsChanged, AuditAction.KpiSettingsChanged, AuditAction.UserAdded, AuditAction.UserChanged,
                AuditAction.RolesChanged],
        ];

        var entries = audit.Latest(AuditLog.Capacity)
            .Where(e => LocalDay(e.AtUtc) is var day && day >= from && day <= to)
            .ToList();
        IReadOnlyList<ReportCell> Row(ReportCell label, IReadOnlyCollection<AuditEntry> actions) =>
        [
            label,
            .. columns.Select(kinds => ReportCell.Count(actions.Count(e => kinds.Contains(e.Action)))),
            ReportCell.Count(actions.Count),
        ];

        var rows = entries
            .GroupBy(e => e.User, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(user => Row(ReportCell.Of(user.Key), user.ToList()))
            .ToList();

        return new ReportTable(ReportKind.Activity, from, to, IsSample: false,
            [
                new(text["Report.Column.User"], Numeric: false),
                new(text["Report.Column.SignIns"]),
                new(text["Report.Column.Uploads"]),
                new(text["Report.Column.Edits"]),
                new(text["Report.Column.StatusChanges"]),
                new(text["Report.Column.Deletions"]),
                new(text["Report.Column.Downloads"]),
                new(text["Report.Column.SettingsChanges"]),
                new(text["Report.Column.AllActions"]),
            ],
            rows,
            rows.Count == 0 ? null : Row(TotalLabel, entries));
    }

    private ReportCell TotalLabel => ReportCell.Of(text["Report.Total"]);

    private ReportCell DepartmentLabel(DocumentDepartment? department) =>
        ReportCell.Of(department is { } value ? text.Department(value) : text["Dept.Unassigned"]);

    private static ReportCell PercentOf(double value, double target) =>
        target > 0 ? ReportCell.Percent(value * 100 / target) : ReportCell.Empty;

    private static ReportCell Share(int part, int whole) => whole > 0 ? ReportCell.Percent(part * 100d / whole) : ReportCell.Empty;

    private sealed record Report(IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<ReportCell>> Rows, IReadOnlyList<ReportCell> Total);

    private DateOnly LocalDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), time.LocalTimeZone));

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> fields) =>
        csv.Append(string.Join(',', fields.Select(ApprovedDocumentFiles.CsvField))).Append("\r\n");
}
