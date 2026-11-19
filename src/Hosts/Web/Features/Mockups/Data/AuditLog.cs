namespace OppOcr.Web.Features.Mockups.Data;

public enum AuditAction
{
    SignedIn,
    SignInFailed,
    Uploaded,
    Edited,
    Restored,
    StatusChanged,
    Deleted,
}

/// <param name="Version">The version an edit or restore created.</param>
/// <param name="Status">The status a document was moved to.</param>
public sealed record AuditEntry(
    DateTime AtUtc,
    string User,
    AuditAction Action,
    string? DocumentId = null,
    string? DocumentName = null,
    string? BatchId = null,
    string? Version = null,
    DocumentStatus? Status = null);

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
        string? batchId = null, string? version = null, DocumentStatus? status = null)
    {
        var entry = new AuditEntry(time.GetUtcNow().UtcDateTime, user, action, documentId, documentName, batchId, version, status);
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
