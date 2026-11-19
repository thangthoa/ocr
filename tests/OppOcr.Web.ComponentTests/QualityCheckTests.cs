using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.ComponentTests;

/// <summary>The rules of the automatic quality check (review round 1) and the simulated confidence.</summary>
public sealed class QualityCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DocumentMetadata TitleOnly = new("memo", string.Empty, null, null, null, DocumentConfidentiality.Internal, []);

    [Fact]
    public void A_document_that_meets_every_threshold_passes()
    {
        var result = Run([Page(1, 90), Page(2, 80)], 85);

        Assert.True(result.Passed);
        Assert.Empty(result.FlaggedPages);
        Assert.Equal(Now, result.CheckedAtUtc);
    }

    [Fact]
    public void Low_document_and_page_confidence_are_flagged_with_their_values()
    {
        var result = Run([Page(1, 95), Page(2, 70.4), Page(3, 74.9)], 80.1);

        Assert.False(result.Passed);
        Assert.Equal(
            [
                new QualityIssue(QualityIssueKind.LowDocumentConfidence, Confidence: 80.1),
                new QualityIssue(QualityIssueKind.LowPageConfidence, 2, 70.4),
                new QualityIssue(QualityIssueKind.LowPageConfidence, 3, 74.9),
            ],
            result.Issues);
        Assert.Equal([2, 3], result.FlaggedPages);
    }

    [Fact]
    public void A_value_equal_to_the_threshold_passes() =>
        Assert.True(Run([Page(1, 75)], 85).Passed);

    [Theory]
    [InlineData(true, true, 2)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 0)]
    public void Warnings_and_empty_pages_are_flagged_only_when_the_settings_say_so(bool flagWarnings, bool flagEmptyPages, int expected)
    {
        var settings = QualitySettings.Default with { FlagWarnings = flagWarnings, FlagEmptyPages = flagEmptyPages };

        var result = Run([Page(1, 95, warnings: 2), new PageQuality(2, null, HasText: false, 0)], 95, settings);

        Assert.Equal(expected, result.Issues.Count);
    }

    [Fact]
    public void Required_metadata_that_is_empty_is_flagged_once_per_field()
    {
        var settings = QualitySettings.Default with
        {
            RequiredMetadata = [RequiredMetadata.Department, RequiredMetadata.Reference, RequiredMetadata.Department],
        };
        var metadata = TitleOnly with { Reference = "MEMO/1" };

        var result = QualityCheck.Run([Page(1, 99)], 99, metadata, settings, Now);

        Assert.Equal([new QualityIssue(QualityIssueKind.MissingMetadata, Field: RequiredMetadata.Department)], result.Issues);
    }

    [Fact]
    public void A_document_without_any_text_never_passes()
    {
        var lenient = new QualitySettings(0, 0, FlagWarnings: false, FlagEmptyPages: false, [], AutoApprove: true);

        var result = Run([new PageQuality(1, null, HasText: false, 0)], null, lenient);

        Assert.Equal([new QualityIssue(QualityIssueKind.NoText)], result.Issues);
    }

    [Fact]
    public void Document_confidence_is_the_average_of_the_pages_with_text()
    {
        Assert.Equal(85.5, QualityCheck.DocumentConfidence([90, null, 81]));
        Assert.Null(QualityCheck.DocumentConfidence([null, null]));
    }

    [Fact]
    public void Simulated_confidence_is_stable_and_follows_the_pages_of_the_job()
    {
        var job = Job("job_0000000000000042", Page(1, "نص"), Page(3, " "));

        var values = SimulatedConfidence.ForJob(job);

        Assert.Equal(values, SimulatedConfidence.ForJob(job));
        Assert.Equal(3, values.Count);
        Assert.InRange(values[0]!.Value, 72, 99);
        Assert.Null(values[1]);
        Assert.Null(values[2]);
    }

    [Fact]
    public void Each_warning_lowers_the_simulated_confidence()
    {
        var clean = SimulatedConfidence.ForJob(Job("job_0000000000000043", Page(1, "نص")))[0]!.Value;

        var warned = SimulatedConfidence.ForJob(Job("job_0000000000000043", Page(1, "نص", "a", "b")))[0]!.Value;

        Assert.Equal(Math.Round(clean - (2 * SimulatedConfidence.WarningPenalty), 1), warned, 1);
    }

    [Theory]
    [InlineData(71.5, 2)]
    [InlineData(82.4, 14)]
    [InlineData(99.0, 1)]
    [InlineData(98.1, 5)]
    public void Sample_confidence_is_spread_over_pages_keeping_its_average(double confidence, int pages)
    {
        var values = SimulatedConfidence.SpreadOverPages("DOC-2026-000999", pages, confidence);

        Assert.Equal(pages, values.Count);
        Assert.Equal(confidence, values.Average()!.Value, 0.05);
        Assert.All(values, v => Assert.InRange(v!.Value, 1, 99.9));
    }

    private static QualityCheckResult Run(IReadOnlyList<PageQuality> pages, double? confidence, QualitySettings? settings = null) =>
        QualityCheck.Run(pages, confidence, TitleOnly, settings ?? QualitySettings.Default, Now);

    private static PageQuality Page(int number, double confidence, int warnings = 0) => new(number, confidence, HasText: true, warnings);

    private static OcrPage Page(int number, string text, params string[] warnings) =>
        new(number, OcrDocumentType.Printed, text, 1, "test", warnings, Cached: false);

    private static OcrJob Job(string id, params OcrPage[] pages) =>
        InMemoryOcr.NewJob(id, "scan.png", OcrJobStatus.Done) with { PagesTotal = 3, PagesDone = 3, Pages = pages };
}
