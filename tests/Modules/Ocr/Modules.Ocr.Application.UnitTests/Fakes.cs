using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.UnitTests;

internal sealed class FakeApi : IOcrApiClient
{
    private int _next;

    public List<(string FileName, OcrDocumentType Type, byte[] Bytes)> Submitted { get; } = [];

    public List<string> Cancelled { get; } = [];

    public Func<Result<OcrJob>>? SubmitResult { get; set; }

    public Dictionary<string, Result<OcrJob>> Jobs { get; } = [];

    public Result<PagedResult<OcrJob>> ListResult { get; set; } =
        Error.Validation(OcrServiceErrorCodes.ListNotSupported, "no list");

    public Result CancelResult { get; set; } = Result.Success();

    /// <summary>When set, <see cref="ListAsync"/> pages through these jobs like the service does.</summary>
    public IReadOnlyList<OcrJob>? Catalog { get; set; }

    public int ListCalls { get; private set; }

    public async Task<Result<OcrJob>> SubmitAsync(Stream content, string fileName, OcrDocumentType documentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Submitted.Add((fileName, documentType, buffer.ToArray()));
        if (SubmitResult is not null)
            return SubmitResult();

        var job = TestJobs.Job($"job_{++_next:D16}", OcrJobStatus.Queued) with { FileName = fileName, DocumentType = documentType, QueuePosition = _next };
        Jobs[job.Id.Value] = job;
        return job;
    }

    public Task<Result<OcrJob>> GetAsync(OcrJobId jobId, CancellationToken cancellationToken) =>
        Task.FromResult(Jobs.TryGetValue(jobId.Value, out var result) ? result : Error.NotFound("not_found", "job not found"));

    public Task<Result<PagedResult<OcrJob>>> ListAsync(int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken)
    {
        ListCalls++;
        if (Catalog is null)
            return Task.FromResult(ListResult);

        var matching = Catalog.Where(j => status is null || j.Status == status).ToList();
        var items = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(Result.Success(new PagedResult<OcrJob>(items, page, pageSize, matching.Count)));
    }

    public Task<Result> CancelAsync(OcrJobId jobId, CancellationToken cancellationToken)
    {
        Cancelled.Add(jobId.Value);
        return Task.FromResult(CancelResult);
    }

    public Task<Result<FileContent>> GetPageImageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new FileContent(new MemoryStream([1]), "image/png", "p")));

    public Task<Result<FileContent>> DownloadAsync(OcrJobId jobId, OcrResultFormat format, CancellationToken cancellationToken) =>
        Task.FromResult<Result<FileContent>>(Error.Conflict("not_ready", "not finished"));
}

internal sealed class FakeRegistry : IOcrJobRegistry
{
    private readonly List<OcrJob> _jobs = [];

    public IReadOnlyList<OcrJob> All => _jobs;

    public void Add(OcrJob job) => _jobs.Add(job);

    public OcrJob? Update(OcrJob job)
    {
        var index = _jobs.FindIndex(j => j.Id == job.Id);
        if (index < 0)
            return null;
        var previous = _jobs[index];
        _jobs[index] = job;
        return previous;
    }

    public bool Remove(OcrJobId jobId) => _jobs.RemoveAll(j => j.Id == jobId) > 0;

    public OcrJob? Find(OcrJobId jobId) => _jobs.Find(j => j.Id == jobId);

    public IReadOnlyList<OcrJob> Unfinished() => [.. _jobs.Where(j => !j.IsFinished)];

    public PagedResult<OcrJob> List(int page, int pageSize, OcrJobStatus? status) =>
        new([.. _jobs.Where(j => status is null || j.Status == status)], page, pageSize, _jobs.Count);
}

internal sealed class FakeNotifier : IOcrJobNotifier
{
    public List<OcrJobChange> Changes { get; } = [];

    public Task NotifyAsync(OcrJobChange change, CancellationToken cancellationToken)
    {
        Changes.Add(change);
        return Task.CompletedTask;
    }
}

internal static class TestJobs
{
    public static OcrJob Job(string id, OcrJobStatus status) =>
        new(OcrJobId.Parse(id), "a.png", OcrDocumentType.Printed, status, 1, 0, null, null, null, null, [], null, null);

    public static byte[] Png(int size = 64)
    {
        var bytes = new byte[size];
        ((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).CopyTo(bytes);
        return bytes;
    }

    public static byte[] Pdf() => "%PDF-1.4\n%%EOF"u8.ToArray();
}
