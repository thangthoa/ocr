using System.Buffers;

namespace Modules.Ocr.Domain.Jobs;

public static class OcrRules
{
    /// <summary>Upload limit enforced by the OCR service (API doc §3.1).</summary>
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    /// <summary>The OCR service rejects longer documents (<c>too_many_pages</c>).</summary>
    public const int MaxPagesPerDocument = 20;

    // Path separators and the characters Windows rejects: the name is typed by the user and is
    // later used as a download file name.
    private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create("/\\:*?\"<>|");

    public static bool IsValidFileName(string name) =>
        !name.AsSpan().ContainsAny(InvalidFileNameChars) && !name.Any(char.IsControl);
}
