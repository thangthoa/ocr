using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.ComponentTests;

/// <summary>OCR jobs shown as documents (the former OCR job pages were merged into Documents).</summary>
public sealed class DocumentsPagesTests : BunitContext
{
    private readonly InMemoryOcr _ocr;
    private readonly OcrJobNotifications _notifications;

    public DocumentsPagesTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _notifications) = Services.AddOcrAndDocuments();
    }

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    [Fact]
    public void Ocr_jobs_are_listed_with_their_progress_and_refresh_on_changes()
    {
        var job = _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "scan.png", OcrJobStatus.Queued) with { QueuePosition = 3 });
        var cut = Render<DocumentsPage>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("cannot list jobs yet", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("In queue, position 3", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("15 document(s)", cut.Markup, StringComparison.Ordinal);
        });

        ((IOcrJobRegistry)_ocr).Update(job with { Status = OcrJobStatus.Running, QueuePosition = null, CurrentStep = "page 1/1: reading (view 2/4)" });
        _ = _notifications.NotifyAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Running, OcrJobChangeKind.Updated), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Contains("reading (view 2/4)", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Ocr_jobs_can_be_found_by_file_name()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "invoice.png", OcrJobStatus.Done));
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000002", "letter.png", OcrJobStatus.Done));
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        cut.Find("#doc-field").Change(nameof(DocumentSearchField.FileName));
        cut.Find("#doc-search").Input("INVO");

        cut.WaitForAssertion(() => Assert.Equal("invoice.png", Assert.Single(cut.FindAll("tbody tr")).QuerySelector("td a")!.TextContent));
    }

    [Fact]
    public void Search_without_matches_says_so()
    {
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        cut.Find("#doc-search").Input("zzz");

        cut.WaitForAssertion(() => Assert.Contains("No documents match these filters.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Deleting_a_queued_job_cancels_it_on_the_service()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "scan.png", OcrJobStatus.Queued));
        var cut = Render<DocumentsPage>();

        cut.WaitForElement("button[aria-label='Delete job scan.png']").Click();
        cut.Find("[role=alertdialog] button.btn-danger").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("scan.png", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["job_0000000000000001"], _ocr.Cancelled);
        Assert.Empty(cut.FindAll("[role=alertdialog]"));
    }

    [Fact]
    public void Delete_asks_for_confirmation_in_a_dialog_that_can_be_cancelled()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "scan.png", OcrJobStatus.Queued));
        var cut = Render<DocumentsPage>();

        cut.WaitForElement("button[aria-label='Delete job scan.png']").Click();
        var dialog = cut.Find("[role=alertdialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Delete “scan.png”?", cut.Find("[role=alertdialog] h2").TextContent);
        Assert.Contains("only cancels jobs that are still queued", dialog.TextContent, StringComparison.Ordinal);

        cut.Find("[role=alertdialog] button.btn-secondary").Click();
        Assert.Empty(cut.FindAll("[role=alertdialog]"));

        cut.Find("button[aria-label='Delete job scan.png']").Click();
        cut.Find("[role=alertdialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role=alertdialog]"));

        Assert.Empty(_ocr.Cancelled);
        Assert.Contains("scan.png", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Samples_are_still_shown_when_the_ocr_service_is_down()
    {
        _ocr.Unavailable = true;

        var cut = Render<DocumentsPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("OCR jobs could not be loaded", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("14 document(s)", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Finished_job_shows_rtl_text_translated_warnings_and_downloads()
    {
        var page = new OcrPage(1, OcrDocumentType.Printed, "لما كان القانون", 63, "A v2",
            ["the full-page read repeated itself; this page was read block by block"], Cached: false);
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000002", "contract.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page] });
        TestServices.SetCulture("ar");

        var cut = RenderDocument("job_0000000000000002");

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("#pane-result .page-text");
            Assert.Equal("rtl", text.GetAttribute("dir"));
            Assert.Equal("ar", text.GetAttribute("lang"));
            Assert.Equal("لما كان القانون", text.TextContent);
            Assert.Contains("تكرّر النص عند قراءة الصفحة كاملة", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("ltr", cut.Find("code").GetAttribute("dir"));
            // One download: the current version, in the format picked next to the page numbers.
            Assert.Equal("/documents/job_0000000000000002/file?format=docx", Assert.Single(cut.FindAll("a[download]")).GetAttribute("href"));
            Assert.NotNull(cut.Find("img.page-image[src='/ocr/jobs/job_0000000000000002/pages/1/image']"));
        });
    }

    [Fact]
    public void A_document_mixing_printed_and_handwritten_pages_shows_both_kinds()
    {
        OcrPage[] pages =
        [
            new(1, OcrDocumentType.Handwritten, "أ", 5, "B", [], Cached: false),
            new(2, OcrDocumentType.Printed, "ب", 5, "A v2", [], Cached: false),
        ];
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000014", "mixed.pdf", OcrJobStatus.Done) with { PagesTotal = 2, PagesDone = 2, Pages = pages });

        var cut = RenderDocument("job_0000000000000014");

        cut.WaitForAssertion(() => Assert.Equal("· Printed / typed + Handwritten", cut.Find("#document-types").TextContent.Trim()));
    }

    [Fact]
    public void Failed_job_shows_localized_service_error()
    {
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000003", "big.pdf", OcrJobStatus.Failed) with { ErrorCode = "processing_error" });

        var cut = RenderDocument("job_0000000000000003");

        Assert.Contains("Processing failed on the OCR service", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("a[download]"));
        Assert.Empty(cut.FindAll("#action-edit"));
    }

    [Fact]
    public void Unknown_job_shows_not_found()
    {
        var cut = RenderDocument("job_does0not0exist");

        Assert.Contains("Document not found.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Finished_job_details_can_be_corrected_as_a_new_version_and_archived()
    {
        // A warning from the OCR service makes the automatic check send the document to review.
        var page = new OcrPage(1, OcrDocumentType.Printed, "نص خطا", 10, "A v2", ["line detection (Kraken) failed"], Cached: false);
        var job = InMemoryOcr.NewJob("job_0000000000000004", "memo.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page] };
        _ocr.SetOnService(job);
        var cut = RenderDocument("job_0000000000000004");
        Assert.Contains("Reviewing", cut.Find(".badge").TextContent, StringComparison.Ordinal);

        // The text is corrected in Word (see WordEditorTests); the details here, from the Metadata tab.
        cut.Find("#edit-metadata").Click();
        Assert.Empty(cut.FindAll(".doc-band-actions button, #edit-text"));
        cut.Find("#edit-reference").Change("MEMO/2026/1");
        cut.Find("#save-edit").Click();
        cut.Find("#action-approve").Click();

        var document = Store.FromJob(job);
        Assert.Contains("Status changed to Archived.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(DocumentStatus.Archived, document.Status);
        Assert.Equal(["1.0", "1.1"], document.Versions.Select(v => v.Number));
        Assert.Equal("MEMO/2026/1", document.Metadata.Reference);
        Assert.Equal(["نص خطا"], document.PageTexts);
    }

    [Fact]
    public void Old_job_links_point_to_the_document_page() =>
        Assert.Equal("documents/job_0000000000000001", OcrEndpoints.JobUrl("job_0000000000000001"));

    private IRenderedComponent<DocumentDetailPage> RenderDocument(string id)
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, id));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading", cut.Markup, StringComparison.Ordinal));
        return cut;
    }
}
