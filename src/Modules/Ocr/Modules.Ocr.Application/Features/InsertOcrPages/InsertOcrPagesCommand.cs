using Modules.Ocr.Application.Features.UploadOcrPages;

namespace Modules.Ocr.Application.Features.InsertOcrPages;

/// <param name="Files">Images or PDFs; their pages are inserted in this order.</param>
/// <param name="Position">The page number the first new page gets (the page there and later ones move down); null adds them after the last page.</param>
public sealed record InsertOcrPagesCommand(string JobId, IReadOnlyList<OcrPageFile> Files, int? Position);
