using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Kpi;

namespace OppOcr.Web.Features.Mockups.Reports;

/// <summary>
/// Builds the reports of the Reports page and their CSV files. Every report but the activity report adds up the
/// sample figures of <see cref="SampleReportFacts"/>; the activity report counts the entries of the activity log.
/// Headers and labels are in the UI language of the request.
/// </summary>
public sealed class ReportBuilder(IStringLocalizer<MockupText> text, KpiSettingsStore kpis, AuditLog audit, TimeProvider time)
{
    /// <summary>A working day of a capture operator, for the pages per hour.</summary>
    private const double ShiftHours = 8;

    /// <summary>Image, searchable PDF and DOCX of one page, in MB.</summary>
    private const double MegabytesPerPage = 0.42;

    private static readonly double[] OperatorShares = [1.12, 1.05, 1.0, 0.97, 1.08, 0.92, 1.03, 0.95, 1.1, 0.89, 0.98, 0.91];

    private static readonly double[] ReviewerShares = [1.1, 0.95, 1.2, 0.85, 1.0, 0.9];

    private static readonly double[] ReviewerMinutes = [6.5, 7.2, 5.9, 8.1, 6.8, 7.5];

    private static readonly (ExceptionReason Reason, double Share, double Hours)[] Reasons =
    [
        (ExceptionReason.LowPageConfidence, 0.36, 20),
        (ExceptionReason.LowDocumentConfidence, 0.2, 16),
        (ExceptionReason.PageWarnings, 0.2, 12),
        (ExceptionReason.EmptyPage, 0.1, 6),
        (ExceptionReason.MissingMetadata, 0.14, 9),
    ];

    /// <summary>Share of internal and confidential documents per department; the rest is secret.</summary>
    private static readonly Dictionary<DocumentDepartment, (double Internal, double Confidential)> Confidentiality = new()
    {
        [DocumentDepartment.Finance] = (0.7, 0.25),
        [DocumentDepartment.HumanResources] = (0.4, 0.5),
        [DocumentDepartment.Legal] = (0.3, 0.5),
        [DocumentDepartment.Procurement] = (0.75, 0.22),
        [DocumentDepartment.Administration] = (0.85, 0.13),
    };

    private enum ExceptionReason
    {
        LowPageConfidence,
        LowDocumentConfidence,
        PageWarnings,
        EmptyPage,
        MissingMetadata,
        OcrFailed,
    }

    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public ReportTable Build(ReportKind kind, DateOnly from, DateOnly to)
    {
        if (ReportRange.Check(from, to, Today) is { } error)
            throw new ArgumentOutOfRangeException(nameof(to), error, "The report period is not valid.");

        return kind switch
        {
            ReportKind.Progress => Progress(from, to),
            ReportKind.Quality => Quality(from, to),
            ReportKind.Productivity => Productivity(from, to),
            ReportKind.Exceptions => Exceptions(from, to),
            ReportKind.Archive => Archive(from, to),
            ReportKind.Activity => Activity(from, to),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>The report as CSV (UTF-8 with a BOM so Excel reads Arabic); the export is written to the activity log.</summary>
    internal DownloadFile Export(ReportKind kind, DateOnly from, DateOnly to, string user)
    {
        var table = Build(kind, from, to);
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

    private ReportTable Progress(DateOnly from, DateOnly to)
    {
        var today = Today;
        var settings = kpis.Current;
        var target = settings.For(KpiKind.PagesPerDay).Target;
        var project = settings.Project;

        var cumulative = 0;
        for (var day = project.Start; day < from; day = day.AddDays(1))
            cumulative += SampleReports.PagesOn(day, today);

        var rows = new List<IReadOnlyList<ReportCell>>();
        int pagesTotal = 0, documentsTotal = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var facts = SampleReportFacts.For(day, today);
            var pages = facts.Sum(f => f.Pages);
            var documents = facts.Sum(f => f.Documents);
            pagesTotal += pages;
            documentsTotal += documents;
            var inProject = day >= project.Start;
            if (inProject)
                cumulative += pages;

            rows.Add(
            [
                ReportCell.Day(day),
                ReportCell.Count(pages),
                ReportCell.Count(documents),
                ReportCell.Count(target),
                PercentOf(pages, target),
                inProject ? ReportCell.Count(cumulative) : ReportCell.Empty,
                inProject ? ReportCell.Count(project.PlannedBy(day)) : ReportCell.Empty,
            ]);
        }

        var targetTotal = target * rows.Count;
        return Table(ReportKind.Progress, from, to,
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

    private ReportTable Quality(DateOnly from, DateOnly to)
    {
        var facts = SampleReportFacts.For(from, to, Today);
        IReadOnlyList<ReportCell> Row(ReportCell label, IReadOnlyCollection<DepartmentDay> days)
        {
            var auto = Weighted(days, d => d.AutoArchivePercent, d => d.Documents);
            var failed = Weighted(days, d => d.FailedPercent, d => d.Documents);
            return
            [
                label,
                ReportCell.Count(days.Sum(d => d.Documents)),
                ReportCell.Count(days.Sum(d => d.Pages)),
                ReportCell.Percent(Weighted(days, d => d.Confidence, d => d.Pages)),
                ReportCell.Percent(Weighted(days, d => d.LowPagesPercent, d => d.Pages)),
                ReportCell.Percent(auto),
                ReportCell.Percent(Math.Max(0, 100 - auto - failed)),
                ReportCell.Percent(failed),
            ];
        }

        var rows = facts.GroupBy(f => f.Department)
            .Select(group => Row(ReportCell.Of(text.Department(group.Key)), group.ToList()))
            .ToList();

        return Table(ReportKind.Quality, from, to,
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
            Row(TotalLabel, facts));
    }

    private ReportTable Productivity(DateOnly from, DateOnly to)
    {
        var facts = SampleReportFacts.For(from, to, Today);
        var pages = facts.Sum(f => f.Pages);
        var documents = facts.Sum(f => f.Documents);
        var reviewed = facts.Sum(f => f.Documents * Math.Max(0, 100 - f.AutoArchivePercent - f.FailedPercent) / 100);
        var hours = (to.DayNumber - from.DayNumber + 1) * ShiftHours;
        var operatorWeight = OperatorShares.Sum();
        var reviewerWeight = ReviewerShares.Sum();

        var rows = new List<IReadOnlyList<ReportCell>>();
        int scannedDocuments = 0, scannedPages = 0, reviewedDocuments = 0;
        double reviewMinutes = 0;
        // The last operator and the last reviewer take what rounding left over, so the totals match the other reports.
        for (var i = 0; i < OperatorShares.Length; i++)
        {
            var last = i == OperatorShares.Length - 1;
            var operatorPages = last ? pages - scannedPages : (int)Math.Round(pages * OperatorShares[i] / operatorWeight);
            var operatorDocuments = last ? documents - scannedDocuments : (int)Math.Round(documents * OperatorShares[i] / operatorWeight);
            scannedPages += operatorPages;
            scannedDocuments += operatorDocuments;
            rows.Add(
            [
                ReportCell.Of(string.Create(CultureInfo.InvariantCulture, $"capture{i + 1:00}")),
                ReportCell.Of(text["Role.CaptureOperator"]),
                ReportCell.Count(operatorDocuments),
                ReportCell.Count(operatorPages),
                ReportCell.Amount(operatorPages / hours),
                ReportCell.Empty,
                ReportCell.Empty,
            ]);
        }

        for (var i = 0; i < ReviewerShares.Length; i++)
        {
            var reviewerDocuments = i == ReviewerShares.Length - 1
                ? (int)Math.Round(reviewed) - reviewedDocuments
                : (int)Math.Round(reviewed * ReviewerShares[i] / reviewerWeight);
            var minutes = ReviewerMinutes[i] + SampleReportFacts.Wobble(to, i) * 0.5;
            reviewedDocuments += reviewerDocuments;
            reviewMinutes += reviewerDocuments * minutes;
            rows.Add(
            [
                ReportCell.Of(string.Create(CultureInfo.InvariantCulture, $"review{i + 1:00}")),
                ReportCell.Of(text["Role.Reviewer"]),
                ReportCell.Empty,
                ReportCell.Empty,
                ReportCell.Empty,
                ReportCell.Count(reviewerDocuments),
                ReportCell.Amount(minutes),
            ]);
        }

        return Table(ReportKind.Productivity, from, to,
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
            [TotalLabel, ReportCell.Empty, ReportCell.Count(scannedDocuments), ReportCell.Count(scannedPages),
                ReportCell.Amount(scannedPages / (hours * OperatorShares.Length)), ReportCell.Count(reviewedDocuments),
                reviewedDocuments == 0 ? ReportCell.Empty : ReportCell.Amount(reviewMinutes / reviewedDocuments)]);
    }

    private ReportTable Exceptions(DateOnly from, DateOnly to)
    {
        var today = Today;
        var flagged = new double[Reasons.Length + 1];
        var open = new double[Reasons.Length + 1];
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var facts = SampleReportFacts.For(day, today);
            var sentToReview = facts.Sum(f => f.Documents * Math.Max(0, 100 - f.AutoArchivePercent - f.FailedPercent) / 100);
            var failed = facts.Sum(f => f.Documents * f.FailedPercent / 100);
            // Most documents are dealt with within two days; what is still open comes from the last days.
            var stillOpen = (today.DayNumber - day.DayNumber) switch { 0 => 0.5, 1 => 0.25, 2 => 0.06, _ => 0 };
            for (var i = 0; i < Reasons.Length; i++)
            {
                flagged[i] += sentToReview * Reasons[i].Share;
                open[i] += sentToReview * Reasons[i].Share * stillOpen;
            }

            flagged[^1] += failed;
            open[^1] += failed * stillOpen;
        }

        var rows = new List<IReadOnlyList<ReportCell>>();
        int flaggedTotal = 0, openTotal = 0;
        double hoursTotal = 0;
        for (var i = 0; i <= Reasons.Length; i++)
        {
            var reason = i < Reasons.Length ? Reasons[i].Reason : ExceptionReason.OcrFailed;
            var baseHours = i < Reasons.Length ? Reasons[i].Hours : 30;
            var count = (int)Math.Round(flagged[i]);
            var stillOpen = Math.Min(count, (int)Math.Round(open[i]));
            var hours = baseHours + SampleReportFacts.Wobble(to, i) * 2;
            flaggedTotal += count;
            openTotal += stillOpen;
            hoursTotal += hours * (count - stillOpen);
            rows.Add(
            [
                ReportCell.Of(text[$"Report.Reason.{reason}"]),
                ReportCell.Count(count),
                ReportCell.Count(count - stillOpen),
                ReportCell.Count(stillOpen),
                count == stillOpen ? ReportCell.Empty : ReportCell.Amount(hours),
            ]);
        }

        var resolved = flaggedTotal - openTotal;
        return Table(ReportKind.Exceptions, from, to,
            [
                new(text["Report.Column.Reason"], Numeric: false),
                new(text["Report.Column.Flagged"]),
                new(text["Report.Column.Resolved"]),
                new(text["Report.Column.Open"]),
                new(text["Report.Column.HoursToResolve"]),
            ],
            rows,
            [TotalLabel, ReportCell.Count(flaggedTotal), ReportCell.Count(resolved), ReportCell.Count(openTotal),
                resolved == 0 ? ReportCell.Empty : ReportCell.Amount(hoursTotal / resolved)]);
    }

    private ReportTable Archive(DateOnly from, DateOnly to)
    {
        var facts = SampleReportFacts.For(from, to, Today);
        var rows = new List<IReadOnlyList<ReportCell>>();
        var totals = new double[6];
        foreach (var group in facts.GroupBy(f => f.Department))
        {
            // Archived: what did not fail and is no longer in the review queue.
            var documents = (int)Math.Round(group.Sum(f => f.Documents) * 0.96);
            var pages = (int)Math.Round(group.Sum(f => f.Pages) * 0.96);
            var (internalShare, confidentialShare) = Confidentiality[group.Key];
            var internalDocuments = (int)Math.Round(documents * internalShare);
            var confidential = (int)Math.Round(documents * confidentialShare);
            var secret = documents - internalDocuments - confidential;
            var gigabytes = pages * MegabytesPerPage / 1024;
            double[] values = [documents, pages, internalDocuments, confidential, secret, gigabytes];
            for (var i = 0; i < values.Length; i++)
                totals[i] += values[i];

            rows.Add(
            [
                ReportCell.Of(text.Department(group.Key)),
                ReportCell.Count(documents),
                ReportCell.Count(pages),
                ReportCell.Count(internalDocuments),
                ReportCell.Count(confidential),
                ReportCell.Count(secret),
                ReportCell.Amount(gigabytes),
            ]);
        }

        return Table(ReportKind.Archive, from, to,
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
            [TotalLabel, .. totals[..5].Select(ReportCell.Count), ReportCell.Amount(totals[5])]);
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

    private static ReportTable Table(ReportKind kind, DateOnly from, DateOnly to, IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<IReadOnlyList<ReportCell>> rows, IReadOnlyList<ReportCell> total) =>
        new(kind, from, to, IsSample: true, columns, rows, total);

    private static ReportCell PercentOf(double value, double target) =>
        target > 0 ? ReportCell.Percent(value * 100 / target) : ReportCell.Empty;

    private static double Weighted(IEnumerable<DepartmentDay> days, Func<DepartmentDay, double> value, Func<DepartmentDay, int> weight)
    {
        double sum = 0, weights = 0;
        foreach (var day in days)
        {
            sum += value(day) * weight(day);
            weights += weight(day);
        }

        return weights == 0 ? 0 : sum / weights;
    }

    private DateOnly LocalDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), time.LocalTimeZone));

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> fields) =>
        csv.Append(string.Join(',', fields.Select(ApprovedDocumentFiles.CsvField))).Append("\r\n");
}
