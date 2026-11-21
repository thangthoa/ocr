using System.Globalization;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Kpi;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Reports;

namespace OppOcr.Web.ComponentTests;

/// <summary>The dashboard and the reports over the simulated history (on here, as in the app): they must agree.</summary>
public sealed class KpiAndReportsTests : BunitContext
{
    private readonly InMemoryOcr _ocr;

    public KpiAndReportsTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments(new Dictionary<string, string?> { [TestServices.HistoryEnabled] = "true" });
        Services.AddHealthChecks();
    }

    private static DateOnly Today => DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

    private KpiSettingsStore Kpis => Services.GetRequiredService<KpiSettingsStore>();

    private ReportBuilder Reports => Services.GetRequiredService<ReportBuilder>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private Task<DocumentSnapshot> Snapshot() => Services.GetRequiredService<DocumentCatalog>().SnapshotAsync(CancellationToken.None);

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
        cut.WaitForElement("#kpi-summary");

        // Today counts only the files uploaded in this app (none here): no pages yet, nothing to measure accuracy on.
        Assert.Equal("Off track", KpiStatusOf(cut, KpiKind.PagesPerDay));
        Assert.Equal("No data yet", KpiStatusOf(cut, KpiKind.OcrAccuracy));
        Assert.Equal("—", cut.Find("#kpi-OcrAccuracy .kpi-card-value").TextContent);
        // What the reviewers left at the end of yesterday's day shift: above the 300 target, under the 500 alert level.
        Assert.Equal("At risk", KpiStatusOf(cut, KpiKind.ReviewBacklog));
        Assert.Contains("Target 1,250", cut.Find(".bar-target").TextContent, StringComparison.Ordinal);

        cut.Find("#period-Week").Click();
        Assert.Equal("On track", KpiStatusOf(cut, KpiKind.PagesPerDay));
        Assert.Equal("On track", KpiStatusOf(cut, KpiKind.OcrAccuracy));
        Assert.Equal("On track", KpiStatusOf(cut, KpiKind.AutoArchiveRate));
        Assert.Equal("On track", KpiStatusOf(cut, KpiKind.ReviewTime));

        Save(KpiKind.PagesPerDay, 30_000, 26_000);
        var raised = Render<DashboardPage>();
        raised.WaitForElement("#kpi-summary");
        Assert.Contains("Target 1,875", raised.Find(".bar-target").TextContent, StringComparison.Ordinal);
        raised.Find("#period-Week").Click();
        Assert.Equal("Off track", KpiStatusOf(raised, KpiKind.PagesPerDay));
        Assert.Equal("Target at least 30,000 · alert below 26,000", raised.Find("#kpi-PagesPerDay .kpi-card-goal").TextContent);
    }

    [Fact]
    public async Task Dashboard_figures_are_added_up_from_the_documents()
    {
        var facts = (await Snapshot()).Facts;
        var cut = Render<DashboardPage>();
        cut.WaitForElement("#kpi-summary");

        foreach (var period in Enum.GetValues<ReportPeriod>())
        {
            cut.Find($"#period-{period}").Click();
            var report = ProjectReports.For(facts, period);
            var figures = facts.Sum(report.From, report.To);

            Assert.Equal(report.Pages, report.Bars.Sum(b => b.Pages));
            Assert.Equal(report.Documents, report.Statuses.Sum(s => s.Documents));
            Assert.Equal(report.Documents, report.Departments.Sum(d => d.Documents));
            Assert.Equal((figures.Documents, figures.Pages), (report.Documents, report.Pages));
            Assert.Equal(Number(report.Documents) + " documents", cut.Find("#status-total").TextContent);
            Assert.Equal(Number(report.PagesPerDay), cut.Find("#kpi-PagesPerDay .kpi-card-value").TextContent);
            // The review queue is the same whatever the period.
            Assert.Equal(Number(facts.WaitingReview), cut.Find("#kpi-ReviewBacklog .kpi-card-value").TextContent);
        }

        Assert.Equal(30, cut.FindAll(".bar").Count);
    }

    [Fact]
    public async Task A_file_uploaded_in_this_app_adds_a_document_to_the_project()
    {
        var before = (await Snapshot()).Total;
        Assert.Equal(HistoryProfile.Documents, before);

        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000091", "test-scan.png", Modules.Ocr.Domain.Jobs.OcrJobStatus.Queued));
        var cut = Render<DashboardPage>();
        cut.WaitForElement("#project-documents");

        Assert.Equal(HistoryProfile.Documents + 1, (await Snapshot()).Total);
        // Today has nothing simulated: the new file is today's only document.
        Assert.Equal("1 documents", cut.Find("#status-total").TextContent);
        var plan = Kpis.Current.Project;
        Assert.Equal(Number((await Snapshot()).Facts.Sum(plan.Start, Today).Documents), cut.Find("#project-documents").TextContent);
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
    public void All_documents_list_the_samples_among_the_history_documents()
    {
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        // The samples take the place of as many history documents: the project still has 1,093,911.
        Assert.Equal("1,093,911 document(s)", cut.Find("#doc-count").TextContent);
        cut.Find("#doc-field").Change(nameof(DocumentSearchField.Reference));
        cut.Find("#doc-search").Input("2019/413");
        Assert.Equal("PV-2019-0413.pdf", Assert.Single(cut.FindAll("tbody tr")).QuerySelector("td a")!.TextContent);
    }

    [Fact]
    public async Task Dashboard_shows_the_project_against_its_plan()
    {
        var plan = Kpis.Current.Project;
        var project = (await Snapshot()).Facts.Sum(plan.Start, Today);

        var cut = Render<DashboardPage>();
        cut.WaitForElement("#project-pages");

        Assert.Equal(Number(project.Pages), cut.Find("#project-pages").TextContent);
        Assert.Equal(Number(project.Documents), cut.Find("#project-documents").TextContent);
        Assert.Equal("On track", cut.Find("#project-status").TextContent);
        Assert.StartsWith("Ahead of plan by", cut.Find("#project-gap").TextContent, StringComparison.Ordinal);

        // A plan that ends today expects every page by now.
        Kpis.Save(Kpis.Current with { Project = plan with { End = Today } }, "admin");
        var late = Render<DashboardPage>();
        late.WaitForElement("#project-status");
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
    public async Task Reports_open_on_the_progress_of_the_last_seven_days()
    {
        var week = ProjectReports.For((await Snapshot()).Facts, ReportPeriod.Week);
        var cut = Render<ReportsPage>();
        cut.WaitForElement("#report-table");

        Assert.Equal("true", cut.Find("#report-Progress").GetAttribute("aria-pressed"));
        Assert.Equal(7, cut.FindAll("#report-table tbody tr").Count);
        // The same pages and documents as the dashboard's week.
        Assert.Equal(Number(week.Pages), cut.FindAll("#report-total td")[0].TextContent);
        Assert.Equal(Number(week.Documents), cut.FindAll("#report-total td")[1].TextContent);
        Assert.Equal(ReportEndpoints.ExportUrl(ReportKind.Progress, Today.AddDays(-6), Today), cut.Find("#report-export").GetAttribute("href"));
    }

    [Fact]
    public async Task Choosing_another_report_or_period_rebuilds_the_table()
    {
        var day = ProjectReports.For((await Snapshot()).Facts, ReportPeriod.Day);
        var cut = Render<ReportsPage>();
        cut.WaitForElement("#report-table");

        cut.Find("#report-Quality").Click();
        cut.WaitForAssertion(() => Assert.Equal("OCR quality", cut.Find("#report-title").TextContent));
        Assert.Equal(Enum.GetValues<DocumentDepartment>().Length, cut.FindAll("#report-table tbody tr").Count);

        cut.Find("#preset-Today").Click();
        cut.WaitForAssertion(() => Assert.Equal(Number(day.Pages), cut.FindAll("#report-total td")[1].TextContent));
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
    public async Task Every_report_adds_up_the_same_documents()
    {
        var from = HistoryStart;

        var progress = await Reports.BuildAsync(ReportKind.Progress, from, Today, CancellationToken.None);
        var quality = await Reports.BuildAsync(ReportKind.Quality, from, Today, CancellationToken.None);
        var archive = await Reports.BuildAsync(ReportKind.Archive, from, Today, CancellationToken.None);
        var productivity = await Reports.BuildAsync(ReportKind.Productivity, from, Today, CancellationToken.None);
        var exceptions = await Reports.BuildAsync(ReportKind.Exceptions, from, Today, CancellationToken.None);
        var snapshot = await Snapshot();
        var figures = snapshot.Facts.Sum(from, Today);

        Assert.Equal(HistoryProfile.Days + 1, progress.Rows.Count);
        Assert.True(progress.IsSample);
        Assert.Equal((double)figures.Pages, progress.Total![1].Value);
        Assert.Equal((double)figures.Documents, progress.Total[2].Value);
        Assert.Equal(progress.Total[1].Value, quality.Total![2].Value);
        Assert.Equal(progress.Total[2].Value, quality.Total[1].Value);
        Assert.Equal((progress.Total[2].Value, progress.Total[1].Value), (productivity.Total![2].Value, productivity.Total[3].Value));
        Assert.Equal(quality.Total[2].Value, quality.Rows.Sum(r => r[2].Value));
        Assert.Equal(archive.Total![1].Value, archive.Rows.Sum(r => r[1].Value));
        Assert.Equal((double)figures.Status(DocumentStatus.Archived), archive.Total[1].Value);
        Assert.Equal((double)snapshot.StatusCounts[DocumentStatus.Archived], archive.Total[1].Value);
        // Reviewed in the productivity report = approved by a person; still open = the review queue plus the failed jobs.
        Assert.Equal((double)figures.Reviewed, productivity.Total[5].Value);
        Assert.Equal((double)(snapshot.Facts.WaitingFlagged + figures.Status(DocumentStatus.Failed)), exceptions.Total![3].Value);
    }

    [Fact]
    public async Task Productivity_lists_the_operators_and_reviewers_of_the_history_with_their_roles()
    {
        var table = await Reports.BuildAsync(ReportKind.Productivity, Today.AddDays(-7), Today.AddDays(-1), CancellationToken.None);

        var users = table.Rows.Select(r => r[0].Text).ToList();
        Assert.Equal(HistoryProfile.Operators.Where(o => o.LastDay is null).Select(o => o.UserName), users.Take(11));
        Assert.Equal(HistoryProfile.Reviewers.Select(r => r.UserName), users.Skip(11));
        Assert.All(table.Rows.Take(11), r => Assert.Equal("Capture operator", r[1].Text));
        Assert.All(table.Rows.Skip(11), r => Assert.Equal("OCR reviewer", r[1].Text));
    }

    [Fact]
    public async Task Activity_report_counts_what_each_user_did()
    {
        Audit.Record("tester-1", AuditAction.SignedIn);
        Audit.Record("tester-1", AuditAction.Downloaded, "DOC-1");
        Audit.Record("tester-1", AuditAction.EditedInWord, "DOC-1");
        Audit.Record("tester-2", AuditAction.StatusChanged, "DOC-1", status: DocumentStatus.Archived);

        var table = await Reports.BuildAsync(ReportKind.Activity, Today, Today, CancellationToken.None);

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
    public async Task Export_writes_the_report_as_csv_and_logs_it()
    {
        var from = Today.AddDays(-6);
        var week = ProjectReports.For((await Snapshot()).Facts, ReportPeriod.Week);

        var file = await Reports.ExportAsync(ReportKind.Progress, from, Today, "admin", CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content[..3]);
        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(9, lines.Length);
        Assert.Equal("Date,Pages scanned,Documents,Daily target,% of target,Project to date,Plan to date", lines[0]);
        Assert.StartsWith(Iso(Today) + ",0,0,20000,", lines[7], StringComparison.Ordinal);
        Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"Total,{week.Pages},{week.Documents},"), lines[8], StringComparison.Ordinal);
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

    private static DateOnly HistoryStart => ProjectHistory.FirstDay(Today);

    private static string KpiStatusOf(IRenderedComponent<DashboardPage> cut, KpiKind kind) => cut.Find($"#kpi-{kind} .kpi-status").TextContent;

    private static string Number(double value) => ((int)Math.Round(value)).ToString("N0", CultureInfo.CurrentCulture);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
}
