using Modules.Ocr.Application.Contracts;

namespace Modules.Ocr.Application.Ports;

/// <summary>
/// Uploaded files waiting for the OCR service to download them (<c>file_url</c>, ADR 0006). The adapter
/// that submits a job holds the file here only while the service fetches it, during the submit request.
/// </summary>
public interface IOcrSourceFiles
{
    /// <returns>The file, or null when the link is unknown, already released or expired.</returns>
    FileContent? Open(string token);
}
