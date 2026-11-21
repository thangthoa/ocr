namespace OppOcr.Web.Features.Mockups.Quality;

// The automatic quality check (review round 1) of the documents mockup. Like the rest of the mockup it
// lives in the Web host until a Documents module exists (docs/development/ui-mockups.md).

/// <summary>Metadata a document can be required to have. The title is always set (from the file name).</summary>
public enum RequiredMetadata
{
    Reference,
    DocumentDate,
    Department,
    Kind,
    Keywords,
}

/// <summary>Rules of the automatic check, set on the Settings page.</summary>
/// <param name="MinDocumentConfidence">Percent; a document below it goes to the review queue.</param>
/// <param name="MinPageConfidence">Percent; one page below it is enough to send the document to review.</param>
/// <param name="FlagWarnings">Pages the OCR service reported a problem on, or as a duplicate of another page, go to review.</param>
/// <param name="FlagEmptyPages">Pages with no recognised text, or missing from the OCR result, go to review.</param>
/// <param name="RequiredMetadata">Fields that must be filled in; documents from the OCR service have none yet.</param>
/// <param name="AutoApprove">Documents that pass are approved without a person; otherwise they wait in the queue too.</param>
public sealed record QualitySettings(
    double MinDocumentConfidence,
    double MinPageConfidence,
    bool FlagWarnings,
    bool FlagEmptyPages,
    IReadOnlyList<RequiredMetadata> RequiredMetadata,
    bool AutoApprove)
{
    public const double MinThreshold = 0;

    public const double MaxThreshold = 100;

    public static QualitySettings Default { get; } = new(85, 75, FlagWarnings: true, FlagEmptyPages: true, [], AutoApprove: true);
}

public enum QualityIssueKind
{
    /// <summary>No page has any text: never passes, whatever the settings.</summary>
    NoText,
    LowDocumentConfidence,
    LowPageConfidence,
    PageWarnings,
    EmptyPage,
    MissingMetadata,

    /// <summary>The OCR service reports the page as the same page as an earlier one (scanned twice, the same file).</summary>
    DuplicatePage,
}

/// <param name="Page">The page the issue is on; null when it concerns the whole document.</param>
/// <param name="Confidence">The confidence that was below the threshold.</param>
/// <param name="Count">How many warnings the page has.</param>
/// <param name="Field">The metadata field that is empty.</param>
/// <param name="DuplicateOf">The earlier page that <paramref name="Page"/> looks the same as.</param>
public sealed record QualityIssue(
    QualityIssueKind Kind,
    int? Page = null,
    double? Confidence = null,
    int? Count = null,
    RequiredMetadata? Field = null,
    int? DuplicateOf = null);

/// <summary>What the check looks at on one page.</summary>
/// <param name="Confidence">Null when the page has no text.</param>
/// <param name="HasText">False for a blank page or one missing from the OCR result.</param>
/// <param name="Warnings">Problems the OCR service reported on the page.</param>
/// <param name="DuplicateOf">An earlier page the OCR service reports as the same page (API doc §4.4).</param>
public sealed record PageQuality(int Number, double? Confidence, bool HasText, int Warnings, int? DuplicateOf = null);

/// <summary>
/// Outcome of the automatic check, kept with the document together with the settings it was checked
/// against: changing the settings later does not change past results.
/// </summary>
public sealed record QualityCheckResult(DateTime CheckedAtUtc, QualitySettings Settings, IReadOnlyList<QualityIssue> Issues)
{
    public bool Passed => Issues.Count == 0;

    /// <summary>Pages with at least one issue, in order.</summary>
    public IReadOnlyList<int> FlaggedPages =>
        Issues.Where(i => i.Page is not null).Select(i => i.Page!.Value).Distinct().Order().ToList();
}
