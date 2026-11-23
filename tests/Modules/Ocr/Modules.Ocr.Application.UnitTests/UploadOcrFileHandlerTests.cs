using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.UnitTests;

public sealed class UploadOcrFileHandlerTests
{
    private readonly FakeApi _api = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeNotifier _notifier = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UploadOcrFileHandler Handler => new(new UploadOcrFileValidator(), _api, _registry, _notifier, NullLogger<UploadOcrFileHandler>.Instance);

    [Fact]
    public async Task Valid_file_is_sent_to_the_service_immediately_and_tracked()
    {
        var bytes = TestJobs.Pdf();

        var result = await Handler.HandleAsync(new UploadOcrFileCommand("giay-to.pdf", bytes.Length, new MemoryStream(bytes)), Ct);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(_api.Submitted);
        Assert.Equal("giay-to.pdf", sent.FileName);
        Assert.Equal(bytes, sent.Bytes);
        Assert.Equal(result.Value.Id, Assert.Single(_registry.All).Id);
        Assert.Equal(OcrJobChangeKind.Created, Assert.Single(_notifier.Changes).Kind);
    }

    [Theory]
    [InlineData("a.gif", OcrErrorCodes.UnsupportedFormat)]
    [InlineData("a.pdf", OcrErrorCodes.ContentMismatch)]
    public async Task Bad_files_never_reach_the_service(string fileName, string expectedCode)
    {
        var result = await Handler.HandleAsync(new UploadOcrFileCommand(fileName, 64, new MemoryStream(TestJobs.Png())), Ct);

        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.Empty(_api.Submitted);
    }

    [Fact]
    public async Task Stream_longer_than_limit_is_rejected_even_if_reported_small()
    {
        var result = await Handler.HandleAsync(
            new UploadOcrFileCommand("big.png", 10, new MemoryStream(TestJobs.Png((int)OcrRules.MaxSizeBytes + 1))), Ct);

        Assert.Equal(OcrErrors.TooLarge, result.Error);
        Assert.Empty(_api.Submitted);
    }

    [Fact]
    public async Task Service_rejection_keeps_the_service_code_for_the_user()
    {
        _api.SubmitResult = () => Error.Validation("too_many_pages", "PDF has 30 pages");

        var result = await Handler.HandleAsync(new UploadOcrFileCommand("big.pdf", 15, new MemoryStream(TestJobs.Pdf())), Ct);

        Assert.Equal("too_many_pages", result.Error?.Code);
        Assert.Empty(_registry.All);
        Assert.Empty(_notifier.Changes);
    }

    [Fact]
    public async Task Unreachable_service_is_reported_as_unavailable()
    {
        _api.SubmitResult = () => Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "down");

        var result = await Handler.HandleAsync(new UploadOcrFileCommand("a.png", 64, new MemoryStream(TestJobs.Png())), Ct);

        Assert.Equal(OcrErrorCodes.ServiceUnavailable, result.Error?.Code);
    }
}
