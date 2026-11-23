using FluentValidation;
using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.UploadOcrPages;

/// <summary>
/// Combines page images into one PDF and sends it to the OCR service as a single job, so a paper
/// document scanned or photographed page by page is read and reviewed as one document.
/// </summary>
public sealed partial class UploadOcrPagesHandler(
    IValidator<UploadOcrPagesCommand> validator,
    IPdfComposer composer,
    UploadOcrFileHandler uploadFile,
    ILogger<UploadOcrPagesHandler> logger)
{
    private const int ChunkSize = 81920;

    public async Task<Result<OcrJob>> HandleAsync(UploadOcrPagesCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        // As for a single upload, sizes reported by the client and extensions are not trusted: the
        // pages are read up to the service limit and each signature is checked.
        var pages = new List<PageImage>(command.Pages.Count);
        long imageBytes = 0;
        foreach (var page in command.Pages)
        {
            var content = await ReadAtMostAsync(page.Content, OcrRules.MaxSizeBytes - imageBytes, cancellationToken);
            if (content is null)
                return OcrErrors.TooLarge;
            if (content.Length == 0)
                return Error.Validation(OcrErrorCodes.Empty, "File is empty.");

            var format = OcrFileFormats.Detect(content.AsSpan(0, Math.Min(content.Length, OcrFileFormats.SignatureLength)));
            if (format is null || format != OcrFileFormats.FromFileName(page.FileName))
                return OcrErrors.ContentMismatch;

            imageBytes += content.Length;
            pages.Add(new PageImage(format.Value, content));
        }

        await using var pdf = new MemoryStream();
        var composed = await composer.ComposeAsync(pages, pdf, cancellationToken);
        if (composed.IsFailure)
            return composed.Error!;

        LogComposed(pages.Count, imageBytes, pdf.Length);
        pdf.Position = 0;
        return await uploadFile.HandleAsync(
            new UploadOcrFileCommand(DocumentName(command.Name), pdf.Length, pdf), cancellationToken);
    }

    internal static string DocumentName(string name) => OcrFileFormats.WithExtension(name.Trim(), OcrFileFormat.Pdf);

    /// <returns>The bytes read, or null when the stream holds more than <paramref name="maxBytes"/>.</returns>
    private static async Task<byte[]?> ReadAtMostAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkSize];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: combined {PageCount} page images ({ImageBytes} bytes) into a {PdfBytes}-byte PDF")]
    private partial void LogComposed(int pageCount, long imageBytes, long pdfBytes);
}
