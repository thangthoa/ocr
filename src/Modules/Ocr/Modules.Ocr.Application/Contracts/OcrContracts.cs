using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Contracts;

public enum OcrResultFormat
{
    /// <summary>Word as flowing text (right to left, with logos / signatures / stamps); the page layout is not kept.</summary>
    Docx,

    /// <summary>Word keeping the original page layout: every block at its place, one section per page.</summary>
    DocxLayout,

    /// <summary>The same layout for Google Docs: blocks flow one after the other instead of being pinned.</summary>
    DocxGdocs,

    Txt,
    Md,
    Json,
}

public enum OcrJobChangeKind
{
    Created,
    Updated,
    Deleted,
}

public sealed record OcrJobChange(OcrJobId JobId, string FileName, OcrJobStatus Status, OcrJobChangeKind Kind, string? ErrorCode = null);

public sealed record FileContent(Stream Content, string ContentType, string FileName);

/// <summary>A file to send to the OCR service, already read and checked (size, type, content).</summary>
public sealed record OcrUpload(string FileName, ReadOnlyMemory<byte> Content);

/// <summary>
/// Paths on this application that the OCR service calls (ADR 0006), relative to the address it is given
/// for this application (<c>OCR_APP_URL</c>).
/// </summary>
public static class OcrServicePaths
{
    /// <summary>Uploaded files, as <c>{SourceFiles}/{token}/{file name}</c>; the name only tells the service the file type.</summary>
    public const string SourceFiles = "api/ocr-files";

    /// <summary>Where the service posts a job when it is done or has failed.</summary>
    public const string Callback = "api/ocr-callback";
}

/// <param name="FromService">
/// False while the OCR service has no list endpoint: the jobs then come from this
/// application's in-memory record of what it submitted since it started.
/// </param>
/// <param name="SearchLimitReached">A text search stopped after the newest jobs; older ones were not searched.</param>
public sealed record OcrJobList(PagedResult<OcrJob> Jobs, bool FromService, bool SearchLimitReached = false);
