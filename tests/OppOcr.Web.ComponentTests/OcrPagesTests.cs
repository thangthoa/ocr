using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
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
    public void One_file_is_sent_with_the_typed_name_and_document_type_and_its_page_opens()
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
            // The document page follows the file step by step (Scan, Recognizing, Reviewing, Archived).
            Assert.Equal("http://localhost/" + OcrEndpoints.JobUrl(job.Id.Value), Navigation.Uri);
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
            Assert.Equal("http://localhost/" + OcrEndpoints.JobUrl(job.Id.Value), Navigation.Uri);
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
    public void A_failed_upload_stays_on_the_page_with_the_error()
    {
        _ocr.Unavailable = true;
        var cut = Render<OcrUploadPage>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "IMG_1.png"));
        cut.Find("#file-name").Input("scan");

        cut.Find("#upload-button").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#capture-error")));
        Assert.Equal("http://localhost/", Navigation.Uri);
        Assert.NotEmpty(cut.FindAll(".selected-files"));
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

    [Fact]
    public async Task Multi_reads_the_chosen_files_one_at_a_time()
    {
        // With Interactive Server the browser starts sending a file as soon as it is opened; files opened
        // together but read one after another stalled each other until the upload timed out.
        var log = new List<string>();
        IBrowserFile[] files =
        [
            new RecordingBrowserFile("p1.png", PngBytes, log),
            new RecordingBrowserFile("p2.jpg", JpegBytes, log),
            new RecordingBrowserFile("p3.png", PngBytes, log),
        ];
        var cut = Render<OcrUploadPage>();
        cut.Find("#mode-Multi").Change(true);
        var input = cut.FindComponent<InputFile>();
        await cut.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs(files)));
        cut.Find("#file-name").Input("Contract");

        await cut.Find("#upload-button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(_ocr.Registry));
        Assert.Equal(["open p1.png", "end p1.png", "open p2.jpg", "end p2.jpg", "open p3.png", "end p3.png"], log);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static string[] SelectedNames(IRenderedComponent<OcrUploadPage> cut) =>
        [.. cut.FindAll(".selected-files li bdi").Select(e => e.TextContent)];

    private static void SetCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = LocalizationSetup.CreateCulture(name);

    /// <summary>A browser file that writes down when it is opened and when it has been read to the end.</summary>
    private sealed class RecordingBrowserFile(string name, byte[] content, List<string> log) : IBrowserFile
    {
        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => content.Length;

        public string ContentType => "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            log.Add($"open {name}");
            return new RecordingStream(name, content, log);
        }
    }

    private sealed class RecordingStream(string name, byte[] content, List<string> log) : MemoryStream(content, writable: false)
    {
        private bool _ended;

        public override int Read(byte[] buffer, int offset, int count) => Record(base.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Record(await base.ReadAsync(buffer, cancellationToken));

        private int Record(int read)
        {
            if (read == 0 && !_ended)
            {
                _ended = true;
                log.Add($"end {name}");
            }

            return read;
        }
    }
}
