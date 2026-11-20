using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Ports;

/// <summary>Reads the result the OCR service posts to this application when a job ends (ADR 0006).</summary>
public interface IOcrCallbackReader
{
    /// <returns>
    /// The job; <see cref="ErrorType.Unauthorized"/> when the request is not signed by the OCR service or is
    /// too old, <see cref="ErrorType.Validation"/> when a signed body is not a job.
    /// </returns>
    Result<OcrJob> Read(OcrCallback callback);
}

/// <param name="Body">The raw body, exactly as received: the signature covers these bytes.</param>
/// <param name="Timestamp">The <c>X-OCR-Timestamp</c> header (Unix seconds).</param>
/// <param name="Signature">The <c>X-OCR-Signature</c> header (<c>sha256=&lt;hex&gt;</c>).</param>
public sealed record OcrCallback(ReadOnlyMemory<byte> Body, string? Timestamp, string? Signature);
