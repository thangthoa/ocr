using Modules.Ocr.Application.Features.ListOcrJobs;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.UnitTests;

public sealed class SearchJobsTests
{
    private readonly FakeApi _api = new();
    private readonly FakeRegistry _registry = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ListOcrJobsHandler Handler => new(_api, _registry);

    [Fact]
    public async Task Searches_file_names_across_service_pages_case_insensitively()
    {
        // 450 jobs = three service pages of 200; matches sit on the first and the last page.
        _api.Catalog = [.. Enumerable.Range(1, 450).Select(n => Job(n, n is 5 or 420 ? $"Contract-{n}.PDF" : $"scan-{n}.png"))];

        var result = await Handler.HandleAsync(new ListOcrJobsQuery(Search: "contract"), Ct);

        Assert.Equal(["Contract-5.PDF", "Contract-420.PDF"], result.Value.Jobs.Items.Select(j => j.FileName));
        Assert.Equal(3, _api.ListCalls);
        Assert.False(result.Value.SearchLimitReached);
    }

    [Fact]
    public async Task Search_results_are_paged_and_combined_with_the_status_filter()
    {
        _api.Catalog = [.. Enumerable.Range(1, 30).Select(n => Job(n, $"doc-{n}.png", n % 2 == 0 ? OcrJobStatus.Done : OcrJobStatus.Failed))];

        var result = await Handler.HandleAsync(new ListOcrJobsQuery(Page: 2, PageSize: 10, Status: OcrJobStatus.Done, Search: "doc"), Ct);

        Assert.Equal(15, result.Value.Jobs.TotalCount);
        Assert.Equal(5, result.Value.Jobs.Items.Count);
        Assert.All(result.Value.Jobs.Items, j => Assert.Equal(OcrJobStatus.Done, j.Status));
    }

    [Fact]
    public async Task Exact_job_id_is_looked_up_directly()
    {
        var job = Job(7, "a.png");
        _api.Jobs[job.Id.Value] = job;

        var result = await Handler.HandleAsync(new ListOcrJobsQuery(Search: $"  {job.Id.Value} "), Ct);

        Assert.Equal(job.Id, Assert.Single(result.Value.Jobs.Items).Id);
        Assert.Equal(0, _api.ListCalls);
    }

    [Fact]
    public async Task Unknown_job_id_gives_an_empty_result()
    {
        var result = await Handler.HandleAsync(new ListOcrJobsQuery(Search: "job_ffffffffffffffff"), Ct);

        Assert.Empty(result.Value.Jobs.Items);
    }

    [Fact]
    public async Task Search_stops_after_the_scan_limit_and_says_so()
    {
        _api.Catalog = [.. Enumerable.Range(1, ListOcrJobsQuery.MaxJobsScannedBySearch + 500).Select(n => Job(n, "scan.png"))];

        var result = await Handler.HandleAsync(new ListOcrJobsQuery(Search: "nothing-matches"), Ct);

        Assert.True(result.Value.SearchLimitReached);
        Assert.Equal(ListOcrJobsQuery.MaxJobsScannedBySearch / 200, _api.ListCalls);
    }

    [Fact]
    public async Task Search_uses_the_in_memory_list_when_the_service_cannot_list()
    {
        _registry.Add(Job(1, "invoice.png"));
        _registry.Add(Job(2, "letter.png"));

        var result = await Handler.HandleAsync(new ListOcrJobsQuery(Search: "INVOICE"), Ct);

        Assert.False(result.Value.FromService);
        Assert.Equal("invoice.png", Assert.Single(result.Value.Jobs.Items).FileName);
    }

    private static OcrJob Job(int n, string fileName, OcrJobStatus status = OcrJobStatus.Done) =>
        TestJobs.Job($"job_{n:D16}", status) with { FileName = fileName };
}
