using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.Features.UploadOcrFile;

/// <param name="Size">Size reported by the client; re-checked against the bytes actually read.</param>
/// <param name="DocumentType">Required; nullable so a missing choice is a validation error, not a default.</param>
public sealed record UploadOcrFileCommand(string FileName, long Size, Stream Content, OcrDocumentType? DocumentType);
