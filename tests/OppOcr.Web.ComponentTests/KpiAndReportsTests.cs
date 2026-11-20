using System.Globalization;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Kpi;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Reports;

namespace OppOcr.Web.ComponentTests;

public sealed class KpiAndReportsTests : BunitContext
{
    public KpiAndReportsTests()
    {
        TestServices.SetCulture("en");
        Services.AddOcrAndDocuments();
        Services.AddHealthChecks();
    }

    private static DateOnly Today => DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

    private KpiSettingsStore Kpis => Services.GetRequiredService<KpiSettingsStore>();

    private ReportBuilder Reports => Services.GetRequiredService<ReportBuilder>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Theory]
    [InlineData(KpiKind.OcrAccuracy, 91, KpiStatus.OnTrack)]
    [InlineData(KpiKind.OcrAccuracy, 90, KpiStatus.OnTrack)]
    [InlineData(KpiKind.OcrAccuracy, 88, KpiStatus.AtRisk)]
    [InlineData(KpiKind.OcrAccuracy, 87.9, KpiStatus.OffTrack)]
    [InlineData(KpiKind.ReviewBacklog, 300, KpiStatus.OnTrack)]
    [InlineData(KpiKind.ReviewBacklog, 500, KpiStatus.AtRisk)]
    [InlineData(KpiKind.ReviewBacklog, 501, KpiStatus.OffTrack)]
    public void Kpi_status_compares_the_figure_with_the_target_and_the_alert_level(KpiKind kind, double actual, KpiStatus expected) =>
        Assert.Equal(expected, KpiRules.Evaluate(actual, KpiSettings.Default(Today).For(kind)));

    [Fact]
    public void Alert_level_must_be_beyond_the_target()
    {
        var (target, alert) = KpiRules.Check(KpiKind.OcrAccuracy, 90, 95);
        Assert.Null(target);
        Assert.Equal(KpiTargetError.AlertAboveTarget, alert);

        (target, alert) = KpiRules.Check(KpiKind.ReviewBacklog, 300, 200);
        Assert.Null(target);
        Assert.Equal(KpiTargetError.AlertBelowTarget, alert);

        (target, alert) = KpiRules.Check(KpiKind.FailureRate, 101, null);
        Assert.Equal(KpiTargetError.OutOfRange, target);
        Assert.Equal(KpiTargetError.Missing, alert);
    }

    [Fact]
    public void Project_plan_expects_pages_at_an_even_pace()
    {
        var plan = new ProjectPlan(1_000, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10));

        Assert.Equal(0, plan.PlannedBy(new DateOnly(2026, 10, 31)));
        Assert.Equal(100, plan.PlannedBy(new DateOnly(2026, 11, 1)));
        Assert.Equal(500, plan.PlannedBy(new DateOnly(2026, 11, 5)));
        Assert.Equal(1_000, plan.PlannedBy(new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Dashboard_measures_the_kpis_against_the_targets_set_in_settings()
    {
        var cut = Render<DashboardPage>();

        // A day: 25,510 pages against 20,000; 312 documents waiting against at most 300 (alert at 500).
        Assert.Equal("On track", cut.Find("#kpi-PagesPerDay .kpi-status").TextContent);
        Assert.Equal("At risk", cut.Find("#kpi-ReviewBacklog .kpi-status").TextContent);
        Assert.Equal("6 on track · 1 at risk · 0 off track", cut.Find("#kpi-summary").TextContent);
        Assert.Contains("Target 1,250", cut.Find(".bar-target").TextContent, StringComparison.Ordinal);

        cut.Find("#period-Month").Click();
        Assert.Equal("Off track", cut.Find("#kpi-AutoArchiveRate .kpi-status").TextContent);

        Save(KpiKind.PagesPerDay, 30_000, 26_000);
        var raised = Render<DashboardPage>();
        Assert.Equal("Off track", raised.Find("#kpi-PagesPerDay .kpi-status").TextContent);
        Assert.Equal("Target at least 30,000 · alert below 26,000", raised.Find("#kpi-PagesPerDay .kpi-card-goal").TextContent);
        Assert.Contains("Target 1,875", raised.Find(".bar-target").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_leaves_out_the_kpis_hidden_in_settings()
    {
        Save(KpiKind.ReviewTime, 24, 48, show: false);

        var cut = Render<DashboardPage>();

        Assert.Empty(cut.FindAll("#kpi-ReviewTime"));
        Assert.Equal(Enum.GetValues<KpiKind>().Length - 1, cut.FindAll(".kpi-card").Count);
    }

    [Fact]
    public void Dashboard_shows_the_project_against_its_plan()
    {
        var plan = Kpis.Current.Project;
        var scanned = Enumerable.Range(0, Today.DayNumber - plan.Start.DayNumber + 1)
            .Sum(day => SampleReports.PagesOn(plan.Start.AddDays(day), Today));

        var cut = Render<DashboardPage>();

        Assert.Equal(scanned.ToString("N0", CultureInfo.CurrentCulture), cut.Find("#project-pages").TextContent);
        Assert.Equal("On track", cut.Find("#project-status").TextContent);
        Assert.StartsWith("Ahead of plan by", cut.Find("#project-gap").TextContent, StringComparison.Ordinal);

        // A plan that ends today expects every page by now.
        Kpis.Save(Kpis.Current with { Project = plan with { End = Today } }, "admin");
        var late = Render<DashboardPage>();
        Assert.Equal("Off track", late.Find("#project-status").TextContent);
        Assert.StartsWith("Behind plan by", late.Find("#project-gap").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("End date passed", late.Find("#project-pace").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Kpi_targets_are_saved_from_settings_and_logged()
    {
        var admin = Render<AdminPage>();
        admin.Find("#tab-Kpi").Click();

        admin.Find("#kpi-PagesPerDay-target").Change("22000");
        admin.Find("#kpi-PagesPerDay-alert").Change("19000");
        admin.Find("#kpi-FailureRate-show").Change(false);
        admin.Find("#project-pages").Change("2500000");
        admin.Find("#kpi-form").Submit();

        Assert.NotNull(admin.Find("#kpi-saved"));
        var saved = Kpis.Current;
        Assert.Equal(new KpiTarget(KpiKind.PagesPerDay, 22_000, 19_000), saved.For(KpiKind.PagesPerDay));
        Assert.False(saved.For(KpiKind.FailureRate).ShowOnDashboard);
        Assert.Equal(2_500_000, saved.Project.TotalPages);
        Assert.Equal(AuditAction.KpiSettingsChanged, Audit.Latest()[0].Action);

        Navigation.NavigateTo("users/activity?user=admin");
        var activity = Render<ActivityLogPage>();
        Assert.Contains("Changed the KPI targets", activity.Find(".activity-table tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Kpi_targets_with_mistakes_are_not_saved()
    {
        var start = Kpis.Current.Project.Start;
        var admin = Render<AdminPage>();
        admin.Find("#tab-Kpi").Click();

        admin.Find("#kpi-ReviewBacklog-alert").Change("200");
        admin.Find("#kpi-OcrAccuracy-target").Change("120");
        admin.Find("#project-end").Change(Iso(start.AddDays(-1)));
        admin.Find("#kpi-form").Submit();

        Assert.Equal("The alert level cannot be below the target.", admin.Find("#kpi-ReviewBacklog-alert-error").TextContent);
        Assert.Equal("Enter a number from 0 to 100.0%.", admin.Find("#kpi-OcrAccuracy-target-error").TextContent);
        Assert.Equal("The end date cannot be before the start date.", admin.Find("#project-end-error").TextContent);
        Assert.Empty(admin.FindAll("#kpi-saved"));
        Assert.DoesNotContain(Audit.Latest(), e => e.Action == AuditAction.KpiSettingsChanged);
        Assert.Equal(300, Kpis.Current.For(KpiKind.ReviewBacklog).Target);
    }

    [Fact]
    public void Reports_open_on_the_progress_of_the_last_seven_days()
    {
        var cut = Render<ReportsPage>();

        Assert.Equal("true", cut.Find("#report-Progress").GetAttribute("aria-pressed"));
        Assert.Equal(7, cut.FindAll("#report-table tbody tr").Count);
        // The same pages as the dashboard's week.
        Assert.Equal("180,610", cut.FindAll("#report-total td")[0].TextContent);
        Assert.Equal(ReportEndpoints.ExportUrl(ReportKind.Progress, Today.AddDays(-6), Today), cut.Find("#report-export").GetAttribute("href"));
    }

    [Fact]
    public void Choosing_another_report_or_period_rebuilds_the_table()
    {
        var cut = Render<ReportsPage>();

        cut.Find("#report-Quality").Click();
        Assert.Equal("OCR quality", cut.Find("#report-title").TextContent);
        Assert.Equal(Enum.GetValues<DocumentDepartment>().Length, cut.FindAll("#report-table tbody tr").Count);

        cut.Find("#preset-Today").Click();
        Assert.Equal("25,510", cut.FindAll("#report-total td")[1].TextContent);
    }

    [Fact]
    public void A_report_cannot_end_after_today()
    {
        var cut = Render<ReportsPage>();

        cut.Find("#report-to").Change(Iso(Today.AddDays(1)));
        cut.Find("#report-form").Submit();

        Assert.Equal("A report cannot include days after today.", cut.Find("#report-range-error").TextContent);
        Assert.Empty(cut.FindAll("#report-table"));
    }

    [Fact]
    public void Reports_read_the_report_and_the_period_from_the_query_string()
    {
        var from = Today.AddDays(-2);
        Navigation.NavigateTo($"reports?report=Archive&from={Iso(from)}&to={Iso(Today)}");

        var cut = Render<ReportsPage>();

        Assert.Equal("Archive statistics", cut.Find("#report-title").TextContent);
        Assert.Equal($"{Date(from)} – {Date(Today)}", cut.Find("#report-period").TextContent);
    }

    [Fact]
    public void Department_reports_add_up_to_the_pages_of_the_progress_report()
    {
        // Older than the dashboard's 30 days too.
        var from = Today.AddDays(-40);

        var progress = Reports.Build(ReportKind.Progress, from, Today);
        var quality = Reports.Build(ReportKind.Quality, from, Today);
        var archive = Reports.Build(ReportKind.Archive, from, Today);
        var productivity = Reports.Build(ReportKind.Productivity, from, Today);

        Assert.Equal(41, progress.Rows.Count);
        Assert.Equal(progress.Total![1].Value, quality.Total![2].Value);
        Assert.Equal((progress.Total[2].Value, progress.Total[1].Value), (productivity.Total![2].Value, productivity.Total[3].Value));
        Assert.Equal(quality.Total[2].Value, quality.Rows.Sum(r => r[2].Value));
        Assert.Equal(archive.Total![1].Value, archive.Rows.Sum(r => r[1].Value));
    }

    [Fact]
    public void Activity_report_counts_what_each_user_did()
    {
        Audit.Record("tester-1", AuditAction.SignedIn);
        Audit.Record("tester-1", AuditAction.Downloaded, "DOC-1");
        Audit.Record("tester-1", AuditAction.EditedInWord, "DOC-1");
        Audit.Record("tester-2", AuditAction.StatusChanged, "DOC-1", status: DocumentStatus.Archived);

        var table = Reports.Build(ReportKind.Activity, Today, Today);

        Assert.False(table.IsSample);
        var user = Assert.Single(table.Rows, r => r[0].Text == "tester-1");
        // Sign-ins, uploads, edits, status changes, deletions, downloads, settings changes, all actions.
        double[] expected = [1, 0, 1, 0, 0, 1, 0, 3];
        Assert.Equal(expected, user.Skip(1).Select(c => c.Value).ToArray());
        Assert.Equal(table.Rows.Sum(r => r[^1].Value), table.Total![^1].Value);
    }

    [Fact]
    public void Activity_report_is_empty_when_nobody_did_anything_in_the_period()
    {
        Audit.Record("tester", AuditAction.SignedIn);
        Navigation.NavigateTo($"reports?report=Activity&from={Iso(Today.AddDays(-10))}&to={Iso(Today.AddDays(-5))}");

        var cut = Render<ReportsPage>();

        Assert.Equal("Nothing to report for this period.", cut.Find(".empty-state").TextContent);
    }

    [Fact]
    public void Export_writes_the_report_as_csv_and_logs_it()
    {
        var from = Today.AddDays(-6);

        var file = Reports.Export(ReportKind.Progress, from, Today, "admin");

        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content[..3]);
        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(9, lines.Length);
        Assert.Equal("Date,Pages scanned,Documents,Daily target,% of target,Project to date,Plan to date", lines[0]);
        Assert.StartsWith(Iso(Today) + ",25510,", lines[7], StringComparison.Ordinal);
        Assert.StartsWith("Total,180610,", lines[8], StringComparison.Ordinal);
        Assert.Equal($"report-progress-{from:yyyyMMdd}-{Today:yyyyMMdd}.csv", file.FileName);
        var entry = Audit.Latest()[0];
        Assert.Equal((AuditAction.ReportExported, "Progress"), (entry.Action, entry.Detail));
    }

    [Fact]
    public void Export_link_is_read_back_by_the_endpoint()
    {
        var url = new Uri("http://localhost" + ReportEndpoints.ExportUrl(ReportKind.Exceptions, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20)));
        var query = QueryHelpers.ParseQuery(url.Query);

        Assert.Equal("/reports/export.csv", url.AbsolutePath);
        Assert.Equal(ReportKind.Exceptions, ReportEndpoints.ParseKind(query["report"]));
        Assert.Equal(new DateOnly(2026, 11, 1), ReportEndpoints.ParseDate(query["from"]));
        Assert.Equal(new DateOnly(2026, 11, 20), ReportEndpoints.ParseDate(query["to"]));
    }

    private void Save(KpiKind kind, double target, double alert, bool show = true)
    {
        var current = Kpis.Current;
        Kpis.Save(current with { Targets = [.. current.Targets.Select(t => t.Kind == kind ? new KpiTarget(kind, target, alert, show) : t)] }, "admin");
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
}
