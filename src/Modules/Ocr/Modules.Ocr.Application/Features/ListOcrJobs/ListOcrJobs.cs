using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.ListOcrJobs;

/// <param name="Search">Matches a file name (contains, case-insensitive) or a job id.</param>
public sealed record ListOcrJobsQuery(
    int Page = 1,
    int PageSize = ListOcrJobsQuery.DefaultPageSize,
    OcrJobStatus? Status = null,
    string? Search = null)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    /// <summary>
    /// The service cannot search, so a text search scans the newest jobs page by page. Bounded so
    /// one keystroke cannot trigger an unbounded number of requests.
    /// </summary>
    public const int MaxJobsScannedBySearch = 1000;

    /// <summary>Largest page the service accepts on <c>GET /v1/jobs</c>.</summary>
    internal const int ServicePageSize = 200;
}

public sealed class ListOcrJobsHandler(IOcrApiClient api, IOcrJobRegistry registry)
{
    public async Task<Result<OcrJobList>> HandleAsync(ListOcrJobsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, ListOcrJobsQuery.MaxPageSize);
        var term = query.Search?.Trim();

        if (string.IsNullOrEmpty(term))
            return await ListAsync(page, pageSize, query.Status, cancellationToken);

        return OcrJobId.TryParse(term, out var id)
            ? await FindByIdAsync(id, query.Status, pageSize, cancellationToken)
            : await SearchAsync(term, page, pageSize, query.Status, cancellationToken);
    }

    private async Task<Result<OcrJobList>> ListAsync(int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken)
    {
        var result = await api.ListAsync(page, pageSize, status, cancellationToken);
        if (result.IsSuccess)
            return new OcrJobList(result.Value, FromService: true);

        // Older service versions cannot list jobs: show what this application submitted since it started.
        if (result.Error!.Code == OcrServiceErrorCodes.ListNotSupported)
            return new OcrJobList(registry.List(page, pageSize, status), FromService: false);

        return OcrErrors.ServiceUnavailable(result.Error);
    }

    private async Task<Result<OcrJobList>> FindByIdAsync(OcrJobId id, OcrJobStatus? status, int pageSize, CancellationToken cancellationToken)
    {
        var result = await api.GetAsync(id, cancellationToken);
        if (result.IsFailure && result.Error!.Type != ErrorType.NotFound)
            return OcrErrors.ServiceUnavailable(result.Error);

        IReadOnlyList<OcrJob> items = result.IsSuccess && (status is null || result.Value.Status == status) ? [result.Value] : [];
        return new OcrJobList(new PagedResult<OcrJob>(items, 1, pageSize, items.Count), FromService: true);
    }

    private async Task<Result<OcrJobList>> SearchAsync(string term, int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken)
    {
        var matches = new List<OcrJob>();
        var scanned = 0;
        var fromService = true;
        var limitReached = false;

        for (var servicePage = 1; ; servicePage++)
        {
            var batch = await api.ListAsync(servicePage, ListOcrJobsQuery.ServicePageSize, status, cancellationToken);
            if (batch.IsFailure)
            {
                if (batch.Error!.Code != OcrServiceErrorCodes.ListNotSupported)
                    return OcrErrors.ServiceUnavailable(batch.Error);

                fromService = false;
                matches.AddRange(registry.List(1, int.MaxValue, status).Items.Where(job => Matches(job, term)));
                break;
            }

            matches.AddRange(batch.Value.Items.Where(job => Matches(job, term)));
            scanned += batch.Value.Items.Count;
            if (!batch.Value.HasNextPage || batch.Value.Items.Count == 0)
                break;
            if (scanned >= ListOcrJobsQuery.MaxJobsScannedBySearch)
            {
                limitReached = true;
                break;
            }
        }

        var items = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new OcrJobList(new PagedResult<OcrJob>(items, page, pageSize, matches.Count), fromService, limitReached);
    }

    private static bool Matches(OcrJob job, string term) =>
        job.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || job.Id.Value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
