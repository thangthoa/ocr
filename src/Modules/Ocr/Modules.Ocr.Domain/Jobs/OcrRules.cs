namespace Modules.Ocr.Domain.Jobs;

public static class OcrRules
{
    /// <summary>Upload limit enforced by the OCR service (API doc §3.1).</summary>
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public const int MaxFileNameLength = 255;
}
