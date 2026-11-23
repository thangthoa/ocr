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

    /// <summary>
    /// What <c>download?format=docx_layout</c> returns; null answers 404 like a job without a result. The other
    /// formats always answer 404, so a test sees which Word file the app asked for.
    /// </summary>
    public byte[]? LayoutDocx { get; set; }

    /// <summary>Formats asked for, in order.</summary>
    public List<OcrResultFormat> Downloads { get; } = [];

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

    /// <summary>The job as the "service" has it now.</summary>
    public OcrJob OnService(string id) => _service[id];

    public async Task<Result<OcrJob>> SubmitAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        if (Unavailable)
            return Down;
        await content.CopyToAsync(Stream.Null, cancellationToken);
        var job = NewJob($"job_{++_next:D16}", fileName, OcrJobStatus.Queued);
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

    public Task<Result<FileContent>> DownloadAsync(OcrJobId jobId, OcrResultFormat format, CancellationToken cancellationToken)
    {
        Downloads.Add(format);
        return Task.FromResult<Result<FileContent>>(LayoutDocx is { } docx && format == OcrResultFormat.DocxLayout
            ? new FileContent(new MemoryStream(docx, writable: false), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "result_layout.docx")
            : Error.NotFound("not_found", "no result"));
    }

    /// <summary>Page changes received, as (method, page or position, file names).</summary>
    public List<(string Method, int? Page, IReadOnlyList<string> Files)> PageEdits { get; } = [];

    /// <summary>When set, page changes fail with this error (for example <c>job_busy</c>).</summary>
    public Error? PageEditError { get; set; }

    public Task<Result<OcrJob>> InsertPagesAsync(OcrJobId jobId, IReadOnlyList<OcrUpload> files, int? position, CancellationToken cancellationToken) =>
        PageEdit("insert", jobId, position, files, job => job with
        {
            Status = OcrJobStatus.Queued,
            PagesTotal = job.PagesTotal + files.Count,
            PagesDone = 0,
            Pages = [],
            FinishedAtUtc = null,
        });

    public Task<Result<OcrJob>> ReplacePageAsync(OcrJobId jobId, int pageNumber, OcrUpload file, CancellationToken cancellationToken) =>
        PageEdit("replace", jobId, pageNumber, [file], job => job with { Status = OcrJobStatus.Queued, PagesDone = 0, Pages = [], FinishedAtUtc = null });

    /// <summary>Like the service: later pages move up at once, and the Word file is rebuilt (not ready yet).</summary>
    public Task<Result<OcrJob>> DeletePageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken) =>
        PageEdit("delete", jobId, pageNumber, [], job => job with
        {
            PagesTotal = job.PagesTotal - 1,
            PagesDone = Math.Max(0, job.PagesDone - 1),
            Pages = [.. job.Pages.Where(p => p.PageNumber != pageNumber).Select(p => p.PageNumber > pageNumber ? p with { PageNumber = p.PageNumber - 1 } : p)],
            WordReady = job.Status != OcrJobStatus.Done,
            Duplicates = [.. job.Duplicates.Where(d => d.FirstPage != pageNumber && d.SecondPage != pageNumber)],
        });

    private Task<Result<OcrJob>> PageEdit(string method, OcrJobId jobId, int? page, IReadOnlyList<OcrUpload> files, Func<OcrJob, OcrJob> change)
    {
        if (Unavailable)
            return Task.FromResult<Result<OcrJob>>(Down);
        PageEdits.Add((method, page, [.. files.Select(f => f.FileName)]));
        if (PageEditError is { } error)
            return Task.FromResult<Result<OcrJob>>(error);

        var current = _service[jobId.Value];
        var job = change(current) with { Edits = current.Edits + 1, UpdatedAtUtc = DateTime.UtcNow };
        _service[jobId.Value] = job;
        return Task.FromResult<Result<OcrJob>>(job);
    }

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
        new(OcrJobId.Parse(id), fileName, status, 1, 0, null, null, null, null, [], null, null);
}
