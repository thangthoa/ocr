using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.DownloadOcrResult;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Application.Features.ListOcrJobs;
using Modules.Ocr.Application.Features.SyncTrackedOcrJobs;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.UnitTests;

public sealed class JobHandlersTests
{
    private const string Id = "job_0000000000000001";
    private readonly FakeApi _api = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeNotifier _notifier = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_reads_from_the_service_and_refreshes_the_tracked_copy()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Queued));
        _api.Jobs[Id] = TestJobs.Job(Id, OcrJobStatus.Running);

        var result = await new GetOcrJobHandler(_api, _registry).HandleAsync(new GetOcrJobQuery(Id), Ct);

        Assert.Equal(OcrJobStatus.Running, result.Value.Status);
        Assert.Equal(OcrJobStatus.Running, _registry.All[0].Status);
    }

    [Theory]
    [InlineData("not-a-job")]
    [InlineData("job_9999999999999999")]
    public async Task Get_unknown_or_malformed_id_is_not_found(string id) =>
        Assert.Equal(OcrErrors.NotFound, (await new GetOcrJobHandler(_api, _registry).HandleAsync(new GetOcrJobQuery(id), Ct)).Error);

    [Fact]
    public async Task List_uses_the_service_when_it_supports_listing()
    {
        _api.ListResult = new PagedResult<OcrJob>([TestJobs.Job(Id, OcrJobStatus.Done)], 1, 20, 1);

        var result = await new ListOcrJobsHandler(_api, _registry).HandleAsync(new ListOcrJobsQuery(), Ct);

        Assert.True(result.Value.FromService);
        Assert.Single(result.Value.Jobs.Items);
    }

    [Fact]
    public async Task List_falls_back_to_jobs_sent_from_this_app_until_the_service_can_list()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Queued));

        var result = await new ListOcrJobsHandler(_api, _registry).HandleAsync(new ListOcrJobsQuery(), Ct);

        Assert.False(result.Value.FromService);
        Assert.Equal(Id, Assert.Single(result.Value.Jobs.Items).Id.Value);
    }

    [Fact]
    public async Task Delete_cancels_a_queued_job_and_removes_it_from_the_list()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Queued));
        _api.Jobs[Id] = TestJobs.Job(Id, OcrJobStatus.Queued);

        var result = await Deleter.HandleAsync(new DeleteOcrJobCommand(Id), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([Id], _api.Cancelled);
        Assert.Empty(_registry.All);
        Assert.Equal(OcrJobChangeKind.Deleted, Assert.Single(_notifier.Changes).Kind);
    }

    [Fact]
    public async Task Delete_does_not_try_to_cancel_a_running_job()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Running));
        _api.Jobs[Id] = TestJobs.Job(Id, OcrJobStatus.Running);

        await Deleter.HandleAsync(new DeleteOcrJobCommand(Id), Ct);

        Assert.Empty(_api.Cancelled);
        Assert.Empty(_registry.All);
    }

    [Fact]
    public async Task Delete_keeps_the_job_when_the_service_cannot_be_reached()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Queued));
        _api.Jobs[Id] = Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "down");

        var result = await Deleter.HandleAsync(new DeleteOcrJobCommand(Id), Ct);

        Assert.Equal(OcrErrorCodes.ServiceUnavailable, result.Error?.Code);
        Assert.Single(_registry.All);
    }

    [Fact]
    public async Task Download_before_done_is_not_ready() =>
        Assert.Equal(OcrErrors.NotReady, (await new DownloadOcrResultHandler(_api).HandleAsync(new DownloadOcrResultQuery(Id, OcrResultFormat.Docx), Ct)).Error);

    [Fact]
    public async Task Sync_announces_changes_once()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Queued));
        _api.Jobs[Id] = TestJobs.Job(Id, OcrJobStatus.Running) with { CurrentStep = "page 1/1: reading (view 1/4)" };

        Assert.Equal(1, await Syncer.HandleAsync(Ct));
        Assert.Equal(0, await Syncer.HandleAsync(Ct));
        Assert.Equal(OcrJobStatus.Running, Assert.Single(_notifier.Changes).Status);
    }

    [Fact]
    public async Task Sync_stops_tracking_finished_jobs()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Running));
        _api.Jobs[Id] = TestJobs.Job(Id, OcrJobStatus.Done);

        await Syncer.HandleAsync(Ct);

        Assert.Empty(_registry.Unfinished());
        Assert.Equal(OcrJobStatus.Done, _registry.All[0].Status);
    }

    [Fact]
    public async Task Sync_marks_a_job_the_service_lost_as_failed()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Running));

        await Syncer.HandleAsync(Ct);

        Assert.Equal(OcrServiceErrorCodes.RemoteJobLost, _registry.All[0].ErrorCode);
        Assert.Equal(OcrJobStatus.Failed, Assert.Single(_notifier.Changes).Status);
    }

    [Fact]
    public async Task Sync_ignores_temporary_outages()
    {
        _registry.Add(TestJobs.Job(Id, OcrJobStatus.Running));
        _api.Jobs[Id] = Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "down");

        await Syncer.HandleAsync(Ct);

        Assert.Equal(OcrJobStatus.Running, _registry.All[0].Status);
        Assert.Empty(_notifier.Changes);
    }

    private DeleteOcrJobHandler Deleter => new(_api, _registry, _notifier, NullLogger<DeleteOcrJobHandler>.Instance);

    private SyncTrackedOcrJobsHandler Syncer => new(_api, _registry, _notifier, NullLogger<SyncTrackedOcrJobsHandler>.Instance);
}
