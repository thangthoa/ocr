using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Domain.UnitTests;

public sealed class OcrJobTests
{
    [Theory]
    [InlineData("job_3f9c2a1b7d4e5f60")]
    [InlineData("job_b687f8826140409e")]
    public void Valid_job_ids_parse(string value)
    {
        Assert.True(OcrJobId.TryParse(value, out var id));
        Assert.Equal(value, id.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3f9c2a1b7d4e5f60")]
    [InlineData("job_")]
    [InlineData("job_../../v1/health")]
    [InlineData("job_abc?api_key=x")]
    [InlineData("job_abc/download")]
    public void Ids_that_could_alter_the_request_path_are_rejected(string? value) =>
        Assert.False(OcrJobId.TryParse(value, out _));

    [Theory]
    [InlineData(OcrJobStatus.Queued, true)]
    [InlineData(OcrJobStatus.Running, false)]
    [InlineData(OcrJobStatus.Done, false)]
    public void Only_queued_jobs_can_be_cancelled(OcrJobStatus status, bool expected) =>
        Assert.Equal(expected, Job(status).CanCancel);

    [Theory]
    [InlineData(OcrJobStatus.Done)]
    [InlineData(OcrJobStatus.Failed)]
    [InlineData(OcrJobStatus.Cancelled)]
    public void Terminal_statuses_are_finished(OcrJobStatus status) => Assert.True(Job(status).IsFinished);

    [Fact]
    public void Visible_change_detection_ignores_identical_state_and_sees_progress()
    {
        var running = Job(OcrJobStatus.Running) with { CurrentStep = "page 1/2: reading (view 1/4)" };

        Assert.True(running.HasVisibleChangesSince(null));
        Assert.False(running.HasVisibleChangesSince(running with { }));
        Assert.True((running with { CurrentStep = "page 1/2: reading (view 2/4)" }).HasVisibleChangesSince(running));
        Assert.True((running with { Status = OcrJobStatus.Done }).HasVisibleChangesSince(running));
    }

    private static OcrJob Job(OcrJobStatus status) =>
        new(OcrJobId.Parse("job_0123456789abcdef"), "a.png", OcrDocumentType.Printed, status, 2, 0, null, null, null, null, [], null, null);
}
