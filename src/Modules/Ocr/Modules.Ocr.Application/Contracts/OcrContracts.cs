using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Contracts;

public enum OcrResultFormat
{
    Docx,
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

/// <param name="FromService">
/// False while the OCR service has no list endpoint: the jobs then come from this
/// application's in-memory record of what it submitted since it started.
/// </param>
/// <param name="SearchLimitReached">A text search stopped after the newest jobs; older ones were not searched.</param>
public sealed record OcrJobList(PagedResult<OcrJob> Jobs, bool FromService, bool SearchLimitReached = false);
