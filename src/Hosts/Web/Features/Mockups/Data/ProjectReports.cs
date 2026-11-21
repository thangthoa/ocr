using OppOcr.Web.Features.Mockups.History;
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

/// <param name="Department">Null for documents whose department is not set yet.</param>
public sealed record ReportDepartmentRow(DocumentDepartment? Department, int Documents, int Pages, double? Confidence);

/// <param name="PagesChangePercent">Against the period before; null when that period starts before the project.</param>
/// <param name="Confidence">Null when no page of the period has a confidence.</param>
/// <param name="WaitingReview">Documents in the review queue now (the same for every period).</param>
/// <param name="AutoArchivePercent">Documents archived by the automatic check, among those it checked.</param>
/// <param name="ReviewHours">Average time from the automatic check to a person's approval.</param>
/// <param name="Operators">People who uploaded documents in the period.</param>
public sealed record ReportSnapshot(
    ReportPeriod Period,
    DateOnly From,
    DateOnly To,
    int Pages,
    double? PagesChangePercent,
    int Documents,
    double? DocumentsChangePercent,
    double? Confidence,
    double? ConfidenceChangePoints,
    int WaitingReview,
    int Failed,
    double? AutoArchivePercent,
    double? ReviewHours,
    int Operators,
    IReadOnlyList<ReportBar> Bars,
    IReadOnlyList<ReportStatusCount> Statuses,
    IReadOnlyList<ReportDepartmentRow> Departments)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public double PagesPerDay => (double)Pages / Days;

    public double? FailedPercent => Documents == 0 ? null : Failed * 100d / Documents;

    public double? PagesPerOperator => Operators == 0 ? null : PagesPerDay / Operators;

    /// <summary>The figure a KPI is measured on, for this period; null when the period has nothing to measure it on.</summary>
    public double? Actual(KpiKind kind) => kind switch
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
/// The dashboard's figures for a day, a week or 30 days up to today, added up from the documents themselves
/// (<see cref="ProjectFacts"/>): the simulated history, the samples and the files uploaded in this app.
/// </summary>
public static class ProjectReports
{
    /// <summary>The hourly chart always shows the working day; earlier or later uploads widen it.</summary>
    public const int LastHour = HistoryProfile.FirstHour + 15;

    public static ReportSnapshot For(ProjectFacts facts, ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var to = facts.Today;
        var from = period switch
        {
            ReportPeriod.Day => to,
            ReportPeriod.Week => to.AddDays(-6),
            ReportPeriod.Month => to.AddDays(-29),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
        };

        var current = facts.Sum(from, to);
        var days = to.DayNumber - from.DayNumber + 1;
        var previousFrom = from.AddDays(-days);
        var previous = previousFrom >= facts.FirstDay ? facts.Sum(previousFrom, from.AddDays(-1)) : null;

        var statuses = new List<ReportStatusCount>
        {
            new(DocumentStatus.Processing, current.Status(DocumentStatus.Processing)),
            new(DocumentStatus.NeedsReview, current.Status(DocumentStatus.NeedsReview)),
            new(DocumentStatus.Archived, current.Status(DocumentStatus.Archived)),
            new(DocumentStatus.Failed, current.Status(DocumentStatus.Failed)),
        };
        if (current.Status(DocumentStatus.Cancelled) > 0)
            statuses.Add(new(DocumentStatus.Cancelled, current.Status(DocumentStatus.Cancelled)));

        return new ReportSnapshot(period, from, to,
            current.Pages, Change(current.Pages, previous?.Pages),
            current.Documents, Change(current.Documents, previous?.Documents),
            current.Confidence, current.Confidence - previous?.Confidence,
            facts.WaitingReview,
            current.Status(DocumentStatus.Failed),
            current.Checked == 0 ? null : current.AutoArchived * 100d / current.Checked,
            current.Reviewed == 0 ? null : current.ReviewHours / current.Reviewed,
            facts.Operators(from, to),
            period == ReportPeriod.Day ? Hours(facts, to) : Dates(facts, from, to),
            statuses,
            facts.ByDepartment(from, to).Select(d => new ReportDepartmentRow(d.Department, d.Facts.Documents, d.Facts.Pages, d.Facts.Confidence)).ToList());
    }

    /// <summary>Change in percent; null without a previous figure to compare with.</summary>
    private static double? Change(int value, int? previous) =>
        previous is > 0 ? (value - previous.Value) * 100d / previous.Value : null;

    private static List<ReportBar> Hours(ProjectFacts facts, DateOnly day)
    {
        var hours = facts.HoursWithPages(day).ToList();
        var first = Math.Min(HistoryProfile.FirstHour, hours.Count == 0 ? HistoryProfile.FirstHour : hours.Min());
        var last = Math.Max(LastHour, hours.Count == 0 ? LastHour : hours.Max());
        var start = day.ToDateTime(TimeOnly.MinValue);
        return Enumerable.Range(first, last - first + 1).Select(hour => new ReportBar(start.AddHours(hour), facts.HourPages(day, hour))).ToList();
    }

    private static List<ReportBar> Dates(ProjectFacts facts, DateOnly from, DateOnly to)
    {
        var bars = new List<ReportBar>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
            bars.Add(new ReportBar(day.ToDateTime(TimeOnly.MinValue), facts.Sum(day, day).Pages));
        return bars;
    }
}
