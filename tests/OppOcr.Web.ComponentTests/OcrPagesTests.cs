using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
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
            Assert.Equal(2, cut.FindAll("a[href^='ocr/jobs/job_']").Count);
        });
    }

    [Fact]
    public void Job_list_explains_the_temporary_source_and_refreshes_on_changes()
    {
        var job = _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "scan.png", OcrJobStatus.Queued) with { QueuePosition = 3 });
        var cut = Render<OcrJobsPage>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("cannot list jobs yet", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("In queue, position 3", cut.Markup, StringComparison.Ordinal);
        });

        ((Modules.Ocr.Application.Ports.IOcrJobRegistry)_ocr).Update(job with { Status = OcrJobStatus.Running, QueuePosition = null, CurrentStep = "page 1/1: reading (view 2/4)" });
        _ = _notifications.NotifyAsync(new OcrJobChange(job.Id, job.FileName, OcrJobStatus.Running, OcrJobChangeKind.Updated), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Contains("reading (view 2/4)", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Search_filters_jobs_by_file_name()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "invoice.png", OcrJobStatus.Done));
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000002", "letter.png", OcrJobStatus.Done));
        var cut = Render<OcrJobsPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));

        cut.Find("#job-search").Input("INVO");

        cut.WaitForAssertion(() => Assert.Equal("invoice.png", Assert.Single(cut.FindAll("tbody tr")).QuerySelector("td a")!.TextContent));
    }

    [Fact]
    public void Search_without_matches_says_so()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "invoice.png", OcrJobStatus.Done));
        var cut = Render<OcrJobsPage>();

        cut.Find("#job-search").Input("zzz");

        cut.WaitForAssertion(() => Assert.Contains("No jobs match “zzz”.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Deleting_a_queued_job_cancels_it_on_the_service()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "scan.png", OcrJobStatus.Queued));
        var cut = Render<OcrJobsPage>();

        cut.WaitForElement("button[aria-label='Delete job scan.png']").Click();
        cut.Find("[role=group] button.btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains("No OCR jobs yet.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["job_0000000000000001"], _ocr.Cancelled);
    }

    [Fact]
    public void Finished_job_shows_rtl_text_translated_warnings_and_downloads()
    {
        var page = new OcrPage(1, OcrDocumentType.Printed, "لما كان القانون", 63, "A v2",
            ["the full-page read repeated itself; this page was read block by block"], Cached: false);
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000002", "contract.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page] });
        SetCulture("ar");

        var cut = Render<OcrJobPage>(p => p.Add(x => x.JobId, "job_0000000000000002"));

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find(".ocr-text");
            Assert.Equal("rtl", text.GetAttribute("dir"));
            Assert.Equal("ar", text.GetAttribute("lang"));
            Assert.Equal("لما كان القانون", text.TextContent);
            Assert.Contains("تكرّر النص عند قراءة الصفحة كاملة", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("ltr", cut.Find("code").GetAttribute("dir"));
            Assert.Equal(4, cut.FindAll("a[download]").Count);
        });
    }

    [Fact]
    public void Failed_job_shows_localized_service_error()
    {
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000003", "big.pdf", OcrJobStatus.Failed) with { ErrorCode = "processing_error" });

        var cut = Render<OcrJobPage>(p => p.Add(x => x.JobId, "job_0000000000000003"));

        cut.WaitForAssertion(() => Assert.Contains("Processing failed on the OCR service", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("a[download]"));
    }

    [Fact]
    public void Unknown_job_id_shows_not_found()
    {
        var cut = Render<OcrJobPage>(p => p.Add(x => x.JobId, "job_does0not0exist"));

        cut.WaitForAssertion(() => Assert.Contains("OCR job not found.", cut.Markup, StringComparison.Ordinal));
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
