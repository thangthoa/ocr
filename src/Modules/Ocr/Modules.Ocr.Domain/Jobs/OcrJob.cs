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
    /// <summary>
    /// The service's Word file is complete. Only meaningful once the job is done: the images of printed pages
    /// (logo, signature, stamp) are made after that, and the file is rebuilt after a page is deleted (API doc §4,
    /// §3.8). True unless the service says otherwise.
    /// </summary>
    public bool WordReady { get; init; } = true;

    /// <summary>Pairs of pages that look like the same page (API doc §4.4); empty when there are none.</summary>
    public IReadOnlyList<OcrPageDuplicate> Duplicates { get; init; } = [];

    /// <summary>How many times pages were inserted, replaced or deleted (API doc §3.8); 0 when never.</summary>
    public int Edits { get; init; }

    /// <summary>When pages were last changed.</summary>
    public DateTime? UpdatedAtUtc { get; init; }

    /// <summary>The service only cancels jobs that are still waiting (API doc §3.5).</summary>
    public bool CanCancel => Status == OcrJobStatus.Queued;

    public bool IsFinished => Status.IsTerminal();

    /// <summary>
    /// The pages of any job but a cancelled one can change: while it waits, after it failed, or once it is done.
    /// While it <see cref="IsBusyWithPages"/> the change has to wait.
    /// </summary>
    public bool AllowsPageEdits => Status != OcrJobStatus.Cancelled;

    /// <summary>The service is reading pages or building the Word file; it answers <c>job_busy</c> meanwhile.</summary>
    public bool IsBusyWithPages => Status == OcrJobStatus.Running || (Status == OcrJobStatus.Done && !WordReady);

    public bool CanEditPages => AllowsPageEdits && !IsBusyWithPages;

    /// <summary>The last page cannot be deleted: deleting the whole job is the way to remove it.</summary>
    public bool CanDeletePage => PagesTotal > 1;

    public bool HasPage(int pageNumber) => pageNumber >= 1 && pageNumber <= PagesTotal;

    /// <summary>New pages become page <paramref name="position"/>: anywhere from the first page to just after the last.</summary>
    public bool CanInsertAt(int position) => position >= 1 && position <= PagesTotal + 1;

    /// <summary>True when anything a user would see differs from <paramref name="previous"/>.</summary>
    public bool HasVisibleChangesSince(OcrJob? previous) =>
        previous is null
        || previous.Status != Status
        || previous.PagesDone != PagesDone
        || previous.PagesTotal != PagesTotal
        || previous.CurrentStep != CurrentStep
        || previous.QueuePosition != QueuePosition
        || previous.Pages.Count != Pages.Count
        || previous.Edits != Edits
        || previous.WordReady != WordReady
        || !previous.Duplicates.SequenceEqual(Duplicates);
}
