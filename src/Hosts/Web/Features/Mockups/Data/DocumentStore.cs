using System.Globalization;
using Microsoft.Extensions.Options;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>
/// In-memory store for the documents mockup: for real OCR jobs whatever the OCR service cannot keep
/// (review status, metadata, edited text, versions), and the sample documents when they are turned on
/// (<see cref="DocumentSampleOptions"/>). Everything is
/// lost when the app restarts and is shared by everyone using this instance (no database, ADR 0003).
/// Every change is written to the <see cref="AuditLog"/>.
/// </summary>
public sealed class DocumentStore(
    TimeProvider time,
    CaptureBatchStore batches,
    AuditLog audit,
    QualitySettingsStore quality,
    IOptions<DocumentSampleOptions> samples)
{
    public const string OcrAuthor = "ocr-service";

    /// <summary>Who approves the documents that pass the automatic check.</summary>
    public const string QualityCheckAuthor = "quality-check";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DocumentRecord> _records =
        (samples.Value.Enabled ? SampleDocumentSeed.Create() : []).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

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
    /// first time (version 1.0 = the OCR text), so it can be reviewed, edited and versioned. That is
    /// also when the automatic check runs: the document is approved, and so archived, if it passes (and the settings
    /// allow it), otherwise it waits in the review queue.
    /// </summary>
    public DocumentRecord FromJob(OcrJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            _records.TryGetValue(job.Id.Value, out var saved);
            var batch = batches.ForJob(job.Id.Value);
            var metadata = saved?.Metadata ?? DefaultMetadata(job.FileName);
            if (saved is null && job.Status == OcrJobStatus.Done && job.Pages.Count > 0)
            {
                var now = time.GetUtcNow().UtcDateTime;
                saved = CheckQuality(FirstVersion(job, metadata, now), job, now);
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
                job.CreatedAtUtc, batch?.UploadedBy, saved?.Confidence, status, metadata,
                saved?.PageTexts ?? OcrPageTexts(job), saved?.Versions ?? [], job, batch?.Id, saved?.Approval,
                saved?.PageConfidences, saved?.Quality);
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

    /// <summary>
    /// Archiving a document approves it: who did it and when are recorded (an already archived
    /// document keeps its approval). Any other status (sent back to review, OCR run again) clears it.
    /// </summary>
    public void ChangeStatus(string id, DocumentStatus status, string author)
    {
        lock (_lock)
        {
            var record = Get(id);
            var approval = status switch
            {
                DocumentStatus.Archived when record.Status == DocumentStatus.Archived => record.Approval,
                DocumentStatus.Archived => new DocumentApproval(author, time.GetUtcNow().UtcDateTime),
                _ => null,
            };
            _records[record.Id] = record with { Status = status, Approval = approval };
            audit.Record(author, AuditAction.StatusChanged, record.Id, record.FileName, status: status);
        }
    }

    /// <summary>
    /// Takes the text of a file saved in the Word editor as the document's text (for preview and search).
    /// Ignored when nothing is stored for the document or it can no longer be edited.
    /// </summary>
    public void RecordWordEdit(string id, IReadOnlyList<string> pageTexts, string author, int wordVersion)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(id, out var record) || !record.CanEdit)
                return;

            List<string> pages = [.. pageTexts];
            var version = new DocumentVersion(NextNumber(record), time.GetUtcNow().UtcDateTime, author,
                DocumentVersionChange.EditedInWord, null, null, pages, record.Metadata, wordVersion);
            _records[record.Id] = record with { PageTexts = pages, Versions = [.. record.Versions, version] };
        }
    }

    public string? FileNameOf(string id)
    {
        lock (_lock)
        {
            return _records.TryGetValue(id, out var record) ? record.FileName : null;
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
            .Where(d => query.Confidentiality is null || d.Metadata.Confidentiality == query.Confidentiality)
            .Where(d => query.ApprovedFrom is null || ApprovedOn(d) >= query.ApprovedFrom)
            .Where(d => query.ApprovedTo is null || ApprovedOn(d) <= query.ApprovedTo)
            .Where(d => Matches(d, query.Text, query.Field))
            .OrderBy(d => d.IsSample)
            .ThenByDescending(d => d.UpdatedAtUtc ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>The local day of approval, as the pages show it.</summary>
    private static DateOnly? ApprovedOn(DocumentRecord document) =>
        document.Approval is { } approval ? DateOnly.FromDateTime(approval.AtUtc.ToLocalTime()) : null;

    /// <summary>Only documents in review can be edited; the pages never offer it otherwise.</summary>
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

    /// <summary>Review round 1, with the settings in force now; the result is kept with the document.</summary>
    private DocumentRecord CheckQuality(DocumentRecord record, OcrJob job, DateTime nowUtc)
    {
        var confidences = SimulatedConfidence.ForJob(job);
        var pages = confidences
            .Select((confidence, index) =>
            {
                var page = job.Pages.FirstOrDefault(p => p.PageNumber == index + 1);
                return new PageQuality(index + 1, confidence, !string.IsNullOrWhiteSpace(page?.Text), page?.Warnings.Count ?? 0);
            })
            .ToList();
        var documentConfidence = QualityCheck.DocumentConfidence(confidences);
        var settings = quality.Current;
        var result = QualityCheck.Run(pages, documentConfidence, record.Metadata, settings, nowUtc);

        var approve = result.Passed && settings.AutoApprove;
        var status = approve ? DocumentStatus.Archived : DocumentStatus.NeedsReview;
        audit.Record(QualityCheckAuthor, AuditAction.QualityChecked, record.Id, record.FileName, status: status,
            detail: result.Issues.Count.ToString(CultureInfo.InvariantCulture));
        return record with
        {
            Status = status,
            Approval = approve ? new DocumentApproval(QualityCheckAuthor, nowUtc) : null,
            Confidence = documentConfidence,
            PageConfidences = confidences,
            Quality = result,
        };
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
