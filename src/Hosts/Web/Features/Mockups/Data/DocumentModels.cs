using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.Data;

// A document is an OCR job (or a sample) plus what the OCR service cannot store: review status,
// metadata, edited text and versions. These types stand in for a future Documents module and live
// in the Web host so the mockup can be replaced in one place (docs/development/ui-mockups.md).

/// <summary>
/// Where a document is in the flow Scan → Recognizing (<see cref="Processing"/>) → Reviewing
/// (<see cref="NeedsReview"/>) → <see cref="Archived"/>. Approving a document archives it (ADR 0007).
/// </summary>
public enum DocumentStatus
{
    Processing,
    NeedsReview,
    Archived,
    Failed,
    Cancelled,
}

public enum DocumentDepartment
{
    Finance,
    HumanResources,
    Legal,
    Procurement,
    Administration,
}

public enum DocumentKind
{
    PaymentVoucher,
    PurchaseOrder,
    Contract,
    Correspondence,
    Decision,
}

public enum DocumentConfidentiality
{
    Internal,
    Confidential,
    Secret,
}

/// <summary>Which metadata a free-text search looks in.</summary>
public enum DocumentSearchField
{
    All,
    FileName,
    Title,
    Reference,
    Keyword,
    Content,
}

public enum DocumentVersionChange
{
    OcrResult,
    Edited,
    Restored,
    EditedInWord,

    /// <summary>The OCR text again, after pages were inserted, replaced or deleted (OCR job <c>edits</c>).</summary>
    PagesChanged,
}

/// <summary>Descriptive data entered by people; a fresh OCR job has only a title taken from its file name.</summary>
public sealed record DocumentMetadata(
    string Title,
    string Reference,
    DateOnly? DocumentDate,
    DocumentDepartment? Department,
    DocumentKind? Kind,
    DocumentConfidentiality Confidentiality,
    IReadOnlyList<string> Keywords);

/// <summary>A saved state of a document's text (one entry per page) and metadata.</summary>
/// <param name="WordVersion">
/// For an edit made in the Word editor: the Word file version it came from. After the pages changed: the Word file
/// the OCR service made for the new pages, once it was taken (null until then).
/// </param>
public sealed record DocumentVersion(
    string Number,
    DateTime CreatedAtUtc,
    string Author,
    DocumentVersionChange Change,
    string? RestoredFrom,
    string? Note,
    IReadOnlyList<string> PageTexts,
    DocumentMetadata Metadata,
    int? WordVersion = null);

/// <summary>Who approved a document and when; kept while it is archived.</summary>
public sealed record DocumentApproval(string By, DateTime AtUtc);

/// <param name="Id">The OCR job id, or a <c>DOC-…</c> id for a sample.</param>
/// <param name="PageTexts">Current text per page; may be shorter than <paramref name="Pages"/> (samples, running jobs).</param>
/// <param name="Job">The OCR job as last read from the service; null for samples.</param>
/// <param name="Confidence">OCR confidence of the whole document, in percent (the average of its pages).</param>
/// <param name="BatchId">The capture batch the file was uploaded in, when known.</param>
/// <param name="Approval">Set when the document is approved (and so archived); cleared when it is sent back to review.</param>
/// <param name="PageConfidences">OCR confidence per page, from page 1; null for a page without text.</param>
/// <param name="Quality">The automatic check, run when OCR finishes (again after pages changed); null until then.</param>
/// <param name="OcrEdits">How many page changes of the OCR job the text includes (<see cref="OcrJob.Edits"/>).</param>
public sealed record DocumentRecord(
    string Id,
    bool IsSample,
    string FileName,
    int Pages,
    long? SizeBytes,
    DateTime? UploadedAtUtc,
    string? UploadedBy,
    double? Confidence,
    DocumentStatus Status,
    DocumentMetadata Metadata,
    IReadOnlyList<string> PageTexts,
    IReadOnlyList<DocumentVersion> Versions,
    OcrJob? Job = null,
    string? BatchId = null,
    DocumentApproval? Approval = null,
    IReadOnlyList<double?>? PageConfidences = null,
    QualityCheckResult? Quality = null,
    int OcrEdits = 0)
{
    public double? PageConfidence(int pageNumber) =>
        PageConfidences is { } values && pageNumber >= 1 && pageNumber <= values.Count ? values[pageNumber - 1] : null;

    /// <summary>The number of a document's first version (the OCR result).</summary>
    public const string FirstVersionNumber = "1.0";

    public DocumentVersion? CurrentVersion => Versions.Count > 0 ? Versions[^1] : null;

    /// <summary>The current version's number; a document without a saved version yet (still recognizing, failed…) is at the first.</summary>
    public string VersionNumber => CurrentVersion?.Number ?? FirstVersionNumber;

    public DateTime? UpdatedAtUtc => CurrentVersion?.CreatedAtUtc ?? Job?.FinishedAtUtc ?? UploadedAtUtc;

    public string Text => string.Join("\n", PageTexts);

    /// <summary>Text and metadata can be changed only while the document is reviewed.</summary>
    public bool CanEdit => Status is DocumentStatus.NeedsReview;

    public string PageText(int pageNumber) => pageNumber >= 1 && pageNumber <= PageTexts.Count ? PageTexts[pageNumber - 1] : string.Empty;

    /// <summary>The reviewed text is final once a document is approved, which archives it.</summary>
    public bool IsFinal => Status is DocumentStatus.Archived;
}

public sealed record DocumentQuery(
    string? Text = null,
    DocumentSearchField Field = DocumentSearchField.All,
    DocumentStatus? Status = null,
    DocumentDepartment? Department = null,
    DocumentKind? Kind = null,
    DateOnly? From = null,
    DateOnly? To = null,
    DocumentConfidentiality? Confidentiality = null,
    DateOnly? ApprovedFrom = null,
    DateOnly? ApprovedTo = null);
