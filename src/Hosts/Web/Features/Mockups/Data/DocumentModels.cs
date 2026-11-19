using Modules.Ocr.Domain.Jobs;

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
public sealed record DocumentVersion(
    string Number,
    DateTime CreatedAtUtc,
    string Author,
    DocumentVersionChange Change,
    string? RestoredFrom,
    string? Note,
    IReadOnlyList<string> PageTexts,
    DocumentMetadata Metadata);

/// <param name="Id">The OCR job id, or a <c>DOC-…</c> id for a sample.</param>
/// <param name="PageTexts">Current text per page; may be shorter than <paramref name="Pages"/> (samples, running jobs).</param>
/// <param name="Job">The OCR job as last read from the service; null for samples.</param>
/// <param name="BatchId">The capture batch the file was uploaded in, when known.</param>
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
    string? BatchId = null)
{
    public DocumentVersion? CurrentVersion => Versions.Count > 0 ? Versions[^1] : null;

    public DateTime? UpdatedAtUtc => CurrentVersion?.CreatedAtUtc ?? Job?.FinishedAtUtc ?? UploadedAtUtc;

    public string Text => string.Join("\n", PageTexts);

    /// <summary>Text and metadata can be changed only between OCR and archiving.</summary>
    public bool CanEdit => Status is DocumentStatus.NeedsReview or DocumentStatus.Approved;

    public string PageText(int pageNumber) => pageNumber >= 1 && pageNumber <= PageTexts.Count ? PageTexts[pageNumber - 1] : string.Empty;
}

public sealed record DocumentQuery(
    string? Text = null,
    DocumentSearchField Field = DocumentSearchField.All,
    DocumentStatus? Status = null,
    DocumentDepartment? Department = null,
    DocumentKind? Kind = null,
    DateOnly? From = null,
    DateOnly? To = null);
