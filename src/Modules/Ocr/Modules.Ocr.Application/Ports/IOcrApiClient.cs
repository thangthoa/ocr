using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Ports;

/// <summary>
/// The external OCR service. Failures come back as <see cref="Error"/> carrying the service's
/// own code; <see cref="ErrorType.Failure"/> marks a temporary problem (network, timeout, 5xx).
/// </summary>
public interface IOcrApiClient
{
    /// <summary>
    /// Creates a job for one file. How the file reaches the service (uploaded, or downloaded by the
    /// service from a link, ADR 0006) is the adapter's choice; the call returns once the service has it.
    /// The service recognizes printed and handwritten pages itself (see <see cref="OcrJob.DocumentTypes"/>).
    /// </summary>
    /// <param name="content">Read to the end; the caller keeps ownership and disposes it.</param>
    Task<Result<OcrJob>> SubmitAsync(Stream content, string fileName, CancellationToken cancellationToken);

    Task<Result<OcrJob>> GetAsync(OcrJobId jobId, CancellationToken cancellationToken);

    /// <summary>Fails with <see cref="OcrServiceErrorCodes.ListNotSupported"/> until the service offers a list endpoint.</summary>
    Task<Result<PagedResult<OcrJob>>> ListAsync(int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken);

    Task<Result> CancelAsync(OcrJobId jobId, CancellationToken cancellationToken);

    Task<Result<FileContent>> GetPageImageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken);

    Task<Result<FileContent>> DownloadAsync(OcrJobId jobId, OcrResultFormat format, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the files' pages so the first becomes page <paramref name="position"/> (null: after the last page).
    /// The service reads only the new pages; the job is queued again.
    /// </summary>
    Task<Result<OcrJob>> InsertPagesAsync(OcrJobId jobId, IReadOnlyList<OcrUpload> files, int? position, CancellationToken cancellationToken);

    /// <summary>Replaces page <paramref name="pageNumber"/> with a new image; the job is queued again to read it.</summary>
    Task<Result<OcrJob>> ReplacePageAsync(OcrJobId jobId, int pageNumber, OcrUpload file, CancellationToken cancellationToken);

    /// <summary>Deletes a page at once; later pages move up and the text is updated without reading anything again.</summary>
    Task<Result<OcrJob>> DeletePageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken);
}
