namespace Modules.Ocr.Application.Features.UploadOcrFile;

/// <param name="Size">Size reported by the client; re-checked against the bytes actually read.</param>
public sealed record UploadOcrFileCommand(string FileName, long Size, Stream Content);
