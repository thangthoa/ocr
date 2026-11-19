using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Infrastructure.Pdf;

/// <summary>
/// Builds the PDF in-process from JPEG and PNG pages, without an imaging library (ADR 0005). JPEG data
/// is embedded unchanged; PNG data too, unless the image has an alpha channel.
/// </summary>
internal sealed partial class ImagePdfComposer(ILogger<ImagePdfComposer> logger) : IPdfComposer
{
    /// <summary>
    /// Pages are sized from the resolution recorded in the image, so that a service rendering the PDF
    /// at a usual scan resolution gets back about the original pixels. Images without a believable
    /// resolution (phone photos usually say 72 dpi) are treated as 300 dpi scans.
    /// </summary>
    public const double DefaultDpi = 300;

    private const double MinDpi = 100;
    private const double MaxDpi = 1200;

    public async Task<Result> ComposeAsync(IReadOnlyList<PageImage> pages, Stream destination, CancellationToken cancellationToken)
    {
        var images = new List<(PdfImage Image, double Dpi)>(pages.Count);
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var image = page.Format switch
            {
                OcrFileFormat.Jpeg => JpegReader.Read(page.Content),
                OcrFileFormat.Png => PngReader.Read(page.Content, cancellationToken),
                _ => null,
            };
            if (image is null)
            {
                LogUnsupported(i + 1, page.Format);
                return OcrErrors.ImageNotSupported;
            }

            images.Add((image, image.Dpi is >= MinDpi and <= MaxDpi ? image.Dpi.Value : DefaultDpi));
        }

        await new PdfWriter(destination).WriteAsync(images, cancellationToken);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: page {PageNumber} ({Format}) cannot be placed in a PDF")]
    private partial void LogUnsupported(int pageNumber, OcrFileFormat format);
}
