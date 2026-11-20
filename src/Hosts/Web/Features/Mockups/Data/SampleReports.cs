using OppOcr.Web.Features.Mockups.Kpi;

namespace OppOcr.Web.Features.Mockups.Data;

public enum ReportPeriod
{
    Day,
    Week,
    Month,
}

/// <summary>One bar of the throughput chart: an hour (day report) or a date (week and month).</summary>
public sealed record ReportBar(DateTime Start, int Pages);

public sealed record ReportStatusCount(DocumentStatus Status, int Documents);

public sealed record ReportDepartmentRow(DocumentDepartment Department, int Documents, int Pages, double Confidence);

/// <param name="WaitingReview">Documents in the review queue now (the same for every period).</param>
/// <param name="AutoArchivePercent">Documents archived by the automatic check, without a reviewer.</param>
/// <param name="ReviewHours">Average time a document waited in the review queue.</param>
public sealed record ReportSnapshot(
    ReportPeriod Period,
    DateOnly From,
    DateOnly To,
    int Pages,
    double PagesChangePercent,
    int Documents,
    double DocumentsChangePercent,
    double Confidence,
    double ConfidenceChangePoints,
    int WaitingReview,
    int Failed,
    double AutoArchivePercent,
    double ReviewHours,
    IReadOnlyList<ReportBar> Bars,
    IReadOnlyList<ReportStatusCount> Statuses,
    IReadOnlyList<ReportDepartmentRow> Departments)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public double PagesPerDay => (double)Pages / Days;

    public double FailedPercent => Documents == 0 ? 0 : Failed * 100d / Documents;

    public double PagesPerOperator => PagesPerDay / SampleReports.CaptureOperators;

    /// <summary>The figure a KPI is measured on, for this period.</summary>
    public double Actual(KpiKind kind) => kind switch
    {
        KpiKind.PagesPerDay => PagesPerDay,
        KpiKind.OcrAccuracy => Confidence,
        KpiKind.AutoArchiveRate => AutoArchivePercent,
        KpiKind.ReviewBacklog => WaitingReview,
        KpiKind.ReviewTime => ReviewHours,
        KpiKind.FailureRate => FailedPercent,
        KpiKind.PagesPerOperator => PagesPerOperator,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

/// <summary>
/// Fixed figures for the dashboard mockup. The numbers are illustrative and consistent with each
/// other (bars add up to the totals); only the dates follow the current day.
/// </summary>
public static class SampleReports
{
    public const int FirstHour = 7;

    /// <summary>Capture operators on the digitization line (the sample roles list the same number).</summary>
    public const int CaptureOperators = 12;

    private static readonly int[] HourlyPages =
        [1280, 1610, 1720, 1690, 1540, 1650, 1700, 1580, 1490, 1620, 1680, 1710, 1550, 1600, 1660, 1430];

    // The last seven days are the week report; the last day matches the day report's total.
    private static readonly int[] DailyPages =
    [
        23900, 24600, 25200, 24100, 25800, 26200, 24400, 25100, 25900, 26500, 23800, 24900, 25600, 26100, 25300,
        24700, 25500, 26300, 24200, 25000, 25800, 24600, 25400, 24800, 25900, 26300, 24200, 26800, 27100, 25510,
    ];

    public static ReportSnapshot For(ReportPeriod period, DateOnly today) => period switch
    {
        ReportPeriod.Day => Day(today),
        ReportPeriod.Week => Week(today),
        ReportPeriod.Month => Month(today),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };

    private static ReportSnapshot Day(DateOnly today)
    {
        var start = today.ToDateTime(new TimeOnly(FirstHour, 0));
        var bars = HourlyPages.Select((pages, i) => new ReportBar(start.AddHours(i), pages)).ToList();
        return new ReportSnapshot(ReportPeriod.Day, today, today, bars.Sum(b => b.Pages), 3.1,
            2140, 2.4, 94.6, 0.3, 312, 4, 82.4, 18.5, bars,
            Statuses(180, 196, 1760, 4),
            [
                new(DocumentDepartment.Finance, 820, 9840, 95.1),
                new(DocumentDepartment.HumanResources, 410, 4920, 93.8),
                new(DocumentDepartment.Legal, 230, 4180, 92.7),
                new(DocumentDepartment.Procurement, 450, 4410, 94.9),
                new(DocumentDepartment.Administration, 230, 2160, 96.2),
            ]);
    }

    private static ReportSnapshot Week(DateOnly today)
    {
        var bars = Days(today, 7);
        return new ReportSnapshot(ReportPeriod.Week, today.AddDays(-6), today, bars.Sum(b => b.Pages), 4.2,
            15020, 3.6, 94.2, 0.4, 312, 27, 78.9, 22.0, bars,
            Statuses(180, 1240, 13573, 27),
            [
                new(DocumentDepartment.Finance, 5760, 69700, 94.8),
                new(DocumentDepartment.HumanResources, 2880, 34800, 93.5),
                new(DocumentDepartment.Legal, 1610, 29600, 92.4),
                new(DocumentDepartment.Procurement, 3160, 31200, 94.6),
                new(DocumentDepartment.Administration, 1610, 15310, 96.0),
            ]);
    }

    private static ReportSnapshot Month(DateOnly today)
    {
        var bars = Days(today, DailyPages.Length);
        return new ReportSnapshot(ReportPeriod.Month, today.AddDays(1 - DailyPages.Length), today, bars.Sum(b => b.Pages),
            11.8, 63400, 10.9, 93.9, 0.6, 312, 118, 68.4, 27.5, bars,
            Statuses(180, 2950, 60152, 118),
            [
                new(DocumentDepartment.Finance, 24300, 293000, 94.5),
                new(DocumentDepartment.HumanResources, 12170, 146500, 93.2),
                new(DocumentDepartment.Legal, 6800, 124400, 92.1),
                new(DocumentDepartment.Procurement, 13330, 131200, 94.3),
                new(DocumentDepartment.Administration, 6800, 64410, 95.8),
            ]);
    }

    /// <summary>
    /// Pages scanned on <paramref name="day"/>: the dashboard's figures for the last 30 days, a stable figure in
    /// the same range before that, nothing after <paramref name="today"/>. Reports use it so they agree with the
    /// dashboard.
    /// </summary>
    public static int PagesOn(DateOnly day, DateOnly today)
    {
        var daysAgo = today.DayNumber - day.DayNumber;
        if (daysAgo < 0)
            return 0;
        return daysAgo < DailyPages.Length
            ? DailyPages[^(daysAgo + 1)]
            : 23_000 + (int)((uint)day.DayNumber * 7919u % 3_600u);
    }

    private static List<ReportBar> Days(DateOnly today, int count) =>
        DailyPages[^count..]
            .Select((pages, i) => new ReportBar(today.AddDays(i - count + 1).ToDateTime(TimeOnly.MinValue), pages))
            .ToList();

    private static List<ReportStatusCount> Statuses(int processing, int needsReview, int archived, int failed) =>
    [
        new(DocumentStatus.Processing, processing),
        new(DocumentStatus.NeedsReview, needsReview),
        new(DocumentStatus.Archived, archived),
        new(DocumentStatus.Failed, failed),
    ];
}
