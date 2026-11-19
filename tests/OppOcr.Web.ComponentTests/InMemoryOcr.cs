using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.ComponentTests;

/// <summary>Stand-in OCR service plus in-memory job list so the real handlers run inside component tests.</summary>
internal sealed class InMemoryOcr : IOcrApiClient, IOcrJobRegistry
{
    private readonly Dictionary<string, OcrJob> _service = [];
    private readonly List<OcrJob> _registry = [];
    private int _next;

    public List<string> Cancelled { get; } = [];

    /// <summary>Makes every call fail as if the OCR service could not be reached.</summary>
    public bool Unavailable { get; set; }

    private static Error Down => Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "OCR service unreachable");

    public IReadOnlyList<OcrJob> Registry => _registry;

    /// <summary>Puts a job on the "service" and in the app's list, as if uploaded through the app.</summary>
    public OcrJob Seed(OcrJob job)
    {
        _service[job.Id.Value] = job;
        _registry.Add(job);
        return job;
    }

    public void SetOnService(OcrJob job) => _service[job.Id.Value] = job;

    public async Task<Result<OcrJob>> SubmitAsync(Stream content, string fileName, OcrDocumentType documentType, CancellationToken cancellationToken)
    {
        await content.CopyToAsync(Stream.Null, cancellationToken);
        var job = NewJob($"job_{++_next:D16}", fileName, OcrJobStatus.Queued) with { DocumentType = documentType };
        _service[job.Id.Value] = job;
        return job;
    }

    public Task<Result<OcrJob>> GetAsync(OcrJobId jobId, CancellationToken cancellationToken)
    {
        if (Unavailable)
            return Task.FromResult<Result<OcrJob>>(Down);
        return Task.FromResult<Result<OcrJob>>(_service.TryGetValue(jobId.Value, out var job) ? job : Error.NotFound("not_found", "job not found"));
    }

    public Task<Result<PagedResult<OcrJob>>> ListAsync(int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PagedResult<OcrJob>>>(Unavailable ? Down : Error.Validation(OcrServiceErrorCodes.ListNotSupported, "no list"));

    public Task<Result> CancelAsync(OcrJobId jobId, CancellationToken cancellationToken)
    {
        Cancelled.Add(jobId.Value);
        _service[jobId.Value] = _service[jobId.Value] with { Status = OcrJobStatus.Cancelled };
        return Task.FromResult(Result.Success());
    }

    public Task<Result<FileContent>> GetPageImageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<FileContent>> DownloadAsync(OcrJobId jobId, OcrResultFormat format, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    void IOcrJobRegistry.Add(OcrJob job) => _registry.Add(job);

    OcrJob? IOcrJobRegistry.Update(OcrJob job)
    {
        var index = _registry.FindIndex(j => j.Id == job.Id);
        if (index < 0)
            return null;
        var previous = _registry[index];
        _registry[index] = job;
        return previous;
    }

    bool IOcrJobRegistry.Remove(OcrJobId jobId) => _registry.RemoveAll(j => j.Id == jobId) > 0;

    OcrJob? IOcrJobRegistry.Find(OcrJobId jobId) => _registry.Find(j => j.Id == jobId);

    IReadOnlyList<OcrJob> IOcrJobRegistry.Unfinished() => [.. _registry.Where(j => !j.IsFinished)];

    PagedResult<OcrJob> IOcrJobRegistry.List(int page, int pageSize, OcrJobStatus? status)
    {
        var items = _registry.Where(j => status is null || j.Status == status).Reverse().ToList();
        return new PagedResult<OcrJob>(items, page, pageSize, items.Count);
    }

    public static OcrJob NewJob(string id, string fileName, OcrJobStatus status) =>
        new(OcrJobId.Parse(id), fileName, OcrDocumentType.Printed, status, 1, 0, null, null, null, null, [], null, null);
}
