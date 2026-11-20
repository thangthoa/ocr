using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.GetOcrSourceFile;
using Modules.Ocr.Application.Features.ReceiveOcrJobCallback;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.UnitTests;

public sealed class ReceiveOcrJobCallbackHandlerTests
{
    private static readonly OcrCallback AnyCallback = new(new byte[] { 1 }, "1", "sha256=x");

    private readonly FakeRegistry _registry = new();
    private readonly FakeCallbackReader _reader = new();

    private ReceiveOcrJobCallbackHandler Handler => new(_reader, _registry, NullLogger<ReceiveOcrJobCallbackHandler>.Instance);

    [Fact]
    public void Finished_job_replaces_the_tracked_one_and_is_announced()
    {
        _registry.Add(TestJobs.Job("job_a1", OcrJobStatus.Running));
        _reader.Next = Done("job_a1");

        var result = Handler.Handle(new ReceiveOcrJobCallbackCommand(AnyCallback));

        var change = result.Value!;
        Assert.Equal((OcrJobStatus.Done, OcrJobChangeKind.Updated), (change.Status, change.Kind));
        Assert.Equal(OcrJobStatus.Done, Assert.Single(_registry.All).Status);
    }

    [Fact]
    public void Failed_job_is_announced_with_its_error_code()
    {
        _registry.Add(TestJobs.Job("job_a1", OcrJobStatus.Running));
        _reader.Next = TestJobs.Job("job_a1", OcrJobStatus.Failed) with { ErrorCode = "bad_image" };

        var change = Handler.Handle(new ReceiveOcrJobCallbackCommand(AnyCallback)).Value!;

        Assert.Equal((OcrJobStatus.Failed, "bad_image"), (change.Status, change.ErrorCode));
    }

    [Fact]
    public void Repeated_callback_overwrites_without_a_second_announcement()
    {
        _registry.Add(TestJobs.Job("job_a1", OcrJobStatus.Running));
        _reader.Next = Done("job_a1");
        Handler.Handle(new ReceiveOcrJobCallbackCommand(AnyCallback));

        var again = Handler.Handle(new ReceiveOcrJobCallbackCommand(AnyCallback));

        Assert.True(again.IsSuccess);
        Assert.Null(again.Value);
        Assert.Single(_registry.All);
    }

    [Fact]
    public void Job_submitted_before_a_restart_is_tracked_again()
    {
        _reader.Next = Done("job_b2");

        var result = Handler.Handle(new ReceiveOcrJobCallbackCommand(AnyCallback));

        Assert.Equal("job_b2", Assert.Single(_registry.All).Id.Value);
        Assert.Equal(OcrJobStatus.Done, result.Value!.Status);
    }

    [Fact]
    public void Rejected_callback_changes_nothing()
    {
        _registry.Add(TestJobs.Job("job_a1", OcrJobStatus.Running));
        _reader.Next = Error.Unauthorized(OcrServiceErrorCodes.CallbackRejected, "bad signature");

        var result = Handler.Handle(new ReceiveOcrJobCallbackCommand(AnyCallback));

        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        Assert.Equal(OcrJobStatus.Running, Assert.Single(_registry.All).Status);
    }

    [Fact]
    public void Source_file_link_that_no_longer_exists_is_not_found()
    {
        var files = new FakeSourceFiles();
        var handler = new GetOcrSourceFileHandler(files);
        files.Files["t1"] = new FileContent(new MemoryStream([1]), "image/png", "a.png");

        Assert.Equal("a.png", handler.Handle(new GetOcrSourceFileQuery("t1")).Value.FileName);
        Assert.Equal(OcrErrors.NotFound, handler.Handle(new GetOcrSourceFileQuery("t2")).Error);
    }

    private static OcrJob Done(string id) =>
        TestJobs.Job(id, OcrJobStatus.Done) with
        {
            PagesDone = 1,
            Pages = [new OcrPage(1, OcrDocumentType.Printed, "نص", 0, string.Empty, [], false)],
        };

    private sealed class FakeCallbackReader : IOcrCallbackReader
    {
        public Result<OcrJob> Next { get; set; } = Error.Validation("unset", "no job set");

        public Result<OcrJob> Read(OcrCallback callback) => Next;
    }

    private sealed class FakeSourceFiles : IOcrSourceFiles
    {
        public Dictionary<string, FileContent> Files { get; } = [];

        public FileContent? Open(string token) => Files.GetValueOrDefault(token);
    }
}
