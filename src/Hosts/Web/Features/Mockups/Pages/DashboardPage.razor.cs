using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Kpi;
using static OppOcr.Web.Features.Mockups.MockupTextExtensions;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Report mockup: fixed sample figures per day, week and month (see <see cref="SampleReports"/>), measured against
/// the KPI targets and the project plan set in Settings (<see cref="KpiSettingsStore"/>).
/// </summary>
public sealed partial class DashboardPage
{
    private static readonly double[] NiceSteps = [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

    private ReportPeriod _period = ReportPeriod.Day;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    [Inject] private KpiSettingsStore Kpis { get; set; } = default!;

    private DateOnly Today => DateOnly.FromDateTime(Time.GetLocalNow().DateTime);

    private ReportSnapshot Report => SampleReports.For(_period, Today);

    private KpiSettings Settings => Kpis.Current;

    private ProjectPlan Plan => Settings.Project;

    private List<KpiTarget> VisibleKpis => Settings.Targets.Where(t => t.ShowOnDashboard).ToList();

    private string KpiSummary
    {
        get
        {
            var statuses = VisibleKpis.Select(t => KpiRules.Evaluate(Report.Actual(t.Kind), t)).ToList();
            return Text["KpiBoard.Summary",
                FormatNumber(statuses.Count(s => s == KpiStatus.OnTrack)),
                FormatNumber(statuses.Count(s => s == KpiStatus.AtRisk)),
                FormatNumber(statuses.Count(s => s == KpiStatus.OffTrack))];
        }
    }

    /// <summary>The chart's target line: the daily pages target, spread over the hours of a day report.</summary>
    private int BarTarget
    {
        get
        {
            var daily = Settings.For(KpiKind.PagesPerDay).Target;
            return (int)Math.Round(_period == ReportPeriod.Day ? daily / Report.Bars.Count : daily);
        }
    }

    /// <summary>Pages scanned since the project started, up to today (the period buttons do not change it).</summary>
    private int ProjectPages
    {
        get
        {
            var pages = 0;
            for (var day = Plan.Start; day <= Today; day = day.AddDays(1))
                pages += SampleReports.PagesOn(day, Today);
            return pages;
        }
    }

    private int PlannedPages => Plan.PlannedBy(Today);

    private double ProjectPercent => Math.Min(100, ProjectPages * 100d / Plan.TotalPages);

    private double PlannedPercent => PlannedPages * 100d / Plan.TotalPages;

    private KpiStatus ProjectStatus => KpiRules.EvaluateProject(ProjectPages, PlannedPages);

    private string ProjectSummary => Text["Project.Summary", FormatNumber(ProjectPages), FormatNumber(Plan.TotalPages),
        FormatNumber(PlannedPages)];

    private string ProjectGap
    {
        get
        {
            var gap = ProjectPages - PlannedPages;
            return gap >= 0 ? Text["Project.Ahead", FormatNumber(gap)] : Text["Project.Behind", FormatNumber(-gap)];
        }
    }

    /// <summary>What is left: the pages a day needed to finish on the end date, or that the project is done or late.</summary>
    private string ProjectPace
    {
        get
        {
            var left = Plan.TotalPages - ProjectPages;
            if (left <= 0)
                return Text["Project.Done"];
            var daysLeft = Plan.End.DayNumber - Today.DayNumber;
            return daysLeft <= 0
                ? Text["Project.Late", FormatNumber(left)]
                : Text["Project.Pace", FormatNumber(daysLeft), FormatNumber((int)Math.Ceiling((double)left / daysLeft))];
        }
    }

    /// <summary>A line under some KPIs: the change against the previous period, or what the figure counts.</summary>
    private string? KpiNote(KpiKind kind) => kind switch
    {
        KpiKind.PagesPerDay => Text["Kpi.VsPrevious", FormatSigned(Report.PagesChangePercent, "0.0") + "%"].Value,
        KpiKind.OcrAccuracy => Text["Kpi.PointsChange", FormatSigned(Report.ConfidenceChangePoints, "0.0")].Value,
        KpiKind.ReviewBacklog => Text["Kpi.ReviewNow"].Value,
        KpiKind.PagesPerOperator => Text["Kpi.Operators", FormatNumber(SampleReports.CaptureOperators)].Value,
        _ => null,
    };

    /// <summary>Top of the chart's scale: a round number just above the highest bar and the target.</summary>
    private int ScaleMax
    {
        get
        {
            var top = Math.Max(Report.Bars.Max(b => b.Pages), BarTarget) * 1.08;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(top)));
            var step = NiceSteps.First(s => s * magnitude >= top);
            return (int)(step * magnitude);
        }
    }

    private string RangeLabel => Report.From == Report.To
        ? MockupTextExtensions.FormatDate(Report.From)
        : Text["Dashboard.Range", MockupTextExtensions.FormatDate(Report.From), MockupTextExtensions.FormatDate(Report.To)];

    private string ChartTitle => _period == ReportPeriod.Day ? Text["Chart.PagesByHour"] : Text["Chart.PagesByDay"];

    private string ChartSummary => Text["Chart.Summary", ChartTitle,
        MockupTextExtensions.FormatNumber(Report.Bars.Min(b => b.Pages)),
        MockupTextExtensions.FormatNumber(Report.Bars.Max(b => b.Pages)),
        MockupTextExtensions.FormatNumber(BarTarget)];

    private string BarLabel(ReportBar bar, bool full) => _period switch
    {
        ReportPeriod.Day => bar.Start.ToString("HH:mm", CultureInfo.CurrentCulture),
        ReportPeriod.Week => bar.Start.ToString(full ? "dddd d MMM" : "ddd d", CultureInfo.CurrentCulture),
        _ => bar.Start.ToString(full ? "d MMM yyyy" : "d MMM", CultureInfo.CurrentCulture),
    };

    /// <summary>Thins the axis labels so they do not collide: every other hour, every fifth day of a month.</summary>
    private bool ShowLabel(int index) => _period switch
    {
        ReportPeriod.Day => index % 2 == 0,
        ReportPeriod.Week => true,
        _ => (Report.Bars.Count - 1 - index) % 5 == 0,
    };

    private List<KeyValuePair<DocumentStatus, int>> StatusCounts =>
        Report.Statuses.Select(row => KeyValuePair.Create(row.Status, row.Documents)).ToList();

    /// <summary>A status in the legend opens All Documents filtered to it.</summary>
    private static string StatusUrl(DocumentStatus status) => $"documents?status={status}";

    /// <summary>CSS lengths must use the invariant culture whatever the UI language is.</summary>
    private static string CssPercent(double value) =>
        Math.Clamp(value, 0, 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
