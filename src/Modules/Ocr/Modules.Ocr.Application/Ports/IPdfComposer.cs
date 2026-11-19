using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Ports;

/// <summary>Builds one PDF from page images, so several scans can be sent to the OCR service as one document.</summary>
public interface IPdfComposer
{
    /// <summary>Writes a PDF with one page per image, in the given order.</summary>
    /// <returns>
    /// <see cref="Contracts.OcrErrors.ImageNotSupported"/> when an image cannot be placed on a page;
    /// what was written to <paramref name="destination"/> must then be discarded.
    /// </returns>
    Task<Result> ComposeAsync(IReadOnlyList<PageImage> pages, Stream destination, CancellationToken cancellationToken);
}

/// <param name="Format">One of <see cref="OcrFileFormats.PageFormats"/>, already checked against the content.</param>
public sealed record PageImage(OcrFileFormat Format, ReadOnlyMemory<byte> Content);
