using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>Report mockup: fixed sample figures per day, week and month (see <see cref="SampleReports"/>).</summary>
public sealed partial class DashboardPage
{
    private static readonly double[] NiceSteps = [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

    private ReportPeriod _period = ReportPeriod.Day;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    private DateOnly Today => DateOnly.FromDateTime(Time.GetLocalNow().DateTime);

    private ReportSnapshot Report => SampleReports.For(_period, Today);

    private double PagesPercent => Report.Pages * 100d / Report.PagesTarget;

    /// <summary>Top of the chart's scale: a round number just above the highest bar and the target.</summary>
    private int ScaleMax
    {
        get
        {
            var top = Math.Max(Report.Bars.Max(b => b.Pages), Report.BarTarget) * 1.08;
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
        MockupTextExtensions.FormatNumber(Report.BarTarget)];

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

    /// <summary>CSS lengths must use the invariant culture whatever the UI language is.</summary>
    private static string CssPercent(double value) =>
        Math.Clamp(value, 0, 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
