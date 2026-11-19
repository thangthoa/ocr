namespace Modules.Ocr.Domain.Jobs;

/// <summary>
/// A job as the OCR service reports it. The service is the source of truth; this application
/// keeps no copy of its own (see ADR 0003).
/// </summary>
public sealed record OcrJob(
    OcrJobId Id,
    string FileName,
    OcrDocumentType DocumentType,
    OcrJobStatus Status,
    int PagesTotal,
    int PagesDone,
    string? CurrentStep,
    int? QueuePosition,
    DateTime? CreatedAtUtc,
    DateTime? FinishedAtUtc,
    IReadOnlyList<OcrPage> Pages,
    string? ErrorCode,
    string? ErrorMessage)
{
    /// <summary>The service only cancels jobs that are still waiting (API doc §3.5).</summary>
    public bool CanCancel => Status == OcrJobStatus.Queued;

    public bool IsFinished => Status.IsTerminal();

    /// <summary>True when anything a user would see differs from <paramref name="previous"/>.</summary>
    public bool HasVisibleChangesSince(OcrJob? previous) =>
        previous is null
        || previous.Status != Status
        || previous.PagesDone != PagesDone
        || previous.PagesTotal != PagesTotal
        || previous.CurrentStep != CurrentStep
        || previous.QueuePosition != QueuePosition
        || previous.Pages.Count != Pages.Count;
}
