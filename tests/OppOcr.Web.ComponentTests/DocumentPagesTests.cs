using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.ComponentTests;

/// <summary>
/// Inserting, replacing and deleting pages of a document, and the pages the OCR service reports as duplicates
/// (API doc §3.8, §4.4), on the document page and in what the app keeps for the document.
/// </summary>
public sealed class DocumentPagesTests : BunitContext
{
    private const string Id = "job_0000000000000061";
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private readonly InMemoryOcr _ocr;

    public DocumentPagesTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments();
        Settings.Save(QualitySettings.Default with { AutoApprove = false }, "admin");
    }

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    private QualitySettingsStore Settings => Services.GetRequiredService<QualitySettingsStore>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    [Fact]
    public void Duplicates_reported_on_upload_can_be_removed_before_the_pages_are_read()
    {
        _ocr.Seed(InMemoryOcr.NewJob(Id, "scan.pdf", OcrJobStatus.Queued) with
        {
            PagesTotal = 10,
            QueuePosition = 2,
            Duplicates = [new OcrPageDuplicate(3, 7, OcrDuplicateKind.Identical)],
        });

        var cut = RenderDocument();

        var warning = cut.Find("#duplicate-pages");
        Assert.Contains("Pages 3 and 7 are the same file.", warning.TextContent, StringComparison.Ordinal);
        // Both pages can be looked at before choosing: their scans exist before they are read.
        warning.QuerySelectorAll("button.issue-link")[1].Click();
        Assert.Equal($"/ocr/jobs/{Id}/pages/7/image", cut.Find("#pane-original img.page-image").GetAttribute("src"));

        cut.Find("#duplicate-pages button[aria-label='Delete page 7, a duplicate of page 3']").Click();
        Assert.Contains("Page 7 is removed and the pages after it move up.", cut.Find(".confirm-message").TextContent, StringComparison.Ordinal);
        cut.Find(".confirm-dialog .btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains("Page 7 deleted", cut.Find("#pages-message").TextContent, StringComparison.Ordinal));
        Assert.Equal(("delete", (int?)7), (_ocr.PageEdits[0].Method, _ocr.PageEdits[0].Page));
        Assert.Empty(cut.FindAll("#duplicate-pages"));
        Assert.Contains("Page 7 of 9", cut.Find(".page-pager").TextContent, StringComparison.Ordinal);
        // The image of the page that now has number 7 is not taken from the browser cache.
        Assert.Equal($"/ocr/jobs/{Id}/pages/7/image?v=1", cut.Find("#pane-original img.page-image").GetAttribute("src"));

        var entry = Audit.Latest(1)[0];
        Assert.Equal((AuditAction.PageDeleted, Id, "7"), (entry.Action, entry.DocumentId, entry.Version));
    }

    [Fact]
    public void Each_kind_of_duplicate_is_explained()
    {
        SeedDone(pages: 9, duplicates: [new OcrPageDuplicate(2, 5, OcrDuplicateKind.Similar), new OcrPageDuplicate(4, 8, OcrDuplicateKind.SameText)]);

        var items = RenderDocument().FindAll("#duplicate-pages li").Select(li => li.TextContent).ToList();

        Assert.Contains("Pages 2 and 5 look like the same page scanned twice.", items[0], StringComparison.Ordinal);
        Assert.Contains("Pages 4 and 8 have almost the same text (95 % or more).", items[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_a_page_of_a_document_in_review_makes_a_new_version_from_the_ocr_text()
    {
        SeedDone(pages: 3);
        var cut = RenderDocument();
        cut.Find("#page-next").Click();

        cut.Find(".page-tools button[aria-haspopup=dialog]").Click();
        cut.Find(".confirm-dialog .btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains("Page 2 deleted", cut.Find("#pages-message").TextContent, StringComparison.Ordinal));
        var document = Store.FromJob(_ocr.OnService(Id));
        Assert.Equal(2, document.Pages);
        Assert.Equal(["نص الصفحة 1", "نص الصفحة 3"], document.PageTexts);
        Assert.Equal(("1.1", DocumentVersionChange.PagesChanged), (document.CurrentVersion!.Number, document.CurrentVersion.Change));
        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
        // The service is rebuilding its Word file meanwhile: pages wait, and so does the Word editor.
        Assert.Contains("finishing the Word file", cut.Find("#pages-busy").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("#insert-pages").HasAttribute("disabled"));
    }

    [Fact]
    public void Pages_are_inserted_at_the_chosen_position_and_the_new_pages_are_read()
    {
        SeedDone(pages: 4);
        var cut = RenderDocument();

        cut.Find("#insert-pages").Click();
        Assert.Equal("5", cut.Find("#insert-position").GetAttribute("value"));
        Assert.Equal(["Page 1", "Page 2", "Page 3", "Page 4", "Page 5, at the end"], cut.FindAll("#insert-position option").Select(o => o.TextContent));
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "trang_10.png"), InputFileContent.CreateFromBinary(PngBytes, "trang_2.png"));
        cut.Find("#insert-position").Change("2");
        cut.Find("#insert-submit").Click();

        cut.WaitForAssertion(() => Assert.Contains("Inserted 2 file(s) as page 2", cut.Find("#pages-message").TextContent, StringComparison.Ordinal));
        var sent = Assert.Single(_ocr.PageEdits);
        Assert.Equal(("insert", (int?)2), (sent.Method, sent.Page));
        Assert.Equal(["trang_2.png", "trang_10.png"], sent.Files);
        Assert.Empty(cut.FindAll("#insert-form"));
        Assert.Contains("Page 2 of 6", cut.Find(".page-pager").TextContent, StringComparison.Ordinal);
        // The job is queued again: the document is recognized until it is done.
        Assert.Contains("Recognizing", cut.Find("li[aria-current=step]").TextContent, StringComparison.Ordinal);
        Assert.Equal(AuditAction.PagesInserted, Audit.Latest(1)[0].Action);
    }

    [Fact]
    public void A_page_is_replaced_with_one_new_image()
    {
        SeedDone(pages: 3);
        var cut = RenderDocument();
        cut.Find("#page-next").Click();

        cut.Find("#replace-page").Click();
        Assert.Contains("New image of page 2", cut.Find("#replace-form label").TextContent, StringComparison.Ordinal);
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "a.png"), InputFileContent.CreateFromBinary(PngBytes, "b.png"));
        Assert.Contains("Choose one file", cut.Find("#pages-error").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("#replace-submit").HasAttribute("disabled"));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "trang_02_scan_lai.png"));
        cut.Find("#replace-submit").Click();

        cut.WaitForAssertion(() => Assert.Contains("Page 2 replaced", cut.Find("#pages-message").TextContent, StringComparison.Ordinal));
        Assert.Equal(("replace", (int?)2, "trang_02_scan_lai.png"), (_ocr.PageEdits[0].Method, _ocr.PageEdits[0].Page, Assert.Single(_ocr.PageEdits[0].Files)));
    }

    [Fact]
    public void When_the_service_is_busy_the_user_is_told_to_try_again_later()
    {
        SeedDone(pages: 3);
        _ocr.PageEditError = Error.Conflict("job_busy", "busy");
        var cut = RenderDocument();

        cut.Find(".page-tools button[aria-haspopup=dialog]").Click();
        cut.Find(".confirm-dialog .btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains("Try again when it is done.", cut.Find("#pages-error").TextContent, StringComparison.Ordinal));
        Assert.Equal(3, Store.FromJob(_ocr.OnService(Id)).Pages);
    }

    [Fact]
    public void While_ocr_reads_the_pages_they_cannot_change()
    {
        _ocr.Seed(InMemoryOcr.NewJob(Id, "scan.pdf", OcrJobStatus.Running) with { PagesTotal = 3 });

        var cut = RenderDocument();

        Assert.True(cut.Find("#insert-pages").HasAttribute("disabled"));
        Assert.True(cut.Find("#replace-page").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".page-tools button[aria-haspopup=dialog]"));
        Assert.Contains("OCR is reading the pages", cut.Find("#pages-busy").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_only_page_can_be_replaced_but_not_deleted()
    {
        SeedDone(pages: 1);

        var cut = RenderDocument();

        Assert.False(cut.Find("#replace-page").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".page-tools button[aria-haspopup=dialog]"));
    }

    [Fact]
    public void Archived_and_sample_documents_offer_no_page_changes()
    {
        SeedDone(pages: 2);
        Store.FromJob(_ocr.Registry[0]);
        Store.ChangeStatus(Id, DocumentStatus.Archived, "admin");

        Assert.Empty(RenderDocument().FindAll(".pages-panel"));
        Assert.Empty(Render<DocumentDetailPage>(p => p.Add(x => x.Id, "DOC-2026-000105")).FindAll(".pages-panel"));
    }

    [Fact]
    public void Changed_pages_are_checked_again_but_the_document_stays_in_review()
    {
        // Lenient enough to pass: the first result is approved and archived on its own.
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 0, MinPageConfidence = 0, AutoApprove = true }, "admin");
        var job = SeedDone(pages: 3);
        Assert.Equal(DocumentStatus.Archived, Store.FromJob(job).Status);

        // Pages changed through the API, outside the app: a third page was deleted.
        var changed = job with { PagesTotal = 2, PagesDone = 2, Pages = job.Pages.Take(2).ToList(), Edits = 1 };
        var document = Store.FromJob(changed);

        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
        Assert.Null(document.Approval);
        Assert.Equal(2, document.PageConfidences!.Count);
        Assert.True(document.Quality!.Passed);
        Assert.Equal(["1.0", "1.1"], document.Versions.Select(v => v.Number));
        // Read again with the same job: nothing new.
        Assert.Equal(2, Store.FromJob(changed).Versions.Count);
    }

    [Fact]
    public void A_document_with_duplicate_pages_waits_for_a_person_instead_of_being_archived()
    {
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 0, MinPageConfidence = 0, AutoApprove = true }, "admin");
        var job = SeedDone(pages: 3, duplicates: [new OcrPageDuplicate(1, 3, OcrDuplicateKind.Similar)]);

        var document = Store.FromJob(job);

        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
        Assert.Equal(new QualityIssue(QualityIssueKind.DuplicatePage, 3, DuplicateOf: 1), Assert.Single(document.Quality!.Issues));
        var text = Services.GetRequiredService<IStringLocalizer<MockupText>>();
        Assert.Equal("Page 3 may be the same page as page 1", text.Issue(document.Quality.Issues[0]));
        Assert.Equal(["Possible duplicate pages: 3"], text.IssueSummary(document.Quality));

        // The copy is deleted: checked again without the issue.
        var after = Store.FromJob(job with { PagesTotal = 2, PagesDone = 2, Pages = job.Pages.Take(2).ToList(), Duplicates = [], Edits = 1 });
        Assert.True(after.Quality!.Passed);
    }

    [Fact]
    public void Text_edited_before_the_pages_changed_stays_in_the_version_history()
    {
        var job = SeedDone(pages: 2);
        Store.FromJob(job);
        Store.SaveEdit(Id, ["مصحح", "نص الصفحة 2"], Store.FromJob(job).Metadata, "admin", null);

        var document = Store.FromJob(job with { Edits = 1, Pages = [.. job.Pages, Page(3)], PagesTotal = 3, PagesDone = 3 });

        Assert.Equal(["نص الصفحة 1", "نص الصفحة 2", "نص الصفحة 3"], document.PageTexts);
        Assert.Equal(["مصحح", "نص الصفحة 2"], document.Versions[1].PageTexts);
    }

    [Fact]
    public void Page_changes_are_described_in_the_activity_log()
    {
        var text = Services.GetRequiredService<IStringLocalizer<MockupText>>();

        Assert.Equal("Inserted 2 file(s) as page 5", text.DescribeActivity(new AuditEntry(DateTime.UtcNow, "admin", AuditAction.PagesInserted, Version: "5", Detail: "2")));
        Assert.Equal("Replaced page 3", text.DescribeActivity(new AuditEntry(DateTime.UtcNow, "admin", AuditAction.PageReplaced, Version: "3")));
        Assert.Equal("Deleted page 7", text.DescribeActivity(new AuditEntry(DateTime.UtcNow, "admin", AuditAction.PageDeleted, Version: "7")));
    }

    private OcrJob SeedDone(int pages, params OcrPageDuplicate[] duplicates) =>
        _ocr.Seed(InMemoryOcr.NewJob(Id, "scan.pdf", OcrJobStatus.Done) with
        {
            PagesTotal = pages,
            PagesDone = pages,
            Pages = [.. Enumerable.Range(1, pages).Select(Page)],
            Duplicates = duplicates,
        });

    private static OcrPage Page(int number) => new(number, OcrDocumentType.Printed, $"نص الصفحة {number}", 5, "A v2", [], Cached: false);

    private IRenderedComponent<DocumentDetailPage> RenderDocument()
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, Id));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("h1")));
        return cut;
    }
}
