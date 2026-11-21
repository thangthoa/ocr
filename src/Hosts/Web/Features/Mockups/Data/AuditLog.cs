namespace OppOcr.Web.Features.Mockups.Data;

public enum AuditAction
{
    SignedIn,
    SignInFailed,
    OtpFailed,
    Uploaded,
    Edited,
    Restored,
    StatusChanged,
    Deleted,
    EditedInWord,
    Downloaded,
    Exported,
    QualityChecked,
    QualitySettingsChanged,
    KpiSettingsChanged,
    ReportExported,
    UserAdded,
    UserChanged,
    RolesChanged,
    SignedOut,
    AccountLocked,
    AccessDenied,
    Viewed,
    Searched,
    Printed,
    MetadataChanged,
    OcrFailed,
    OcrRetried,
    ActivityExported,
    PagesInserted,
    PageReplaced,
    PageDeleted,
}

/// <param name="Version">The version an edit or restore created; the page number a page change was made at.</param>
/// <param name="Status">The status a document was moved to.</param>
/// <param name="Detail">
/// Extra detail for the action (the file format of a download, the number of exported documents, the number of
/// issues the quality check found, the search terms, the confidentiality of a document that was refused).
/// </param>
/// <param name="IpAddress">Where the request came from; the sample history has it, this mockup's own entries do not.</param>
/// <param name="Client">Browser and system, or the service that acted.</param>
public sealed record AuditEntry(
    DateTime AtUtc,
    string User,
    AuditAction Action,
    string? DocumentId = null,
    string? DocumentName = null,
    string? BatchId = null,
    string? Version = null,
    DocumentStatus? Status = null,
    string? Detail = null,
    string? IpAddress = null,
    string? Client = null);

/// <summary>
/// Activity log for the mockup: what people did in this app since it started. In memory only and
/// capped; the real audit trail (append-only, forwarded to the SIEM) comes with persistence.
/// </summary>
public sealed class AuditLog(TimeProvider time)
{
    public const int Capacity = 500;

    private readonly Lock _lock = new();
    private readonly LinkedList<AuditEntry> _entries = new();

    public void Record(string user, AuditAction action, string? documentId = null, string? documentName = null,
        string? batchId = null, string? version = null, DocumentStatus? status = null, string? detail = null)
    {
        var entry = new AuditEntry(time.GetUtcNow().UtcDateTime, user, action, documentId, documentName, batchId, version, status, detail);
        lock (_lock)
        {
            _entries.AddFirst(entry);
            if (_entries.Count > Capacity)
                _entries.RemoveLast();
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<AuditEntry> Latest(int count = 100)
    {
        lock (_lock)
        {
            return _entries.Take(count).ToList();
        }
    }
}
