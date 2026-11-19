using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Quality;

/// <summary>
/// Review round 1: compares an OCR result with the configured thresholds and lists everything a person
/// should look at. A document with no issues passes; any issue sends it to the review queue.
/// </summary>
public static class QualityCheck
{
    public static QualityCheckResult Run(
        IReadOnlyList<PageQuality> pages,
        double? documentConfidence,
        DocumentMetadata metadata,
        QualitySettings settings,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(settings);

        List<QualityIssue> issues = [];
        if (documentConfidence is not { } confidence)
            issues.Add(new QualityIssue(QualityIssueKind.NoText));
        else if (confidence < settings.MinDocumentConfidence)
            issues.Add(new QualityIssue(QualityIssueKind.LowDocumentConfidence, Confidence: confidence));

        foreach (var page in pages.OrderBy(p => p.Number))
        {
            if (!page.HasText)
            {
                if (settings.FlagEmptyPages)
                    issues.Add(new QualityIssue(QualityIssueKind.EmptyPage, page.Number));
                continue;
            }

            if (page.Confidence is { } pageConfidence && pageConfidence < settings.MinPageConfidence)
                issues.Add(new QualityIssue(QualityIssueKind.LowPageConfidence, page.Number, pageConfidence));
            if (settings.FlagWarnings && page.Warnings > 0)
                issues.Add(new QualityIssue(QualityIssueKind.PageWarnings, page.Number, Count: page.Warnings));
        }

        foreach (var field in settings.RequiredMetadata.Distinct().Order())
        {
            if (IsMissing(metadata, field))
                issues.Add(new QualityIssue(QualityIssueKind.MissingMetadata, Field: field));
        }

        return new QualityCheckResult(nowUtc, settings, issues);
    }

    /// <summary>The average of the pages that have text; null when none has.</summary>
    public static double? DocumentConfidence(IEnumerable<double?> pageConfidences)
    {
        ArgumentNullException.ThrowIfNull(pageConfidences);
        var values = pageConfidences.OfType<double>().ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 1);
    }

    private static bool IsMissing(DocumentMetadata metadata, RequiredMetadata field) => field switch
    {
        RequiredMetadata.Reference => string.IsNullOrWhiteSpace(metadata.Reference),
        RequiredMetadata.DocumentDate => metadata.DocumentDate is null,
        RequiredMetadata.Department => metadata.Department is null,
        RequiredMetadata.Kind => metadata.Kind is null,
        RequiredMetadata.Keywords => metadata.Keywords.Count == 0,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}
