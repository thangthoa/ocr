using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Infrastructure.Registry;

/// <summary>
/// Thread-safe, bounded, in-memory record of jobs submitted through this instance. Lost on
/// restart by design: the OCR service remains the source of truth (ADR 0003).
/// </summary>
internal sealed class InMemoryOcrJobRegistry(int capacity) : IOcrJobRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<OcrJobId, Entry> _jobs = [];
    private long _sequence;

    public void Add(OcrJob job)
    {
        lock (_lock)
        {
            _jobs[job.Id] = new Entry(job, ++_sequence);
            if (_jobs.Count > capacity)
            {
                // Evict the oldest finished job first; never drop one still being tracked if avoidable.
                var victim = _jobs.Values.Where(e => e.Job.IsFinished).MinBy(e => e.Order)
                    ?? _jobs.Values.MinBy(e => e.Order)!;
                _jobs.Remove(victim.Job.Id);
            }
        }
    }

    public OcrJob? Update(OcrJob job)
    {
        lock (_lock)
        {
            if (!_jobs.TryGetValue(job.Id, out var entry))
                return null;
            _jobs[job.Id] = entry with { Job = job };
            return entry.Job;
        }
    }

    public bool Remove(OcrJobId jobId)
    {
        lock (_lock)
            return _jobs.Remove(jobId);
    }

    public OcrJob? Find(OcrJobId jobId)
    {
        lock (_lock)
            return _jobs.TryGetValue(jobId, out var entry) ? entry.Job : null;
    }

    public IReadOnlyList<OcrJob> Unfinished()
    {
        lock (_lock)
            return [.. _jobs.Values.Where(e => !e.Job.IsFinished).OrderBy(e => e.Order).Select(e => e.Job)];
    }

    public PagedResult<OcrJob> List(int page, int pageSize, OcrJobStatus? status)
    {
        lock (_lock)
        {
            var matching = _jobs.Values
                .Where(e => status is null || e.Job.Status == status)
                .OrderByDescending(e => e.Order)
                .Select(e => e.Job)
                .ToList();
            var items = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new PagedResult<OcrJob>(items, page, pageSize, matching.Count);
        }
    }

    private sealed record Entry(OcrJob Job, long Order);
}
