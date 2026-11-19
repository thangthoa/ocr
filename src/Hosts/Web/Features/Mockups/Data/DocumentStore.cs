using System.Globalization;
using Modules.Ocr.Domain.Jobs;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>
/// In-memory store for the documents mockup: the sample documents, and for real OCR jobs whatever
/// the OCR service cannot keep (review status, metadata, edited text, versions). Everything is
/// lost when the app restarts and is shared by everyone using this instance (no database, ADR 0003).
/// Every change is written to the <see cref="AuditLog"/>.
/// </summary>
public sealed class DocumentStore(TimeProvider time, CaptureBatchStore batches, AuditLog audit)
{
    public const string OcrAuthor = "ocr-service";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DocumentRecord> _records =
        SampleDocumentSeed.Create().ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DocumentRecord> Samples()
    {
        lock (_lock)
        {
            return _records.Values.Where(r => r.IsSample).ToList();
        }
    }

    public DocumentRecord? FindSample(string id)
    {
        lock (_lock)
        {
            return _records.TryGetValue(id, out var record) && record.IsSample ? record : null;
        }
    }

    /// <summary>
    /// The document view of an OCR job. A finished job read with its pages gets a record here the
    /// first time (version 1.0 = the OCR text), so it can be reviewed, edited and versioned.
    /// </summary>
    public DocumentRecord FromJob(OcrJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            _records.TryGetValue(job.Id.Value, out var saved);
            var batch = batches.ForJob(job.Id.Value);
            var metadata = saved?.Metadata ?? DefaultMetadata(job.FileName) with { Department = batch?.Department };
            if (saved is null && job.Status == OcrJobStatus.Done && job.Pages.Count > 0)
            {
                saved = FirstVersion(job, metadata, time.GetUtcNow().UtcDateTime);
                _records[job.Id.Value] = saved;
            }

            var status = job.Status switch
            {
                OcrJobStatus.Queued or OcrJobStatus.Running => DocumentStatus.Processing,
                OcrJobStatus.Failed => DocumentStatus.Failed,
                OcrJobStatus.Cancelled => DocumentStatus.Cancelled,
                _ => saved?.Status ?? DocumentStatus.NeedsReview,
            };

            return new DocumentRecord(job.Id.Value, IsSample: false, job.FileName, job.PagesTotal, SizeBytes: null,
                job.CreatedAtUtc, batch?.UploadedBy, Confidence: null, status, metadata,
                saved?.PageTexts ?? OcrPageTexts(job), saved?.Versions ?? [], job, batch?.Id);
        }
    }

    /// <summary>Saves edited text and metadata as a new version.</summary>
    public void SaveEdit(string id, IReadOnlyList<string> pageTexts, DocumentMetadata metadata, string author, string? note)
    {
        lock (_lock)
        {
            var record = Get(id, forEdit: true);
            List<string> pages = [.. pageTexts];
            var version = new DocumentVersion(NextNumber(record), time.GetUtcNow().UtcDateTime, author,
                DocumentVersionChange.Edited, null, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), pages, metadata);
            _records[record.Id] = record with { PageTexts = pages, Metadata = metadata, Versions = [.. record.Versions, version] };
            audit.Record(author, AuditAction.Edited, record.Id, record.FileName, version: version.Number);
        }
    }

    /// <summary>Copies an older version forward as the newest one; history is never rewritten.</summary>
    public string Restore(string id, string versionNumber, string author)
    {
        lock (_lock)
        {
            var record = Get(id, forEdit: true);
            var source = record.Versions.FirstOrDefault(v => v.Number == versionNumber)
                ?? throw new InvalidOperationException($"Document {id} has no version {versionNumber}.");
            var version = new DocumentVersion(NextNumber(record), time.GetUtcNow().UtcDateTime, author,
                DocumentVersionChange.Restored, source.Number, null, source.PageTexts, source.Metadata);
            _records[record.Id] = record with { PageTexts = source.PageTexts, Metadata = source.Metadata, Versions = [.. record.Versions, version] };
            audit.Record(author, AuditAction.Restored, record.Id, record.FileName, version: version.Number);
            return version.Number;
        }
    }

    public void ChangeStatus(string id, DocumentStatus status, string author)
    {
        lock (_lock)
        {
            var record = Get(id);
            _records[record.Id] = record with { Status = status };
            audit.Record(author, AuditAction.StatusChanged, record.Id, record.FileName, status: status);
        }
    }

    /// <summary>Forgets what was stored for a deleted OCR job. Samples are never removed.</summary>
    public void Forget(string id)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(id, out var record) && !record.IsSample)
                _records.Remove(id);
        }
    }

    /// <summary>Filters and orders documents: real ones before samples, then newest change first.</summary>
    public static IReadOnlyList<DocumentRecord> Filter(IEnumerable<DocumentRecord> documents, DocumentQuery query)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(query);
        return documents
            .Where(d => query.Status is null || d.Status == query.Status)
            .Where(d => query.Department is null || d.Metadata.Department == query.Department)
            .Where(d => query.Kind is null || d.Metadata.Kind == query.Kind)
            .Where(d => query.From is null || d.Metadata.DocumentDate >= query.From)
            .Where(d => query.To is null || d.Metadata.DocumentDate <= query.To)
            .Where(d => Matches(d, query.Text, query.Field))
            .OrderBy(d => d.IsSample)
            .ThenByDescending(d => d.UpdatedAtUtc ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>Only documents between OCR and archiving change; the pages never offer it otherwise.</summary>
    private DocumentRecord Get(string id, bool forEdit = false)
    {
        if (!_records.TryGetValue(id, out var record))
            throw new InvalidOperationException($"Document {id} has nothing stored; open it first.");
        if (forEdit && !record.CanEdit)
            throw new InvalidOperationException($"Document {id} cannot be edited in status {record.Status}.");
        return record;
    }

    private static DocumentRecord FirstVersion(OcrJob job, DocumentMetadata metadata, DateTime nowUtc)
    {
        var pages = OcrPageTexts(job);
        var ocr = new DocumentVersion("1.0", job.FinishedAtUtc ?? job.CreatedAtUtc ?? nowUtc, OcrAuthor,
            DocumentVersionChange.OcrResult, null, null, pages, metadata);
        return new DocumentRecord(job.Id.Value, IsSample: false, job.FileName, job.PagesTotal, null, job.CreatedAtUtc, null, null,
            DocumentStatus.NeedsReview, metadata, pages, [ocr]);
    }

    private static List<string> OcrPageTexts(OcrJob job) =>
        job.Pages.OrderBy(p => p.PageNumber).Select(p => p.Text).ToList();

    private static DocumentMetadata DefaultMetadata(string fileName) =>
        new(Path.GetFileNameWithoutExtension(fileName), string.Empty, null, null, null, DocumentConfidentiality.Internal, []);

    private static string NextNumber(DocumentRecord record)
    {
        if (record.CurrentVersion is not { } current)
            return "1.0";

        var parts = current.Number.Split('.');
        var major = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minor = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor + 1}");
    }

    private static bool Matches(DocumentRecord document, string? text, DocumentSearchField field)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var term = text.Trim();
        bool Has(string value) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
        var metadata = document.Metadata;

        return field switch
        {
            DocumentSearchField.FileName => Has(document.FileName) || Has(document.Id),
            DocumentSearchField.Title => Has(metadata.Title),
            DocumentSearchField.Reference => Has(metadata.Reference),
            DocumentSearchField.Keyword => metadata.Keywords.Any(Has),
            DocumentSearchField.Content => Has(document.Text),
            _ => Has(document.FileName) || Has(document.Id) || Has(metadata.Title) || Has(metadata.Reference)
                || metadata.Keywords.Any(Has) || Has(document.Text),
        };
    }
}
