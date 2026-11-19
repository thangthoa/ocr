using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Application.Features.ListOcrJobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.Features.Mockups.Data;

/// <param name="Documents">The newest OCR jobs as documents, followed by the samples.</param>
/// <param name="JobsFromService">False while the OCR service cannot list jobs (this instance's own list is used).</param>
/// <param name="JobsTotal">How many jobs exist; more than <see cref="DocumentCatalog.MaxJobs"/> means only the newest are shown.</param>
/// <param name="JobsError">Why OCR jobs could not be loaded; the samples are still returned.</param>
public sealed record DocumentList(
    IReadOnlyList<DocumentRecord> Documents,
    bool JobsFromService,
    int JobsTotal,
    Error? JobsError);

/// <summary>
/// Documents = OCR jobs (through the OCR module's use cases) + what <see cref="DocumentStore"/> keeps
/// for them + the samples. The OCR module stays unaware of documents.
/// </summary>
public sealed class DocumentCatalog(
    ListOcrJobsHandler listJobs,
    GetOcrJobHandler getJob,
    DeleteOcrJobHandler deleteJob,
    DocumentStore store,
    AuditLog audit)
{
    /// <summary>
    /// The newest jobs are filtered here, in memory, because metadata only exists in this app.
    /// One page of the service's list keeps that to a single request.
    /// </summary>
    public const int MaxJobs = ListOcrJobsQuery.MaxPageSize;

    public async Task<DocumentList> ListAsync(CancellationToken cancellationToken)
    {
        var samples = store.Samples();
        var result = await listJobs.HandleAsync(new ListOcrJobsQuery(1, MaxJobs), cancellationToken);
        if (result.IsFailure)
            return new DocumentList(samples, JobsFromService: true, 0, result.Error);

        var jobs = result.Value.Jobs;
        var documents = jobs.Items.Select(store.FromJob).Concat(samples).ToList();
        return new DocumentList(documents, result.Value.FromService, jobs.TotalCount, null);
    }

    public async Task<Result<DocumentRecord>> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (store.FindSample(id) is { } sample)
            return sample;

        var result = await getJob.HandleAsync(new GetOcrJobQuery(id), cancellationToken);
        if (result.IsFailure)
            return result.Error!;
        return store.FromJob(result.Value);
    }

    /// <summary>Removes an OCR job (cancelling it if it is still queued) and what was stored for it.</summary>
    public async Task<Result> DeleteAsync(string id, string? fileName, string author, CancellationToken cancellationToken)
    {
        var result = await deleteJob.HandleAsync(new DeleteOcrJobCommand(id), cancellationToken);
        if (result.IsSuccess)
        {
            store.Forget(id);
            audit.Record(author, AuditAction.Deleted, id, fileName);
        }

        return result;
    }
}
