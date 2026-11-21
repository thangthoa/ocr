using System.Globalization;
using Microsoft.Extensions.Options;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>
/// In-memory store for the documents mockup: for real OCR jobs whatever the OCR service cannot keep
/// (review status, metadata, edited text, versions), the sample documents when they are turned on
/// (<see cref="DocumentSampleOptions"/>), and a copy of each document of the simulated history
/// (<see cref="ProjectHistory"/>) once someone changes it. Everything is
/// lost when the app restarts and is shared by everyone using this instance (no database, ADR 0003).
/// Every change is written to the <see cref="AuditLog"/>.
/// </summary>
/// <remarks>
/// Samples and history documents are both "simulated" (<see cref="DocumentRecord.IsSample"/>): they have no OCR
/// job behind them, and are opened, reviewed and approved the same way.
/// </remarks>
public sealed class DocumentStore(
    TimeProvider time,
    CaptureBatchStore batches,
    AuditLog audit,
    QualitySettingsStore quality,
    IOptions<DocumentSampleOptions> samples,
    ProjectHistory history)
{
    public const string OcrAuthor = "ocr-service";

    /// <summary>Who approves the documents that pass the automatic check.</summary>
    public const string QualityCheckAuthor = "quality-check";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DocumentRecord> _records =
        (samples.Value.Enabled ? SampleDocumentSeed.Create() : []).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>History documents stored here because someone changed them (by index).</summary>
    private readonly HashSet<int> _changedHistory = [];

    /// <summary>The samples, and the history documents someone changed.</summary>
    public IReadOnlyList<DocumentRecord> Samples()
    {
        lock (_lock)
        {
            return _records.Values.Where(r => r.IsSample).ToList();
        }
    }

    /// <summary>History documents stored with changes: the lists and figures use the stored copy instead.</summary>
    public IReadOnlySet<int> ChangedHistory()
    {
        lock (_lock)
        {
            return _changedHistory.ToHashSet();
        }
    }

    /// <summary>A sample or history document: the stored copy if there is one, otherwise the history as simulated.</summary>
    public DocumentRecord? FindSample(string id)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(id, out var record))
                return record.IsSample ? record : null;
            return history.TryIndex(id, out var index) ? history.Materialize(index) : null;
        }
    }

    /// <summary>
    /// The document view of an OCR job. A finished job read with its pages gets a record here the
    /// first time (version 1.0 = the OCR text), so it can be reviewed, edited and versioned. That is
    /// also when the automatic check runs: the document is approved, and so archived, if it passes (and the settings
    /// allow it), otherwise it waits in the review queue.
    /// </summary>
    /// <remarks>
    /// When the job's pages were changed since (inserted, replaced or deleted: <see cref="OcrJob.Edits"/>), its new
    /// text becomes the next version once the job is done again, and the check runs again on the new pages.
    /// </remarks>
    public DocumentRecord FromJob(OcrJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            _records.TryGetValue(job.Id.Value, out var saved);
            var batch = batches.ForJob(job.Id.Value);
            var metadata = saved?.Metadata ?? DefaultMetadata(job.FileName);
            var hasText = job.Status == OcrJobStatus.Done && job.Pages.Count > 0;
            if (saved is null && hasText)
            {
                var now = time.GetUtcNow().UtcDateTime;
                saved = CheckQuality(FirstVersion(job, metadata, now), job, now, mayApprove: true);
                _records[job.Id.Value] = saved;
            }
            else if (saved is not null && hasText && job.Edits != saved.OcrEdits)
            {
                saved = PagesChanged(saved, job, time.GetUtcNow().UtcDateTime);
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
                saved?.PageConfidences, saved?.Quality, saved?.OcrEdits ?? job.Edits);
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
    /// Links the version made after the pages changed to the Word file the OCR service made for the new pages, once
    /// that file was taken (see <see cref="DocumentVersion.WordVersion"/>).
    /// </summary>
    public void AttachWordVersion(string id, string versionNumber, int wordVersion)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(id, out var record))
                return;
            var versions = record.Versions
                .Select(v => v.Number == versionNumber && v.Change == DocumentVersionChange.PagesChanged ? v with { WordVersion = wordVersion } : v)
                .ToList();
            _records[id] = record with { Versions = versions };
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
            var current = _records.GetValueOrDefault(id) ?? (history.TryIndex(id, out var index) ? history.Materialize(index) : null);
            if (current is not { CanEdit: true })
                return;
            var record = Get(id);

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
            if (_records.TryGetValue(id, out var record))
                return record.FileName;
            return history.TryIndex(id, out var index) ? history.Materialize(index).FileName : null;
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

    /// <summary>
    /// Only documents in review can be edited; the pages never offer it otherwise. A history document is copied
    /// here the first time it is changed.
    /// </summary>
    private DocumentRecord Get(string id, bool forEdit = false)
    {
        if (!_records.TryGetValue(id, out var record) && history.TryIndex(id, out var index))
        {
            record = history.Materialize(index);
            _records[record.Id] = record;
            _changedHistory.Add(index);
        }

        if (record is null)
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
            DocumentStatus.NeedsReview, metadata, pages, [ocr], OcrEdits: job.Edits);
    }

    /// <summary>
    /// The OCR text after the pages changed replaces the current text as a new version: the service only has OCR
    /// text, so edits made since the last OCR result are not carried over (they stay in the earlier versions).
    /// The changed document is reviewed again, even if it was archived (pages are only changed in the app while
    /// it is in review; this covers changes made through the API directly).
    /// </summary>
    private DocumentRecord PagesChanged(DocumentRecord record, OcrJob job, DateTime nowUtc)
    {
        var pages = OcrPageTexts(job);
        var version = new DocumentVersion(NextNumber(record), nowUtc, OcrAuthor, DocumentVersionChange.PagesChanged, null, null, pages, record.Metadata);
        var changed = record with
        {
            Pages = job.PagesTotal,
            PageTexts = pages,
            Versions = [.. record.Versions, version],
            Status = DocumentStatus.NeedsReview,
            Approval = null,
            OcrEdits = job.Edits,
        };

        // Someone is reviewing it: the new result is checked, but never approved and archived under their hands.
        return CheckQuality(changed, job, nowUtc, mayApprove: false);
    }

    /// <summary>
    /// Review round 1, with the settings in force now; the result is kept with the document. Passing archives the
    /// document only when <paramref name="mayApprove"/> and the settings allow it.
    /// </summary>
    private DocumentRecord CheckQuality(DocumentRecord record, OcrJob job, DateTime nowUtc, bool mayApprove)
    {
        var confidences = SimulatedConfidence.ForJob(job);
        var pages = confidences
            .Select((confidence, index) =>
            {
                var page = job.Pages.FirstOrDefault(p => p.PageNumber == index + 1);
                var duplicateOf = job.Duplicates.Where(d => d.SecondPage == index + 1).Select(d => (int?)d.FirstPage).Min();
                return new PageQuality(index + 1, confidence, !string.IsNullOrWhiteSpace(page?.Text), page?.Warnings.Count ?? 0, duplicateOf);
            })
            .ToList();
        var documentConfidence = QualityCheck.DocumentConfidence(confidences);
        var settings = quality.Current;
        var result = QualityCheck.Run(pages, documentConfidence, record.Metadata, settings, nowUtc);

        var approve = result.Passed && settings.AutoApprove && mayApprove;
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
