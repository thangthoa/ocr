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
    private readonly InMemoryOcr _ocr = new();
    private readonly OcrJobNotifications _notifications = new(NullLogger<OcrJobNotifications>.Instance);

    public OcrPagesTests()
    {
        SetCulture("en");
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddOcrApplication();
        Services.AddSingleton<IOcrApiClient>(_ocr);
        Services.AddSingleton<IOcrJobRegistry>(_ocr);
        Services.AddSingleton(_notifications);
        Services.AddSingleton<IOcrJobNotifier>(_notifications);
        Services.AddMockups();
    }

    [Fact]
    public void Upload_is_disabled_until_a_document_type_is_chosen()
    {
        var cut = Render<OcrUploadPage>();

        Assert.True(cut.Find("#ocr-files").HasAttribute("disabled"));
        cut.Find("#doc-type-Printed").Change(true);
        Assert.False(cut.Find("#ocr-files").HasAttribute("disabled"));
    }

    [Fact]
    public void Each_file_is_sent_to_the_service_and_unsupported_files_are_rejected()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#doc-type-Handwritten").Change(true);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "a.png"),
            InputFileContent.CreateFromBinary("%PDF-1.4"u8.ToArray(), "b.pdf"),
            InputFileContent.CreateFromBinary(PngBytes, "c.gif"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, _ocr.Registry.Count);
            Assert.All(_ocr.Registry, j => Assert.Equal(OcrDocumentType.Handwritten, j.DocumentType));
            Assert.Contains("Only JPG, JPEG, PNG", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("2 job(s) created on the OCR service", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(2, cut.FindAll(".upload-results a[href^='documents/job_']").Count);
        });
    }

    [Fact]
    public void Folder_upload_sends_supported_files_and_skips_the_rest()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#doc-type-Printed").Change(true);

        cut.FindComponents<InputFile>()[1].UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "scan-1.png"),
            InputFileContent.CreateFromText("notes", "readme.txt"),
            InputFileContent.CreateFromBinary("%PDF-1.4"u8.ToArray(), "scan-2.pdf"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["scan-1.png", "scan-2.pdf"], _ocr.Registry.Select(j => j.FileName).Order(StringComparer.Ordinal));
            Assert.Contains("1 file(s) in the folder were skipped", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(2, cut.FindAll(".upload-results a[href^='documents/job_']").Count);
        });
    }

    [Fact]
    public void Folder_without_supported_files_uploads_nothing()
    {
        var cut = Render<OcrUploadPage>();
        cut.Find("#doc-type-Printed").Change(true);

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText("notes", "readme.txt"));

        cut.WaitForAssertion(() => Assert.Contains("The folder has no supported files", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(_ocr.Registry);
    }

    [Fact]
    public void Capture_lists_recent_scans_and_adds_new_uploads()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000077", "earlier.png", OcrJobStatus.Done) with { PagesDone = 1 });
        var cut = Render<OcrUploadPage>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".scan-history a[href='documents/job_0000000000000077']")));

        cut.Find("#doc-type-Printed").Change(true);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(PngBytes, "new-scan.png"));

        cut.WaitForAssertion(() =>
        {
            var history = cut.Find(".scan-history").TextContent;
            Assert.Contains("earlier.png", history, StringComparison.Ordinal);
            Assert.Contains("new-scan.png", history, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Batch_ticket_is_reconciled_and_kept_with_the_documents()
    {
        var cut = Render<OcrUploadPage>();
        var batchId = cut.WaitForElement("#batch-id").GetAttribute("value")!;
        Assert.StartsWith("B-", batchId, StringComparison.Ordinal);

        cut.Find("#doc-type-Printed").Change(true);
        cut.Find("#batch-box").Change("BX-00731");
        cut.Find("#batch-department").Change(nameof(DocumentDepartment.Finance));
        cut.Find("#batch-expected").Change("3");
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(PngBytes, "a.png"),
            InputFileContent.CreateFromBinary(PngBytes, "b.png"));

        cut.WaitForAssertion(() =>
        {
            var result = cut.Find("#batch-result").TextContent;
            Assert.Contains($"Batch {batchId}: 2 file(s) uploaded, 0 failed.", result, StringComparison.Ordinal);
            Assert.Contains("1 file(s) fewer than the 3 expected.", result, StringComparison.Ordinal);
            Assert.NotEqual(batchId, cut.Find("#batch-id").GetAttribute("value"));
        });

        var batch = Services.GetRequiredService<CaptureBatchStore>().ForJob(_ocr.Registry[0].Id.Value)!;
        Assert.Equal(("BX-00731", DocumentDepartment.Finance, 3), (batch.BoxBarcode, batch.Department, batch.ExpectedFiles));
        var document = Services.GetRequiredService<DocumentStore>().FromJob(_ocr.Registry[0]);
        Assert.Equal(batchId, document.BatchId);
        Assert.Equal(DocumentDepartment.Finance, document.Metadata.Department);
        Assert.Equal(2, Services.GetRequiredService<AuditLog>().Latest().Count(e => e.Action == AuditAction.Uploaded && e.BatchId == batchId));
    }

    [Fact]
    public void Capture_history_explains_when_the_ocr_service_is_down()
    {
        _ocr.Unavailable = true;

        var cut = Render<OcrUploadPage>();

        cut.WaitForAssertion(() => Assert.Contains("not available", cut.Find(".scan-history [role=alert]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Folder_button_is_disabled_until_a_document_type_is_chosen()
    {
        var cut = Render<OcrUploadPage>();

        Assert.True(cut.Find("#ocr-folder").HasAttribute("disabled"));
        Assert.True(cut.Find("#ocr-folder").HasAttribute("webkitdirectory"));
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

    private static void SetCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = LocalizationSetup.CreateCulture(name);
}
