using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Infrastructure.Api;

// Wire format of the OCR service (API doc §4). Property names are snake_case on the wire.
internal sealed record ApiJob(
    string? JobId,
    string? Status,
    string? FileName,
    int PagesTotal,
    int PagesDone,
    string? CurrentStep,
    int? QueuePosition,
    double? CreatedAt,
    double? FinishedAt,
    ApiResult? Result,
    ApiError? Error,
    bool? WordReady = null,
    IReadOnlyList<ApiDuplicate>? Duplicates = null,
    int? Edits = null,
    double? UpdatedAt = null);

/// <summary><c>{"pages": [a, b], "kind": "identical" | "similar" | "same_text"}</c> (API doc §4.4).</summary>
internal sealed record ApiDuplicate(IReadOnlyList<int>? Pages, string? Kind);

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
            ParseStatus(job.Status),
            Math.Max(0, job.PagesTotal),
            Math.Max(0, job.PagesDone),
            job.CurrentStep,
            job.QueuePosition,
            FromEpoch(job.CreatedAt),
            FromEpoch(job.FinishedAt),
            pages,
            job.Error?.Code,
            job.Error?.Message)
        {
            // A service that does not report it predates the Word images: its file is complete when the job is done.
            WordReady = job.WordReady ?? true,
            Duplicates = ParseDuplicates(job.Duplicates),
            Edits = Math.Max(0, job.Edits ?? 0),
            UpdatedAtUtc = FromEpoch(job.UpdatedAt),
        };
    }

    public static string ToWire(this OcrJobStatus status) => status.ToString().ToLowerInvariant();

    public static string ToWire(this OcrResultFormat format) => format switch
    {
        OcrResultFormat.Docx => "docx",
        OcrResultFormat.DocxLayout => "docx_layout",
        OcrResultFormat.DocxGdocs => "docx_gdocs",
        OcrResultFormat.Txt => "txt",
        OcrResultFormat.Md => "md",
        OcrResultFormat.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>For the file name when the service sends none; all three Word variants are DOCX files.</summary>
    public static string FileExtension(this OcrResultFormat format) => format switch
    {
        OcrResultFormat.Docx or OcrResultFormat.DocxLayout or OcrResultFormat.DocxGdocs => "docx",
        _ => format.ToWire(),
    };

    private static OcrJobStatus ParseStatus(string? status) => status switch
    {
        "queued" => OcrJobStatus.Queued,
        "running" => OcrJobStatus.Running,
        "done" => OcrJobStatus.Done,
        "failed" => OcrJobStatus.Failed,
        "cancelled" => OcrJobStatus.Cancelled,
        _ => throw new FormatException($"Unknown OCR job status '{status}'."),
    };

    /// <summary>Pairs this version does not understand (an unknown kind, not two pages) are left out; they are only warnings.</summary>
    private static List<OcrPageDuplicate> ParseDuplicates(IReadOnlyList<ApiDuplicate>? duplicates)
    {
        var parsed = new List<OcrPageDuplicate>();
        foreach (var duplicate in duplicates ?? [])
        {
            if (duplicate.Pages is [var first, var second] && first >= 1 && second >= 1 && first != second
                && ParseDuplicateKind(duplicate.Kind) is { } kind)
                parsed.Add(new OcrPageDuplicate(first, second, kind));
        }

        return parsed;
    }

    private static OcrDuplicateKind? ParseDuplicateKind(string? kind) => kind switch
    {
        "identical" => OcrDuplicateKind.Identical,
        "similar" => OcrDuplicateKind.Similar,
        "same_text" => OcrDuplicateKind.SameText,
        _ => null,
    };

    private static OcrDocumentType ParseDocumentType(string? type) =>
        type == "handwritten" ? OcrDocumentType.Handwritten : OcrDocumentType.Printed;

    // The service reports Unix epoch seconds (API doc §4).
    private static DateTime? FromEpoch(double? seconds) =>
        seconds is null ? null : DateTime.UnixEpoch.AddSeconds(seconds.Value);
}
