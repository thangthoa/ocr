using System.Globalization;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>A batch ticket: what one capture session was expected to contain and what it produced.</summary>
/// <param name="JobIds">OCR jobs created by the batch, in upload order.</param>
public sealed record CaptureBatch(
    string Id,
    DateTime CreatedAtUtc,
    string UploadedBy,
    string? BoxBarcode,
    DocumentDepartment? Department,
    int? ExpectedFiles,
    IReadOnlyList<string> JobIds,
    int FailedFiles)
{
    public int UploadedFiles => JobIds.Count;

    /// <summary>Uploaded minus expected: negative when files are missing, positive when there are extra ones.</summary>
    public int? Difference => ExpectedFiles is { } expected ? UploadedFiles - expected : null;
}

/// <summary>Batches recorded by the Capture page (in memory, like the rest of the mockup).</summary>
public sealed class CaptureBatchStore(TimeProvider time)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CaptureBatch> _byJob = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _saved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A readable, sortable id such as <c>B-20261119-093005</c>. Ids are not reserved (a page may be
    /// rendered twice); a suffix only avoids reusing the id of a batch already saved.
    /// </summary>
    public string NewBatchId()
    {
        var id = "B-" + time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        lock (_lock)
        {
            var candidate = id;
            for (var n = 2; _saved.Contains(candidate); n++)
                candidate = string.Create(CultureInfo.InvariantCulture, $"{id}-{n}");
            return candidate;
        }
    }

    public void Save(CaptureBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_lock)
        {
            _saved.Add(batch.Id);
            foreach (var jobId in batch.JobIds)
                _byJob[jobId] = batch;
        }
    }

    public CaptureBatch? ForJob(string jobId)
    {
        lock (_lock)
        {
            return _byJob.GetValueOrDefault(jobId);
        }
    }
}
