using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;
using OppOcr.Web.Features.Ocr.Components;
using OppOcr.Web.Features.Ocr.Pages;
using OppOcr.Web.Localization;

namespace OppOcr.Web.ComponentTests;

public sealed class OcrPagesTests : BunitContext
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private readonly InMemoryOcr _ocr = new();
    private readonly FakePdfComposer _composer = new();
    private readonly OcrJobNotifications _notifications = new(NullLogger<OcrJobNotifications>.Instance);

    public OcrPagesTests()
    {
        SetCulture("en");
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddOcrApplication();
        Services.AddSingleton<IOcrApiClient>(_ocr);
        Services.AddSingleton<IOcrJobRegistry>(_ocr);
        Services.AddSingleton<IPdfComposer>(_composer);
        Services.AddSingleton(_notifications);
        Services.AddSingleton<IOcrJobNotifier>(_notifications);
        Services.AddDocumentsMockup();
    }

    [Fact]
    public void Capture_options_default_to_printed_and_one_file_without_a_batch_ticket()
    {
        var cut = Render<OcrUploadPage>();

        Assert.Equal(nameof(OcrDocumentType.Printed), cut.Find("#doc-type").GetAttribute("value"));
        Assert.True(cut.Find("#mode-One").HasAttribute("checked"));
        Assert.Empty(cut.Find("#file-name").GetAttribute("value") ?? string.Empty);
        Assert.False(cut.Find("#ocr-files").HasAttribute("multiple"));
        Assert.Empty(cut.FindAll("#ocr-folder"));
        Assert.True(cut.Find("#upload-button").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#batch-id, #batch-box, #batch-department, #batch-expected"));
    }

    [Fact]
    public void One_file_is_sent_with_the_typed_name_and_document_type()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#doc-type").Change(nameof(OcrDocumentType.Handwritten));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "IMG_0042.png"));
        cut.Find("#file-name").Input("Invoice 17");

        Assert.Contains("Invoice 17.png", cut.Find("#file-name-help").TextContent, StringComparison.Ordinal);
        cut.Find("#upload-button").Click();

        cut.WaitForAssertion(() =>
        {
            var job = Assert.Single(_ocr.Registry);
            Assert.Equal(("Invoice 17.png", OcrDocumentType.Handwritten), (job.FileName, job.DocumentType));
            Assert.Contains("Invoice 17.png was sent to the OCR service", cut.Find("#upload-result").TextContent, StringComparison.Ordinal);
            Assert.NotNull(cut.Find($"#upload-result a[href='{OcrEndpoints.JobUrl(job.Id.Value)}']"));
            Assert.Empty(cut.FindAll(".selected-files"));
            Assert.Empty(cut.Find("#file-name").GetAttribute("value") ?? string.Empty);
        });
        Assert.Empty(_composer.Calls);
    }

    [Fact]
    public void A_pdf_can_be_sent_as_one_file()
    {
        var cut = Render<OcrUploadPage>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary("%PDF-1.4"u8.ToArray(), "scan.pdf"));
        cut.Find("#file-name").Input("contract.PDF");
        cut.Find("#upload-button").Click();

        cut.WaitForAssertion(() => Assert.Equal("contract.PDF", Assert.Single(_ocr.Registry).FileName));
    }

    [Fact]
    public void File_name_is_required_before_anything_is_sent()
    {
        var cut = Render<OcrUploadPage>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "a.png"));

        cut.Find("#upload-button").Click();

        Assert.Contains("File name is required.", cut.Find("#file-name-help.invalid-feedback").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#file-name").GetAttribute("aria-invalid"));
        Assert.Empty(_ocr.Registry);

        cut.Find("#file-name").Input("a");
        Assert.Empty(cut.FindAll(".invalid-feedback"));
    }

    [Fact]
    public void Invalid_characters_in_the_file_name_are_rejected()
    {
        var cut = Render<OcrUploadPage>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "a.png"));
        cut.Find("#file-name").Input("../secret");
        cut.Find("#upload-button").Click();

        cut.WaitForAssertion(() => Assert.Contains("must not contain", cut.Find("#capture-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(_ocr.Registry);
    }

    [Fact]
    public void One_mode_accepts_a_single_supported_file()
    {
        var cut = Render<OcrUploadPage>();
        var input = cut.FindComponent<InputFile>();

        input.UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "a.png"), InputFileContent.CreateFromBinary(PngBytes, "b.png"));
        Assert.Contains("Choose a single file", cut.Find("#capture-error").TextContent, StringComparison.Ordinal);

        input.UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "c.gif"));
        Assert.Contains("Only JPG, JPEG, PNG", cut.Find("#capture-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".selected-files"));
    }

    [Fact]
    public void Multi_combines_images_into_one_pdf_in_the_order_shown()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);
        Assert.True(cut.Find("#ocr-files").HasAttribute("multiple"));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "page-10.png"),
            InputFileContent.CreateFromBinary(JpegBytes, "page-2.jpg"),
            InputFileContent.CreateFromBinary(PngBytes, "page-1.png"));

        // File name order counts numbers as numbers; the arrows then change it.
        Assert.Equal(["page-1.png", "page-2.jpg", "page-10.png"], SelectedNames(cut));
        cut.FindAll(".selected-files .move-up")[2].Click();
        Assert.Equal(["page-1.png", "page-10.png", "page-2.jpg"], SelectedNames(cut));

        cut.Find("#file-name").Input("Contract");
        Assert.Contains("Contract.pdf", cut.Find("#file-name-help").TextContent, StringComparison.Ordinal);
        cut.Find("#upload-button").Click();

        cut.WaitForAssertion(() =>
        {
            var job = Assert.Single(_ocr.Registry);
            Assert.Equal(("Contract.pdf", OcrDocumentType.Printed), (job.FileName, job.DocumentType));
            Assert.Contains("Contract.pdf (3 pages) was sent", cut.Find("#upload-result").TextContent, StringComparison.Ordinal);
        });
        var pages = Assert.Single(_composer.Calls);
        Assert.Equal([OcrFileFormat.Png, OcrFileFormat.Png, OcrFileFormat.Jpeg], pages.Select(p => p.Format));
    }

    [Fact]
    public void Multi_pages_can_be_removed_and_switching_mode_clears_the_selection()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "a.png"),
            InputFileContent.CreateFromBinary(PngBytes, "b.png"));

        cut.FindAll(".selected-files .remove")[0].Click();
        Assert.Equal(["b.png"], SelectedNames(cut));

        cut.Find("#mode-One").Change(true);
        Assert.Empty(cut.FindAll(".selected-files"));
    }

    [Fact]
    public void Multi_accepts_only_images()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "a.png"),
            InputFileContent.CreateFromBinary("%PDF-1.4"u8.ToArray(), "b.pdf"));

        Assert.Contains("Only JPG, JPEG, PNG images can be combined", cut.Find("#capture-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".selected-files"));
    }

    [Fact]
    public void Multi_shows_why_the_pages_could_not_be_combined()
    {
        _composer.Fail = true;
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "a.png"),
            InputFileContent.CreateFromBinary(PngBytes, "b.png"));
        cut.Find("#file-name").Input("scan");
        cut.Find("#upload-button").Click();

        cut.WaitForAssertion(() => Assert.Contains("cannot be added as a page", cut.Find("#capture-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(_ocr.Registry);
        Assert.Equal(["a.png", "b.png"], SelectedNames(cut));
    }

    [Fact]
    public void Folder_images_become_pages_in_name_order_and_other_files_are_skipped()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);

        cut.FindComponents<InputFile>()[1].UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "scan-2.png"),
            InputFileContent.CreateFromText("notes", "readme.txt"),
            InputFileContent.CreateFromBinary("%PDF-1.4"u8.ToArray(), "other.pdf"),
            InputFileContent.CreateFromBinary(JpegBytes, "scan-1.jpg"));

        Assert.Equal(["scan-1.jpg", "scan-2.png"], SelectedNames(cut));
        Assert.Contains("2 file(s) in the folder were skipped", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("#ocr-folder").HasAttribute("webkitdirectory"));
    }

    [Fact]
    public void Folder_without_images_selects_nothing()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText("notes", "readme.txt"));

        Assert.Contains("The folder has no images", cut.Find("#capture-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".selected-files"));
    }

    [Fact]
    public void Just_uploaded_lists_only_documents_uploaded_on_this_page_newest_first()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000077", "earlier.png", OcrJobStatus.Done) with { PagesDone = 1 });
        var cut = Render<OcrUploadPage>();
        Assert.Contains("Documents you upload here are listed", cut.Find(".scan-history").TextContent, StringComparison.Ordinal);

        Upload(cut, "first");
        Upload(cut, "second");

        cut.WaitForAssertion(() => Assert.Equal(["second.png", "first.png"], HistoryNames(cut)));
        Assert.DoesNotContain("earlier.png", cut.Find(".scan-history").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Just_uploaded_follows_progress_and_drops_deleted_documents()
    {
        var cut = Render<OcrUploadPage>();
        Upload(cut, "scan");
        cut.WaitForAssertion(() => Assert.Equal(["scan.png"], HistoryNames(cut)));
        var job = _ocr.Registry[0];

        _ocr.SetOnService(job with { Status = OcrJobStatus.Running, PagesTotal = 3, PagesDone = 1 });
        await _notifications.NotifyAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Running, OcrJobChangeKind.Updated), Ct);
        cut.WaitForAssertion(() => Assert.Equal("Running", cut.Find(".scan-history tbody .badge").TextContent));

        // When the service cannot be asked, the status carried by the notification is still shown.
        _ocr.Unavailable = true;
        await _notifications.NotifyAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Done, OcrJobChangeKind.Updated), Ct);
        cut.WaitForAssertion(() => Assert.Equal("Done", cut.Find(".scan-history tbody .badge").TextContent));

        await _notifications.NotifyAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Done, OcrJobChangeKind.Deleted), Ct);
        cut.WaitForAssertion(() => Assert.Empty(HistoryNames(cut)));
    }

    [Fact]
    public void Upload_is_kept_with_the_document_and_in_the_audit_log()
    {
        var cut = Render<OcrUploadPage>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "a.png"));
        cut.Find("#file-name").Input("receipt");
        cut.Find("#upload-button").Click();
        cut.WaitForAssertion(() => Assert.Single(_ocr.Registry));

        var job = _ocr.Registry[0];
        var capture = Services.GetRequiredService<CaptureBatchStore>().ForJob(job.Id.Value)!;
        Assert.Equal([job.Id.Value], capture.JobIds);
        var document = Services.GetRequiredService<DocumentStore>().FromJob(job);
        Assert.Equal((capture.Id, capture.UploadedBy), (document.BatchId, document.UploadedBy));
        var entry = Assert.Single(Services.GetRequiredService<AuditLog>().Latest(), e => e.Action == AuditAction.Uploaded);
        Assert.Equal((job.Id.Value, "receipt.png", capture.Id), (entry.DocumentId, entry.DocumentName, entry.BatchId));
    }

    [Fact]
    public async Task Toast_uses_the_viewer_language_even_when_raised_from_the_worker()
    {
        SetCulture("ar");
        var cut = Render<OcrToasts>();
        var id = OcrJobId.Parse("job_0000000000000009");

        // The background worker runs with the server default culture.
        await Task.Run(async () =>
        {
            SetCulture("en");
            await _notifications.NotifyAsync(new OcrJobChange(id, "scan.png", OcrJobStatus.Done, OcrJobChangeKind.Updated), Xunit.TestContext.Current.CancellationToken);
        }, Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Contains("اكتمل التعرف الضوئي", cut.Markup, StringComparison.Ordinal));
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static void Upload(IRenderedComponent<OcrUploadPage> cut, string name)
    {
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "IMG_1.png"));
        cut.Find("#file-name").Input(name);
        cut.Find("#upload-button").Click();
        cut.WaitForAssertion(() => Assert.Contains($"{name}.png was sent", cut.Find("#upload-result").TextContent, StringComparison.Ordinal));
    }

    private static string[] HistoryNames(IRenderedComponent<OcrUploadPage> cut) =>
        [.. cut.FindAll(".scan-history tbody a").Select(e => e.TextContent)];

    private static string[] SelectedNames(IRenderedComponent<OcrUploadPage> cut) =>
        [.. cut.FindAll(".selected-files li bdi").Select(e => e.TextContent)];

    private static void SetCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = LocalizationSetup.CreateCulture(name);
}
