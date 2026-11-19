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
    /// <param name="content">Read to the end; the caller keeps ownership and disposes it.</param>
    Task<Result<OcrJob>> SubmitAsync(Stream content, string fileName, OcrDocumentType documentType, CancellationToken cancellationToken);

    Task<Result<OcrJob>> GetAsync(OcrJobId jobId, CancellationToken cancellationToken);

    /// <summary>Fails with <see cref="OcrServiceErrorCodes.ListNotSupported"/> until the service offers a list endpoint.</summary>
    Task<Result<PagedResult<OcrJob>>> ListAsync(int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken);

    Task<Result> CancelAsync(OcrJobId jobId, CancellationToken cancellationToken);

    Task<Result<FileContent>> GetPageImageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken);

    Task<Result<FileContent>> DownloadAsync(OcrJobId jobId, OcrResultFormat format, CancellationToken cancellationToken);
}
