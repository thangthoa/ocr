using System.Globalization;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups;

internal static class MockupTextExtensions
{
    public static string Status(this IStringLocalizer<MockupText> text, DocumentStatus status) => text[$"Status.{status}"];

    public static string Department(this IStringLocalizer<MockupText> text, DocumentDepartment department) => text[$"Department.{department}"];

    public static string Kind(this IStringLocalizer<MockupText> text, DocumentKind kind) => text[$"Kind.{kind}"];

    public static string Confidentiality(this IStringLocalizer<MockupText> text, DocumentConfidentiality value) => text[$"Confidentiality.{value}"];

    public static string Field(this IStringLocalizer<MockupText> text, DocumentSearchField field) => text[$"Field.{field}"];

    public static string Period(this IStringLocalizer<MockupText> text, ReportPeriod period) => text[$"Period.{period}"];

    public static string Required(this IStringLocalizer<MockupText> text, RequiredMetadata field) => text[$"Meta.{field}"];

    /// <summary>One issue of the automatic check, in full (document page).</summary>
    public static string Issue(this IStringLocalizer<MockupText> text, QualityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue.Kind switch
        {
            QualityIssueKind.NoText => text["Quality.Issue.NoText"],
            QualityIssueKind.LowDocumentConfidence => text["Quality.Issue.LowDocument", FormatPercent(issue.Confidence ?? 0)],
            QualityIssueKind.LowPageConfidence => text["Quality.Issue.LowPage", issue.Page ?? 0, FormatPercent(issue.Confidence ?? 0)],
            QualityIssueKind.PageWarnings => text["Quality.Issue.Warnings", issue.Page ?? 0, issue.Count ?? 0],
            QualityIssueKind.EmptyPage => text["Quality.Issue.EmptyPage", issue.Page ?? 0],
            QualityIssueKind.MissingMetadata => text["Quality.Issue.Missing", issue.Field is { } field ? text.Required(field) : "?"],
            _ => issue.Kind.ToString(),
        };
    }

    /// <summary>The issues of a check grouped by kind, one short line each (review queue).</summary>
    public static IReadOnlyList<string> IssueSummary(this IStringLocalizer<MockupText> text, QualityCheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var separator = text["ListSeparator"].Value;
        string Pages(QualityIssueKind kind) => string.Join(separator, check.Issues.Where(i => i.Kind == kind).Select(i => FormatNumber(i.Page ?? 0)));

        return check.Issues
            .Select(i => i.Kind)
            .Distinct()
            .Select(kind => kind switch
            {
                QualityIssueKind.NoText => text["Quality.Issue.NoText"].Value,
                QualityIssueKind.LowDocumentConfidence => text.Issue(check.Issues.First(i => i.Kind == kind)),
                QualityIssueKind.LowPageConfidence => text["Quality.Summary.LowPages", Pages(kind)].Value,
                QualityIssueKind.PageWarnings => text["Quality.Summary.Warnings", Pages(kind)].Value,
                QualityIssueKind.EmptyPage => text["Quality.Summary.EmptyPages", Pages(kind)].Value,
                QualityIssueKind.MissingMetadata => text["Quality.Issue.Missing",
                    string.Join(separator, check.Issues.Where(i => i.Field is not null).Select(i => text.Required(i.Field!.Value)))].Value,
                _ => kind.ToString(),
            })
            .ToList();
    }

    /// <summary>Carbon tag colour per status; the label is always shown, so colour is never the only cue.</summary>
    public static string StatusTagClass(DocumentStatus status) => status switch
    {
        DocumentStatus.Processing => "tag-blue",
        DocumentStatus.NeedsReview => "tag-magenta",
        DocumentStatus.Approved => "tag-teal",
        DocumentStatus.Archived => "tag-green",
        DocumentStatus.Failed => "tag-red",
        DocumentStatus.Cancelled => "tag-gray",
        _ => "tag-gray",
    };

    public const string Missing = "—";

    public static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static string FormatDate(DateOnly? date) => date is { } value ? FormatDate(value) : Missing;

    public static string FormatDateTime(DateTime utc) => utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static string FormatDateTime(DateTime? utc) => utc is { } value ? FormatDateTime(value) : Missing;

    public static string FormatShortDate(DateTime? utc) =>
        utc is { } value ? value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : Missing;

    public static string OrMissing(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value;

    public static string FormatNumber(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string FormatPercent(double value) => (value / 100).ToString("P1", CultureInfo.CurrentCulture);

    public static string FormatSigned(double value, string format) =>
        (value > 0 ? "+" : string.Empty) + value.ToString(format, CultureInfo.CurrentCulture);
}
