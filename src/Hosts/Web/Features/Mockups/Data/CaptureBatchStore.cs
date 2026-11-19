using System.Globalization;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>One upload from the Capture page: who sent which document, and when.</summary>
/// <param name="JobIds">OCR jobs created by the upload (one per upload since files are combined into one document).</param>
public sealed record CaptureBatch(
    string Id,
    DateTime CreatedAtUtc,
    string UploadedBy,
    IReadOnlyList<string> JobIds);

/// <summary>Uploads recorded by the Capture page (in memory, like the rest of the mockup).</summary>
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
