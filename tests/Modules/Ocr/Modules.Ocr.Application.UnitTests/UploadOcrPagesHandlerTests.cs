using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Application.Features.UploadOcrPages;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.UnitTests;

public sealed class UploadOcrPagesHandlerTests
{
    private readonly FakeApi _api = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakePdfComposer _composer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UploadOcrPagesHandler Handler => new(
        new UploadOcrPagesValidator(),
        _composer,
        new UploadOcrFileHandler(new UploadOcrFileValidator(), _api, _registry, _notifier, NullLogger<UploadOcrFileHandler>.Instance),
        NullLogger<UploadOcrPagesHandler>.Instance);

    [Fact]
    public async Task Pages_are_combined_in_order_and_sent_as_one_pdf_job()
    {
        var png = TestJobs.Png();
        var jpeg = TestJobs.Jpeg();

        var result = await Handler.HandleAsync(Command(" Contract 7 ", Page("p1.png", png), Page("p2.JPG", jpeg)), Ct);

        Assert.True(result.IsSuccess);
        var pages = Assert.Single(_composer.Calls);
        Assert.Equal([OcrFileFormat.Png, OcrFileFormat.Jpeg], pages.Select(p => p.Format));
        Assert.Equal(png, pages[0].Content.ToArray());
        Assert.Equal(jpeg, pages[1].Content.ToArray());

        var sent = Assert.Single(_api.Submitted);
        Assert.Equal("Contract 7.pdf", sent.FileName);
        Assert.Equal(FakePdfComposer.Output, sent.Bytes);
        Assert.Equal(result.Value.Id, Assert.Single(_registry.All).Id);
        Assert.Equal(OcrJobChangeKind.Created, Assert.Single(_notifier.Changes).Kind);
    }

    [Fact]
    public async Task A_name_that_already_ends_in_pdf_is_kept()
    {
        var result = await Handler.HandleAsync(Command("scan.PDF", Page("a.png", TestJobs.Png())), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("scan.PDF", Assert.Single(_api.Submitted).FileName);
    }

    public static TheoryData<UploadOcrPagesCommand, string> InvalidCommands => new()
    {
        { Command("scan"), OcrErrorCodes.PagesRequired },
        { Command("scan", [.. Enumerable.Range(1, OcrRules.MaxPagesPerDocument + 1).Select(i => Page($"p{i}.png", TestJobs.Png()))]), OcrErrorCodes.TooManyPages },
        { Command("scan", Page("a.png", TestJobs.Png()), Page("b.pdf", TestJobs.Pdf())), OcrErrorCodes.UnsupportedPageFormat },
        { Command("scan", Page("a.tiff", TestJobs.Png())), OcrErrorCodes.UnsupportedPageFormat },
        { Command("scan", new OcrPageFile("a.png", 0, new MemoryStream())), OcrErrorCodes.Empty },
        { Command("scan", new OcrPageFile("a.png", OcrRules.MaxSizeBytes, new MemoryStream()), new OcrPageFile("b.png", 1, new MemoryStream())), OcrErrorCodes.TooLarge },
        { Command("  ", Page("a.png", TestJobs.Png())), OcrErrorCodes.FileNameRequired },
        { Command("../etc/passwd", Page("a.png", TestJobs.Png())), OcrErrorCodes.FileNameInvalid },
        { Command("tab\there", Page("a.png", TestJobs.Png())), OcrErrorCodes.FileNameInvalid },
        { Command(new string('n', OcrRules.MaxFileNameLength), Page("a.png", TestJobs.Png())), OcrErrorCodes.FileNameTooLong },
    };

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task Invalid_requests_are_rejected_before_anything_is_read_or_sent(UploadOcrPagesCommand command, string expectedCode)
    {
        var result = await Handler.HandleAsync(command, Ct);

        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.Empty(_composer.Calls);
        Assert.Empty(_api.Submitted);
    }

    [Fact]
    public async Task A_page_whose_content_does_not_match_its_extension_is_rejected()
    {
        var result = await Handler.HandleAsync(Command("scan", Page("a.png", TestJobs.Png()), Page("b.png", TestJobs.Jpeg())), Ct);

        Assert.Equal(OcrErrors.ContentMismatch, result.Error);
        Assert.Empty(_composer.Calls);
    }

    [Fact]
    public async Task Pages_larger_together_than_the_limit_are_rejected_even_if_reported_small()
    {
        var half = (int)(OcrRules.MaxSizeBytes / 2) + 1;

        var result = await Handler.HandleAsync(
            Command("scan", new OcrPageFile("a.png", 10, new MemoryStream(TestJobs.Png(half))), new OcrPageFile("b.png", 10, new MemoryStream(TestJobs.Png(half)))), Ct);

        Assert.Equal(OcrErrors.TooLarge, result.Error);
        Assert.Empty(_composer.Calls);
    }

    [Fact]
    public async Task An_image_that_cannot_become_a_page_is_reported_and_nothing_is_sent()
    {
        _composer.Result = OcrErrors.ImageNotSupported;

        var result = await Handler.HandleAsync(Command("scan", Page("a.png", TestJobs.Png())), Ct);

        Assert.Equal(OcrErrors.ImageNotSupported, result.Error);
        Assert.Empty(_api.Submitted);
    }

    private static UploadOcrPagesCommand Command(string name, params OcrPageFile[] pages) =>
        new(name, pages);

    private static OcrPageFile Page(string fileName, byte[] content) => new(fileName, content.Length, new MemoryStream(content));
}
