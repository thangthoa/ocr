using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.Data;

// A document is an OCR job (or a sample) plus what the OCR service cannot store: review status,
// metadata, edited text and versions. These types stand in for a future Documents module and live
// in the Web host so the mockup can be replaced in one place (docs/development/ui-mockups.md).

public enum DocumentStatus
{
    Processing,
    NeedsReview,
    Approved,
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
/// <param name="WordVersion">For an edit made in the Word editor: the Word file version it came from.</param>
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

/// <summary>Who approved a document and when; kept while it is approved or archived.</summary>
public sealed record DocumentApproval(string By, DateTime AtUtc);

/// <param name="Id">The OCR job id, or a <c>DOC-…</c> id for a sample.</param>
/// <param name="PageTexts">Current text per page; may be shorter than <paramref name="Pages"/> (samples, running jobs).</param>
/// <param name="Job">The OCR job as last read from the service; null for samples.</param>
/// <param name="Confidence">OCR confidence of the whole document, in percent (the average of its pages).</param>
/// <param name="BatchId">The capture batch the file was uploaded in, when known.</param>
/// <param name="Approval">Set when the document is approved; cleared when it is sent back to review.</param>
/// <param name="PageConfidences">OCR confidence per page, from page 1; null for a page without text.</param>
/// <param name="Quality">The automatic check, run once when OCR finishes; null until then.</param>
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
    QualityCheckResult? Quality = null)
{
    public double? PageConfidence(int pageNumber) =>
        PageConfidences is { } values && pageNumber >= 1 && pageNumber <= values.Count ? values[pageNumber - 1] : null;

    public DocumentVersion? CurrentVersion => Versions.Count > 0 ? Versions[^1] : null;

    public DateTime? UpdatedAtUtc => CurrentVersion?.CreatedAtUtc ?? Job?.FinishedAtUtc ?? UploadedAtUtc;

    public string Text => string.Join("\n", PageTexts);

    /// <summary>Text and metadata can be changed only between OCR and archiving.</summary>
    public bool CanEdit => Status is DocumentStatus.NeedsReview or DocumentStatus.Approved;

    public string PageText(int pageNumber) => pageNumber >= 1 && pageNumber <= PageTexts.Count ? PageTexts[pageNumber - 1] : string.Empty;

    /// <summary>The reviewed text is final once a document is approved; archived documents keep it.</summary>
    public bool IsFinal => Status is DocumentStatus.Approved or DocumentStatus.Archived;
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
