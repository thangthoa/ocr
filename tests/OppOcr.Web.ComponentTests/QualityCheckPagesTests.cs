using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.ComponentTests;

/// <summary>Review round 1 as people see it: the automatic check on finished jobs, its settings and the review queue.</summary>
public sealed class QualityCheckPagesTests : BunitContext
{
    private const string JobId = "job_0000000000000007";

    private readonly InMemoryOcr _ocr;
    private readonly OcrJobNotifications _notifications;

    public QualityCheckPagesTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _notifications) = Services.AddOcrAndDocuments();
        Services.AddHealthChecks();
    }

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    private QualitySettingsStore Settings => Services.GetRequiredService<QualitySettingsStore>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    [Fact]
    public void A_finished_job_that_passes_is_approved_without_review()
    {
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 0, MinPageConfidence = 0 }, "admin");
        var job = _ocr.Seed(DoneJob(Page(1, "نص")));

        var cut = RenderDocument(job.Id.Value);

        Assert.Equal("Passed", cut.Find("#quality-outcome").TextContent.Trim());
        Assert.Contains("archived without manual review", cut.Find("#quality-check").TextContent, StringComparison.Ordinal);
        Assert.Equal(DocumentStore.QualityCheckAuthor, cut.Find("#approved-by").TextContent);
        Assert.Contains("Simulated", cut.Markup, StringComparison.Ordinal);
        var entry = Audit.Latest()[0];
        Assert.Equal((AuditAction.QualityChecked, DocumentStatus.Archived, "0"), (entry.Action, entry.Status, entry.Detail));
    }

    [Fact]
    public void When_passed_documents_are_not_approved_automatically_they_wait_in_the_queue()
    {
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 0, MinPageConfidence = 0, AutoApprove = false }, "admin");
        var job = _ocr.Seed(DoneJob(Page(1, "نص")));

        var cut = RenderDocument(job.Id.Value);

        Assert.Equal("Passed", cut.Find("#quality-outcome").TextContent.Trim());
        Assert.NotEmpty(cut.FindAll("#action-approve"));
        Assert.Equal(DocumentStatus.NeedsReview, Store.FromJob(job).Status);
    }

    [Fact]
    public void A_flagged_job_goes_to_the_review_queue_with_its_reasons()
    {
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 100 }, "admin");
        _ocr.Seed(DoneJob(Page(1, "نص"), Page(2, "نص", "line detection (Kraken) failed")));
        RenderDocument(JobId);

        var queue = Render<ReviewPage>();

        queue.WaitForAssertion(() => Assert.Equal("5", queue.Find("#review-count").TextContent));
        Assert.Equal("5", queue.Find("#review-flagged").TextContent);
        var row = queue.Find($"tbody tr:first-child");
        Assert.Contains("docs.png", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("is below the threshold", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Pages with OCR problems: 2", row.TextContent, StringComparison.Ordinal);
        Assert.NotNull(row.QuerySelector(".low-confidence"));
    }

    [Fact]
    public void Samples_waiting_for_review_show_why_the_check_flagged_them()
    {
        var queue = Render<ReviewPage>();

        queue.WaitForAssertion(() => Assert.Equal("4", queue.Find("#review-flagged").TextContent));
        Assert.Contains("Document confidence 71.5% is below the threshold", queue.Markup, StringComparison.Ordinal);
        Assert.Equal("0", queue.Find("#review-auto-approved").TextContent);
    }

    [Fact]
    public void Issues_on_the_document_page_lead_to_the_page_they_are_on()
    {
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 0, MinPageConfidence = 0 }, "admin");
        _ocr.Seed(DoneJob(Page(1, "نص"), Page(2, string.Empty)));

        var cut = RenderDocument(JobId);

        Assert.Equal("Issues: 1", cut.Find("#quality-outcome").TextContent.Trim());
        var thumbs = cut.FindAll(".page-thumb");
        Assert.Equal([false, true], thumbs.Select(t => t.ClassList.Contains("flagged")));
        Assert.Equal("Page 2, has issues", thumbs[1].GetAttribute("aria-label"));

        cut.Find("#quality-check .issue-link").Click();

        Assert.Contains("Page 2 of 2", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(DocumentStatus.NeedsReview, Store.FromJob(_ocr.Registry[0]).Status);
    }

    [Fact]
    public void Each_page_shows_its_confidence_and_whether_it_is_below_the_threshold()
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, "DOC-2026-000105"));
        var sample = Store.FindSample("DOC-2026-000105")!;
        var low = sample.Quality!.Issues.First(i => i.Kind == QualityIssueKind.LowPageConfidence).Page!.Value;

        cut.FindAll(".page-thumb")[low - 1].Click();

        Assert.Contains("Confidence of page", cut.Find("#page-confidence").TextContent, StringComparison.Ordinal);
        Assert.Contains("below 75.0%", cut.Find("#page-confidence").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#page-confidence").QuerySelector(".low-confidence"));
    }

    [Fact]
    public void Settings_saved_on_the_administration_page_apply_to_the_next_check_and_are_logged()
    {
        var admin = Render<AdminPage>();
        admin.Find("#tab-Quality").Click();

        admin.Find("#min-document").Change("90");
        admin.Find("#min-page").Change("60.5");
        admin.Find("#require-Reference").Change(true);
        admin.Find("#passed-review").Change(true);
        admin.Find("#quality-form").Submit();

        Assert.NotNull(admin.Find("#quality-saved"));
        var saved = Settings.Current;
        Assert.Equal((90, 60.5, false), (saved.MinDocumentConfidence, saved.MinPageConfidence, saved.AutoApprove));
        Assert.Equal([RequiredMetadata.Reference], saved.RequiredMetadata);
        Assert.Equal(AuditAction.QualitySettingsChanged, Audit.Latest()[0].Action);

        _ocr.Seed(DoneJob(Page(1, "نص")));
        var document = RenderDocument(JobId);
        Assert.Contains("Missing metadata: Reference no.", document.Find("#quality-check").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("101")]
    [InlineData("-1")]
    public void A_threshold_outside_0_to_100_is_not_saved(string value)
    {
        var admin = Render<AdminPage>();
        admin.Find("#tab-Quality").Click();

        admin.Find("#min-page").Change(value);
        admin.Find("#quality-form").Submit();

        Assert.Equal("Enter a number from 0 to 100.", admin.Find("#min-page-error").TextContent);
        Assert.Same(QualitySettings.Default, Settings.Current);
        Assert.Empty(Audit.Latest());
    }

    [Fact]
    public async Task A_job_that_finishes_is_checked_without_anyone_opening_it()
    {
        Settings.Save(QualitySettings.Default with { MinDocumentConfidence = 0, MinPageConfidence = 0 }, "admin");
        var job = _ocr.Seed(DoneJob(Page(1, "نص")));
        using var trigger = new QualityCheckTrigger(_notifications, Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<QualityCheckTrigger>.Instance);
        await trigger.StartAsync(Xunit.TestContext.Current.CancellationToken);

        await _notifications.NotifyAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Done, OcrJobChangeKind.Updated),
            Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(DocumentStatus.Archived, Store.FromJob(job with { Pages = [] }).Status);
        await trigger.StopAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Other_job_changes_do_not_run_the_check()
    {
        var job = _ocr.Seed(DoneJob(Page(1, "نص")));
        using var trigger = new QualityCheckTrigger(_notifications, Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<QualityCheckTrigger>.Instance);

        await trigger.OnJobChangedAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Running, OcrJobChangeKind.Updated));

        Assert.Empty(Audit.Latest());
        Assert.Null(Store.FromJob(job with { Pages = [] }).Quality);
    }

    private IRenderedComponent<DocumentDetailPage> RenderDocument(string id)
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, id));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static OcrJob DoneJob(params OcrPage[] pages) =>
        InMemoryOcr.NewJob(JobId, "docs.png", OcrJobStatus.Done) with
        {
            PagesTotal = pages.Length,
            PagesDone = pages.Length,
            Pages = pages,
            CreatedAtUtc = new DateTime(2026, 11, 19, 7, 0, 0, DateTimeKind.Utc),
        };

    private static OcrPage Page(int number, string text, params string[] warnings) =>
        new(number, OcrDocumentType.Printed, text, 1, "test", warnings, Cached: false);
}
