using System.Globalization;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Kpi;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Mockups.Reports;

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

    public static string RoleName(this IStringLocalizer<MockupText> text, UserRole role) => text[$"Role.{role}"];

    public static string PermissionName(this IStringLocalizer<MockupText> text, AccessRight permission) => text[$"Permission.{permission}"];

    public static string KpiName(this IStringLocalizer<MockupText> text, KpiKind kind) => text[$"KpiDef.{kind}"];

    public static string KpiStatusName(this IStringLocalizer<MockupText> text, KpiStatus status) => text[$"KpiStatus.{status}"];

    /// <summary>A KPI figure (actual, target or alert level) in the KPI's unit.</summary>
    public static string KpiValue(this IStringLocalizer<MockupText> text, KpiKind kind, double value) => KpiRules.Unit(kind) switch
    {
        KpiUnit.Percent => FormatPercent(value),
        KpiUnit.Hours => text["Kpi.Hours", value.ToString("N1", CultureInfo.CurrentCulture)],
        _ => FormatNumber((int)Math.Round(value)),
    };

    /// <summary>Colour class per KPI status (green, camel, red); the status is always written next to it.</summary>
    public static string KpiStatusClass(KpiStatus status) => status switch
    {
        KpiStatus.OnTrack => "kpi-on-track",
        KpiStatus.AtRisk => "kpi-at-risk",
        _ => "kpi-off-track",
    };

    /// <summary>What an activity log entry says was done, in the UI language.</summary>
    public static string DescribeActivity(this IStringLocalizer<MockupText> text, AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Action switch
        {
            AuditAction.SignedIn => text["Audit.SignedIn"],
            AuditAction.SignedOut => text["Audit.SignedOut"],
            AuditAction.SignInFailed => text["Audit.SignInFailed"],
            AuditAction.OtpFailed => text["Audit.OtpFailed"],
            AuditAction.AccountLocked => text["Audit.AccountLocked"],
            AuditAction.AccessDenied => text[entry.Detail == "Department" ? "Audit.AccessDenied.Department" : "Audit.AccessDenied.Clearance"],
            AuditAction.Viewed => text["Audit.Viewed"],
            AuditAction.Searched => text["Audit.Searched", entry.Detail ?? "?"],
            AuditAction.Printed => text["Audit.Printed", entry.Version ?? "?"],
            AuditAction.Uploaded when entry.DocumentId is null && entry.Detail is not null => text["Audit.UploadedBatch", entry.BatchId ?? Missing, entry.Detail],
            AuditAction.Uploaded => text["Audit.Uploaded", entry.BatchId ?? Missing],
            AuditAction.Edited => text["Audit.Edited", entry.Version ?? "?"],
            AuditAction.MetadataChanged => text["Audit.MetadataChanged", entry.Version ?? "?"],
            AuditAction.Restored => text["Audit.Restored", entry.Version ?? "?"],
            AuditAction.StatusChanged => text["Audit.StatusChanged", entry.Status is { } status ? text.Status(status) : "?"],
            AuditAction.Deleted => text["Audit.Deleted"],
            AuditAction.EditedInWord => text["Audit.EditedInWord", entry.Version ?? "?"],
            AuditAction.PagesInserted => text["Audit.PagesInserted", entry.Detail ?? "?", entry.Version ?? "?"],
            AuditAction.PageReplaced => text["Audit.PageReplaced", entry.Version ?? "?"],
            AuditAction.PageDeleted => text["Audit.PageDeleted", entry.Version ?? "?"],
            AuditAction.Downloaded => text["Audit.Downloaded", entry.Detail ?? "?", entry.Version ?? "?"],
            AuditAction.Exported => text["Audit.Exported", entry.Detail ?? "?"],
            AuditAction.QualityChecked when entry.Status == DocumentStatus.Archived => text["Audit.QualityApproved"],
            AuditAction.QualityChecked when entry.Detail == "0" => text["Audit.QualityPassed"],
            AuditAction.QualityChecked => text["Audit.QualityFlagged", entry.Detail ?? "?"],
            AuditAction.OcrFailed => text["Audit.OcrFailed", entry.Detail ?? "?"],
            AuditAction.OcrRetried => text["Audit.OcrRetried"],
            AuditAction.QualitySettingsChanged => text["Audit.QualitySettingsChanged"],
            AuditAction.KpiSettingsChanged => text["Audit.KpiSettingsChanged"],
            AuditAction.UserAdded => text["Audit.UserAdded", entry.Detail ?? "?"],
            AuditAction.UserChanged => text["Audit.UserChanged", entry.Detail ?? "?"],
            AuditAction.RolesChanged => text["Audit.RolesChanged"],
            AuditAction.ReportExported => text["Audit.ReportExported",
                ReportEndpoints.ParseKind(entry.Detail) is { } report ? text[$"Report.{report}.Title"] : entry.Detail ?? "?"],
            AuditAction.ActivityExported => text["Audit.ActivityExported", entry.Detail ?? "?"],
            _ => entry.Action.ToString(),
        };
    }

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
            QualityIssueKind.DuplicatePage => text["Quality.Issue.Duplicate", issue.Page ?? 0, issue.DuplicateOf ?? 0],
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
                QualityIssueKind.DuplicatePage => text["Quality.Summary.Duplicates", Pages(kind)].Value,
                QualityIssueKind.MissingMetadata => text["Quality.Issue.Missing",
                    string.Join(separator, check.Issues.Where(i => i.Field is not null).Select(i => text.Required(i.Field!.Value)))].Value,
                _ => kind.ToString(),
            })
            .ToList();
    }

    /// <summary>
    /// Colour class per status (UAE Design System soft badge colours, defined in app.css); the label
    /// is always shown, so colour is never the only cue.
    /// </summary>
    public static string StatusClass(DocumentStatus status) => status switch
    {
        DocumentStatus.Processing => "status-processing",
        DocumentStatus.NeedsReview => "status-needs-review",
        DocumentStatus.Archived => "status-archived",
        DocumentStatus.Failed => "status-failed",
        _ => "status-cancelled",
    };

    public static string StatusTagClass(DocumentStatus status) => "status-tag " + StatusClass(status);

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

    /// <summary>A change with its sign; one that rounds to zero is shown as zero (not "-0.0").</summary>
    public static string FormatSigned(double value, string format)
    {
        var text = value.ToString(format, CultureInfo.CurrentCulture);
        if (double.Parse(text, NumberStyles.Number, CultureInfo.CurrentCulture) == 0)
            return 0d.ToString(format, CultureInfo.CurrentCulture);
        return (value > 0 ? "+" : string.Empty) + text;
    }
}
