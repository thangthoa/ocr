using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Reports;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Reports mockup: pick a report and a period, view it as a table, export it as CSV or print it. The report and
/// the period can come from the query string (<c>?report=Quality&amp;from=2026-11-01&amp;to=2026-11-20</c>), so other
/// pages can link to a report.
/// </summary>
public sealed partial class ReportsPage
{
    private ReportKind _kind = ReportKind.Progress;
    private DateOnly? _from;
    private DateOnly? _to;
    private ReportRangeError? _error;
    private ReportTable? _table;
    private DateTime _generatedUtc;

    private enum RangePreset
    {
        Today,
        Last7Days,
        Last30Days,
        ThisMonth,
    }

    [SupplyParameterFromQuery(Name = "report")]
    public string? QueryReport { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? QueryFrom { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    public string? QueryTo { get; set; }

    [Inject] private ReportBuilder Reports { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    protected override void OnParametersSet()
    {
        var today = Reports.Today;
        _kind = ReportEndpoints.ParseKind(QueryReport) ?? ReportKind.Progress;
        _from = ReportEndpoints.ParseDate(QueryFrom) ?? today.AddDays(-6);
        _to = ReportEndpoints.ParseDate(QueryTo) ?? today;
        View();
    }

    private void Select(ReportKind kind)
    {
        _kind = kind;
        View();
    }

    private void ApplyPreset(RangePreset preset)
    {
        var today = Reports.Today;
        (_from, _to) = preset switch
        {
            RangePreset.Today => (today, today),
            RangePreset.Last7Days => (today.AddDays(-6), today),
            RangePreset.Last30Days => (today.AddDays(-29), today),
            _ => (new DateOnly(today.Year, today.Month, 1), today),
        };
        View();
    }

    /// <summary>Builds the chosen report for the period, or says why the period cannot be used.</summary>
    private void View()
    {
        _error = ReportRange.Check(_from, _to, Reports.Today);
        if (_error is not null)
        {
            _table = null;
            return;
        }

        _table = Reports.Build(_kind, _from!.Value, _to!.Value);
        _generatedUtc = Time.GetUtcNow().UtcDateTime;
    }

    private string Period(ReportTable table) => table.From == table.To
        ? MockupTextExtensions.FormatDate(table.From)
        : Text["Dashboard.Range", MockupTextExtensions.FormatDate(table.From), MockupTextExtensions.FormatDate(table.To)];
}
