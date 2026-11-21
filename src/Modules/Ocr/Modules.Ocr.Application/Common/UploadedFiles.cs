using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Common;

/// <summary>
/// Reads an uploaded file before anything is sent to a third party. The size reported by the client and the
/// extension cannot be trusted, so the read is capped and the content checked against the extension.
/// </summary>
internal static class UploadedFiles
{
    private const int ChunkSize = 81920;

    /// <param name="maxBytes">The most this file may hold (what is left of the request's allowance).</param>
    public static async Task<Result<OcrUpload>> ReadAsync(Stream content, string fileName, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkSize];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return OcrErrors.TooLarge;
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
            return Error.Validation(OcrErrorCodes.Empty, "File is empty.");

        var bytes = buffer.ToArray();
        var format = OcrFileFormats.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, OcrFileFormats.SignatureLength)));
        if (format is null || format != OcrFileFormats.FromFileName(fileName))
            return OcrErrors.ContentMismatch;

        return new OcrUpload(fileName, bytes);
    }
}
