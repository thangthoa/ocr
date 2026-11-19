using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Ports;

/// <summary>
/// The latest known state of jobs submitted through this application, held in memory (not
/// persisted). Lets the worker announce progress, and serves as the job list until the OCR
/// service offers one.
/// </summary>
public interface IOcrJobRegistry
{
    void Add(OcrJob job);

    /// <summary>Stores the new state if the job is tracked; returns the previous state, or null.</summary>
    OcrJob? Update(OcrJob job);

    bool Remove(OcrJobId jobId);

    OcrJob? Find(OcrJobId jobId);

    IReadOnlyList<OcrJob> Unfinished();

    PagedResult<OcrJob> List(int page, int pageSize, OcrJobStatus? status);
}
