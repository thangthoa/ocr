using Modules.Ocr.Domain.Jobs;
using Modules.Ocr.Infrastructure.Registry;

namespace Modules.Ocr.Infrastructure.Tests;

public sealed class InMemoryOcrJobRegistryTests
{
    [Fact]
    public void Lists_newest_first_and_filters_by_status()
    {
        var registry = new InMemoryOcrJobRegistry(capacity: 10);
        registry.Add(Job(1, OcrJobStatus.Done));
        registry.Add(Job(2, OcrJobStatus.Running));
        registry.Add(Job(3, OcrJobStatus.Queued));

        Assert.Equal([3, 2, 1], registry.List(1, 10, null).Items.Select(Number));
        Assert.Equal([2], registry.List(1, 10, OcrJobStatus.Running).Items.Select(Number));
        Assert.Equal([3, 2], registry.Unfinished().Select(Number).Order().Reverse());
    }

    [Fact]
    public void Update_only_applies_to_tracked_jobs_and_returns_previous_state()
    {
        var registry = new InMemoryOcrJobRegistry(capacity: 10);
        registry.Add(Job(1, OcrJobStatus.Queued));

        Assert.Equal(OcrJobStatus.Queued, registry.Update(Job(1, OcrJobStatus.Running))!.Status);
        Assert.Null(registry.Update(Job(2, OcrJobStatus.Running)));
        Assert.Equal(OcrJobStatus.Running, registry.Find(Job(1, OcrJobStatus.Done).Id)!.Status);
    }

    [Fact]
    public void Capacity_evicts_oldest_finished_job_before_unfinished_ones()
    {
        var registry = new InMemoryOcrJobRegistry(capacity: 2);
        registry.Add(Job(1, OcrJobStatus.Running));
        registry.Add(Job(2, OcrJobStatus.Done));
        registry.Add(Job(3, OcrJobStatus.Queued));

        Assert.Equal([3, 1], registry.List(1, 10, null).Items.Select(Number));
    }

    private static OcrJob Job(int n, OcrJobStatus status) =>
        new(OcrJobId.Parse($"job_{n:D16}"), $"f{n}.png", OcrDocumentType.Printed, status, 1, 0, null, null, null, null, [], null, null);

    private static int Number(OcrJob job) => int.Parse(job.Id.Value[4..], System.Globalization.CultureInfo.InvariantCulture);
}
