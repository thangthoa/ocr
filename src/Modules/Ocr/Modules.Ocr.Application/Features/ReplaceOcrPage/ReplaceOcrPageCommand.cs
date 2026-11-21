using Modules.Ocr.Application.Features.UploadOcrPages;

namespace Modules.Ocr.Application.Features.ReplaceOcrPage;

/// <param name="PageNumber">From 1.</param>
/// <param name="File">The new image of the page (for example scanned again because the first scan was blurred).</param>
public sealed record ReplaceOcrPageCommand(string JobId, int PageNumber, OcrPageFile File);
