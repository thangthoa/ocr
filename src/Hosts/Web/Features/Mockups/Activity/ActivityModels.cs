using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Activity;

/// <summary>What an activity log entry is about, for filtering and the summary figures.</summary>
public enum ActivityCategory
{
    Security,
    DocumentAccess,
    DocumentChange,
    Administration,
    System,
}

public enum ActivityResult
{
    Success,
    Failed,
    Denied,
}

/// <summary>One entry of the activity log, in the order it was written, with its place in the hash chain.</summary>
/// <param name="Sequence">Position in the log, from 1 for the oldest entry.</param>
/// <param name="Hash">SHA-256 of the previous hash and this entry (hex); changing or removing an entry breaks every hash after it.</param>
/// <param name="IsSample">True for the sample history; false for what was done in this app since it started.</param>
public sealed record ActivityRow(long Sequence, AuditEntry Entry, string PreviousHash, string Hash, bool IsSample)
{
    public ActivityCategory Category => ActivityRules.Category(Entry.Action);

    public ActivityResult Result => ActivityRules.Result(Entry.Action);

    /// <summary>The id the SIEM gets for this entry (syslog/CEF forwarding is planned; the mockup only shows it).</summary>
    public string SiemEventId => "OCR-" + Sequence.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
}

/// <param name="Text">Matched against the user, the document, the details and the IP address.</param>
public sealed record ActivityQuery(
    string? Text = null,
    string? User = null,
    ActivityCategory? Category = null,
    ActivityResult? Result = null,
    DateOnly? From = null,
    DateOnly? To = null);

public static class ActivityRules
{
    /// <summary>How long the activity log is kept (proposal; the mockup keeps only what it holds in memory).</summary>
    public const int RetentionYears = 7;

    public static ActivityCategory Category(AuditAction action) => action switch
    {
        AuditAction.SignedIn or AuditAction.SignedOut or AuditAction.SignInFailed or AuditAction.OtpFailed
            or AuditAction.AccountLocked or AuditAction.AccessDenied => ActivityCategory.Security,
        AuditAction.Viewed or AuditAction.Searched or AuditAction.Downloaded or AuditAction.Printed
            or AuditAction.Exported or AuditAction.ReportExported or AuditAction.ActivityExported => ActivityCategory.DocumentAccess,
        AuditAction.Uploaded or AuditAction.Edited or AuditAction.EditedInWord or AuditAction.Restored
            or AuditAction.MetadataChanged or AuditAction.StatusChanged or AuditAction.Deleted
            or AuditAction.PagesInserted or AuditAction.PageReplaced or AuditAction.PageDeleted => ActivityCategory.DocumentChange,
        AuditAction.QualitySettingsChanged or AuditAction.KpiSettingsChanged or AuditAction.UserAdded
            or AuditAction.UserChanged or AuditAction.RolesChanged => ActivityCategory.Administration,
        _ => ActivityCategory.System,
    };

    public static ActivityResult Result(AuditAction action) => action switch
    {
        AuditAction.SignInFailed or AuditAction.OtpFailed or AuditAction.AccountLocked or AuditAction.OcrFailed => ActivityResult.Failed,
        AuditAction.AccessDenied => ActivityResult.Denied,
        _ => ActivityResult.Success,
    };
}
