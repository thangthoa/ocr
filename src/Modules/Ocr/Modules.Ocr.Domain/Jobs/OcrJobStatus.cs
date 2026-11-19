namespace Modules.Ocr.Domain.Jobs;

/// <summary>Job lifecycle as reported by the OCR service (API doc §4).</summary>
public enum OcrJobStatus
{
    Queued = 1,
    Running = 2,
    Done = 3,
    Failed = 4,
    Cancelled = 5,
}

public static class OcrJobStatusExtensions
{
    public static bool IsTerminal(this OcrJobStatus status) =>
        status is OcrJobStatus.Done or OcrJobStatus.Failed or OcrJobStatus.Cancelled;
}
