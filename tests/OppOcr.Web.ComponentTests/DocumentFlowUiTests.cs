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
    public void The_menu_follows_the_document_flow_and_ends_with_settings()
    {
        var cut = Render<NavMenu>();

        var links = cut.FindAll("nav a.nav-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ToList();
        Assert.Equal(
            [("", "Dashboard"), ("scan", "Scan"), ("review", "Review Queue"), ("documents", "All Documents"),
                ("archive", "Document Archive"), ("admin", "Settings")],
            links);
    }

    [Fact]
    public void Old_addresses_still_open_the_renamed_pages()
    {
        Assert.Equal(["/archive", "/approved"], Routes<ApprovedPage>());
        Assert.Equal(["/scan", "/capture", "/ocr"], Routes<OcrUploadPage>());
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
        Assert.Contains("Approved by m.khalid", cut.FindAll("li")[2].TextContent, StringComparison.Ordinal);
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
    public void The_dashboard_status_bar_links_each_status_to_the_filtered_list()
    {
        var cut = Render<DashboardPage>();

        Assert.Equal(
            ["documents?status=Processing", "documents?status=NeedsReview", "documents?status=Archived", "documents?status=Failed"],
            cut.FindAll(".status-legend a.status-key").Select(a => a.GetAttribute("href")));
        Assert.Equal(4, cut.FindAll(".status-bar .status-segment").Count);
    }

    private IRenderedComponent<DocumentProgress> RenderProgress(DocumentRecord document) =>
        Render<DocumentProgress>(p => p.Add(x => x.Document, document));

    private static List<string> States(IRenderedComponent<DocumentProgress> cut) => [.. cut.FindAll("li.doc-step").Select(StateOf)];

    private static string StateOf(AngleSharp.Dom.IElement step) =>
        step.ClassList.First(c => c is "is-done" or "is-current" or "is-todo");

    private static string[] Routes<TPage>() =>
        [.. typeof(TPage).GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>().Select(r => r.Template)];
}
