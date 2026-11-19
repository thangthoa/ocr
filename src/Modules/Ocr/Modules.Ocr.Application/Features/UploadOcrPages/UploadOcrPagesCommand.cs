using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.Features.UploadOcrPages;

/// <param name="Name">Document name typed by the user; <c>.pdf</c> is added unless it is already there.</param>
/// <param name="Pages">Page images, in page order.</param>
/// <param name="DocumentType">Required; nullable so a missing choice is a validation error, not a default.</param>
public sealed record UploadOcrPagesCommand(string Name, IReadOnlyList<OcrPageFile> Pages, OcrDocumentType? DocumentType);

/// <param name="Size">Size reported by the client; re-checked against the bytes actually read.</param>
public sealed record OcrPageFile(string FileName, long Size, Stream Content);
