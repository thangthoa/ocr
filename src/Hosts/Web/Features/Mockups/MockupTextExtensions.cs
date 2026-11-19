using System.Globalization;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups;

internal static class MockupTextExtensions
{
    public static string Status(this IStringLocalizer<MockupText> text, DocumentStatus status) => text[$"Status.{status}"];

    public static string Department(this IStringLocalizer<MockupText> text, DocumentDepartment department) => text[$"Department.{department}"];

    public static string Kind(this IStringLocalizer<MockupText> text, DocumentKind kind) => text[$"Kind.{kind}"];

    public static string Confidentiality(this IStringLocalizer<MockupText> text, DocumentConfidentiality value) => text[$"Confidentiality.{value}"];

    public static string Field(this IStringLocalizer<MockupText> text, DocumentSearchField field) => text[$"Field.{field}"];

    public static string Period(this IStringLocalizer<MockupText> text, ReportPeriod period) => text[$"Period.{period}"];

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
