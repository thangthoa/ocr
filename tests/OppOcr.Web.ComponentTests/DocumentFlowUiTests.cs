using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Components.Layout;
using OppOcr.Web.Features.Mockups.Components;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Ocr.Pages;

namespace OppOcr.Web.ComponentTests;

/// <summary>
/// The document flow as the UI shows it (ADR 0007): the menu, the progress of a document
/// (Scan → Recognizing → Reviewing → Archived) and the status bar that links to the lists.
/// </summary>
public sealed class DocumentFlowUiTests : BunitContext
{
    private readonly InMemoryOcr _ocr;

    public DocumentFlowUiTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments();
    }

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    [Fact]
    public void The_menu_follows_the_document_flow_then_reports_and_ends_with_settings()
    {
        var cut = Render<NavMenu>();

        var links = cut.FindAll("nav a.nav-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ToList();
        Assert.Equal(
            [("", "Dashboard"), ("scan", "Scan"), ("review", "Review Queue"), ("documents", "All Documents"),
                ("archive", "Document Archive"), ("reports", "Reports"), ("users", "Users"), ("users/roles", "Roles & access"),
                ("users/activity", "Activity logs"), ("admin", "Settings")],
            links);
    }

    [Fact]
    public void Old_addresses_still_open_the_renamed_pages()
    {
        Assert.Equal(["/archive", "/approved"], Routes<ApprovedPage>());
        Assert.Equal(["/scan", "/capture", "/ocr"], Routes<OcrUploadPage>());
    }

    [Fact]
    public void Before_an_upload_the_flow_is_on_its_first_step()
    {
        var cut = Render<DocumentProgress>();

        Assert.Equal(["is-current", "is-todo", "is-todo", "is-todo"], States(cut));
        var current = cut.Find("li[aria-current=step]");
        Assert.Contains("Scan", current.TextContent, StringComparison.Ordinal);
        Assert.Contains("Choose the file and upload it", current.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("is-live", current.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void While_the_file_is_sent_the_scan_step_shows_it_is_under_way()
    {
        var cut = Render<DocumentProgress>(p => p.Add(x => x.Uploading, true));

        var current = cut.Find("li[aria-current=step]");
        Assert.Contains("Sending to the OCR service", current.TextContent, StringComparison.Ordinal);
        Assert.Contains("is-live", current.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_queued_job_is_recognizing_and_waiting_to_start()
    {
        var job = _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000041", "scan.png", OcrJobStatus.Queued));

        var cut = RenderProgress(Store.FromJob(job));

        Assert.Equal(["is-done", "is-current", "is-todo", "is-todo"], States(cut));
        var current = cut.Find("li[aria-current=step]");
        Assert.Contains("Recognizing", current.TextContent, StringComparison.Ordinal);
        Assert.Contains("Waiting to start", current.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_job_shows_its_pages_as_it_is_recognized()
    {
        var job = _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000042", "scan.pdf", OcrJobStatus.Running) with { PagesTotal = 4, PagesDone = 1 });

        var cut = RenderProgress(Store.FromJob(job));

        Assert.Contains("Page 1 of 4", cut.Find("li[aria-current=step]").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("li[aria-current=step] .doc-step-progress"));
    }

    [Fact]
    public void A_failed_job_stops_at_recognizing_and_says_why()
    {
        var job = _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000043", "scan.png", OcrJobStatus.Failed) with { ErrorCode = "file_too_large" });

        var cut = RenderProgress(Store.FromJob(job));

        Assert.Equal(["is-done", "is-current", "is-todo", "is-todo"], States(cut));
        var halted = cut.Find("li.is-halted");
        Assert.Contains("The file is larger than 25 MB.", halted.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_in_review_is_on_the_reviewing_step()
    {
        var cut = RenderProgress(Store.FindSample("DOC-2026-000105")!);

        Assert.Equal(["is-done", "is-done", "is-current", "is-todo"], States(cut));
        Assert.Contains("Reviewing", cut.Find("li[aria-current=step]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_archived_document_has_every_step_done_and_names_its_approver()
    {
        var cut = RenderProgress(Store.FindSample("DOC-2026-000112")!);

        Assert.Equal(["is-done", "is-done", "is-done", "is-done"], States(cut));
        Assert.Empty(cut.FindAll("li[aria-current]"));
        Assert.Contains("Approved by review01", cut.FindAll("li")[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_that_passed_the_automatic_check_says_so_on_the_reviewing_step()
    {
        Store.ChangeStatus("DOC-2026-000105", DocumentStatus.Archived, DocumentStore.QualityCheckAuthor);

        var cut = RenderProgress(Store.FindSample("DOC-2026-000105")!);

        Assert.Contains("Passed the automatic check", cut.FindAll("li")[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_page_shows_its_progress_at_the_top()
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, "DOC-2026-000105"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".doc-band .doc-steps")));

        cut.Find("#action-approve").Click();

        Assert.Equal(["is-done", "is-done", "is-done", "is-done"], cut.FindAll(".doc-band .doc-step").Select(StateOf));
    }

    [Fact]
    public void The_scan_page_shows_the_flow_at_the_top()
    {
        var cut = Render<OcrUploadPage>();

        Assert.Equal(["is-current", "is-todo", "is-todo", "is-todo"], cut.FindAll(".doc-band .doc-step").Select(StateOf));
    }

    [Fact]
    public void The_scan_form_asks_for_the_file_name_first_and_has_no_link_away()
    {
        var cut = Render<OcrUploadPage>();

        var firstField = cut.Find(".capture-options input, .capture-options select");
        Assert.Equal("file-name", firstField.Id);
        Assert.Empty(cut.FindAll(".scan-main a[href='documents']"));
    }

    [Fact]
    public void All_documents_shows_version_and_last_update_in_their_own_columns_and_no_upload_button()
    {
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        var headers = cut.FindAll("thead th").Select(th => th.TextContent.Trim()).ToList();
        Assert.Equal("Last updated at", headers[headers.IndexOf("Version") + 1]);
        Assert.Empty(cut.FindAll("a[href='scan']"));
        var archived = cut.FindAll("tbody tr").First(row => row.TextContent.Contains("ADM-2022-0310.pdf", StringComparison.Ordinal));
        Assert.Equal("1.1", archived.QuerySelector(".doc-version")!.TextContent.Trim());
        Assert.Matches(@"\d{4}", archived.QuerySelector(".doc-updated")!.TextContent);
    }

    [Fact]
    public async Task The_dashboard_status_bar_links_each_status_to_the_filtered_list()
    {
        var facts = (await Services.GetRequiredService<DocumentCatalog>().SnapshotAsync(CancellationToken.None)).Facts;
        var cut = Render<DashboardPage>();
        cut.WaitForElement(".status-legend");

        Assert.Equal(
            ["documents?status=Processing", "documents?status=NeedsReview", "documents?status=Archived", "documents?status=Failed"],
            cut.FindAll(".status-legend a.status-key").Select(a => a.GetAttribute("href")));
        // A segment for each status that has documents in the period.
        Assert.Equal(ProjectReports.For(facts, ReportPeriod.Day).Statuses.Count(s => s.Documents > 0), cut.FindAll(".status-bar .status-segment").Count);
    }

    private IRenderedComponent<DocumentProgress> RenderProgress(DocumentRecord document) =>
        Render<DocumentProgress>(p => p.Add(x => x.Document, document));

    private static List<string> States(IRenderedComponent<DocumentProgress> cut) => [.. cut.FindAll("li.doc-step").Select(StateOf)];

    private static string StateOf(AngleSharp.Dom.IElement step) =>
        step.ClassList.First(c => c is "is-done" or "is-current" or "is-todo");

    private static string[] Routes<TPage>() =>
        [.. typeof(TPage).GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>().Select(r => r.Template)];
}
