using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Infrastructure.Api;

// Wire format of the OCR service (API doc §4). Property names are snake_case on the wire.
internal sealed record ApiJob(
    string? JobId,
    string? Status,
    string? FileName,
    string? DocType,
    int PagesTotal,
    int PagesDone,
    string? CurrentStep,
    int? QueuePosition,
    double? CreatedAt,
    double? FinishedAt,
    ApiResult? Result,
    ApiError? Error);

internal sealed record ApiResult(IReadOnlyList<ApiPage>? Pages, string? FullText);

internal sealed record ApiPage(int Page, string? Type, string? Text, double Seconds, string? Engine, IReadOnlyList<string>? Warnings, bool Cached);

internal sealed record ApiError(string? Code, string? Message);

internal sealed record ApiErrorEnvelope(ApiError? Error);

/// <summary>Response of <c>GET /v1/jobs</c>: <c>{ total, limit, offset, jobs[] }</c> (jobs without result text).</summary>
internal sealed record ApiJobPage(IReadOnlyList<ApiJob>? Jobs, int? Total, int? Limit, int? Offset);

internal static class ApiMapping
{
    public static OcrJob ToDomain(this ApiJob job)
    {
        var id = OcrJobId.Parse(job.JobId ?? throw new FormatException("The OCR service returned a job without job_id."));
        var pages = (job.Result?.Pages ?? [])
            .Where(p => p.Page >= 1)
            .OrderBy(p => p.Page)
            .Select(p => new OcrPage(p.Page, ParseDocumentType(p.Type), p.Text ?? string.Empty, p.Seconds, p.Engine ?? string.Empty, p.Warnings ?? [], p.Cached))
            .ToList();

        return new OcrJob(
            id,
            job.FileName ?? id.Value,
            ParseDocumentType(job.DocType),
            ParseStatus(job.Status),
            Math.Max(0, job.PagesTotal),
            Math.Max(0, job.PagesDone),
            job.CurrentStep,
            job.QueuePosition,
            FromEpoch(job.CreatedAt),
            FromEpoch(job.FinishedAt),
            pages,
            job.Error?.Code,
            job.Error?.Message);
    }

    public static string ToWire(this OcrDocumentType type) => type switch
    {
        OcrDocumentType.Printed => "printed",
        OcrDocumentType.Handwritten => "handwritten",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string ToWire(this OcrJobStatus status) => status.ToString().ToLowerInvariant();

    private static OcrJobStatus ParseStatus(string? status) => status switch
    {
        "queued" => OcrJobStatus.Queued,
        "running" => OcrJobStatus.Running,
        "done" => OcrJobStatus.Done,
        "failed" => OcrJobStatus.Failed,
        "cancelled" => OcrJobStatus.Cancelled,
        _ => throw new FormatException($"Unknown OCR job status '{status}'."),
    };

    private static OcrDocumentType ParseDocumentType(string? type) =>
        type == "handwritten" ? OcrDocumentType.Handwritten : OcrDocumentType.Printed;

    // The service reports Unix epoch seconds (API doc §4).
    private static DateTime? FromEpoch(double? seconds) =>
        seconds is null ? null : DateTime.UnixEpoch.AddSeconds(seconds.Value);
}
