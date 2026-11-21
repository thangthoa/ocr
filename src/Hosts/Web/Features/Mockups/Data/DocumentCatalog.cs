using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Application.Features.ListOcrJobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.History;

namespace OppOcr.Web.Features.Mockups.Data;

/// <param name="Documents">The newest OCR jobs as documents, followed by the samples and the changed history documents.</param>
/// <param name="JobsFromService">False while the OCR service cannot list jobs (this instance's own list is used).</param>
/// <param name="JobsTotal">How many jobs exist; more than <see cref="DocumentCatalog.MaxJobs"/> means only the newest are shown.</param>
/// <param name="JobsError">Why OCR jobs could not be loaded; the samples are still returned.</param>
public sealed record DocumentList(
    IReadOnlyList<DocumentRecord> Documents,
    bool JobsFromService,
    int JobsTotal,
    Error? JobsError);

/// <summary>
/// Every document at one moment: the ones kept in full (<see cref="Stored"/>: OCR jobs, samples, changed history
/// documents), the rest of the simulated history, and the figures of all of them. The lists page through it and
/// the dashboard and reports read its <see cref="Facts"/>, so they all count the same documents.
/// </summary>
public sealed class DocumentSnapshot
{
    internal DocumentSnapshot(DocumentList list, IReadOnlySet<int> changedHistory, ProjectFacts facts)
    {
        List = list;
        ChangedHistory = changedHistory;
        Facts = facts;
    }

    public DocumentList List { get; }

    public IReadOnlyList<DocumentRecord> Stored => List.Documents;

    public ProjectFacts Facts { get; }

    public DateOnly Today => Facts.Today;

    /// <summary>Documents per status, all of them.</summary>
    public IReadOnlyDictionary<DocumentStatus, int> StatusCounts =>
        Enum.GetValues<DocumentStatus>().ToDictionary(s => s, Facts.AllTime.Status);

    public int Total => Facts.AllTime.Documents;

    internal IReadOnlySet<int> ChangedHistory { get; }
}

/// <summary>One page of a document list, and how many documents match.</summary>
public sealed record DocumentPage(IReadOnlyList<DocumentRecord> Items, int Total);

/// <summary>
/// Documents = OCR jobs (through the OCR module's use cases) + what <see cref="DocumentStore"/> keeps
/// for them + the samples + the simulated history (<see cref="ProjectHistory"/>). The OCR module stays unaware of documents.
/// </summary>
public sealed class DocumentCatalog(
    ListOcrJobsHandler listJobs,
    GetOcrJobHandler getJob,
    DeleteOcrJobHandler deleteJob,
    DocumentStore store,
    ProjectHistory history,
    AuditLog audit,
    TimeProvider time)
{
    /// <summary>
    /// The newest jobs are filtered here, in memory, because metadata only exists in this app.
    /// One page of the service's list keeps that to a single request.
    /// </summary>
    public const int MaxJobs = ListOcrJobsQuery.MaxPageSize;

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>The documents kept in full: OCR jobs, samples and changed history documents.</summary>
    public async Task<DocumentList> ListAsync(CancellationToken cancellationToken)
    {
        var samples = store.Samples();
        var result = await listJobs.HandleAsync(new ListOcrJobsQuery(1, MaxJobs), cancellationToken);
        if (result.IsFailure)
            return new DocumentList(samples, JobsFromService: true, 0, result.Error);

        var jobs = result.Value.Jobs;
        var documents = jobs.Items.Select(store.FromJob).Concat(samples).ToList();
        return new DocumentList(documents, result.Value.FromService, jobs.TotalCount, null);
    }

    /// <summary>Every document now, with its figures; one call to the OCR service.</summary>
    public async Task<DocumentSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var list = await ListAsync(cancellationToken);
        var changed = store.ChangedHistory();
        var today = Today;
        var builder = new FactsBuilder();
        history.AddTo(builder, today);
        var firstDay = ProjectHistory.FirstDay(today).DayNumber;
        foreach (var index in changed)
            builder.Add(history.Entry(index), index, firstDay, sign: -1);
        foreach (var document in list.Documents)
            builder.Add(document, sign: 1, time.LocalTimeZone, today);

        var first = history.Enabled ? ProjectHistory.FirstDay(today) : builder.FirstDocumentDay() ?? today;
        return new DocumentSnapshot(list, changed, builder.Build(today, first));
    }

    /// <summary>
    /// A page of the documents matching <paramref name="query"/>: the OCR jobs of this app first, then the samples and
    /// the history merged in the same order. Only the documents of the page are built in full.
    /// </summary>
    public DocumentPage Query(DocumentSnapshot snapshot, DocumentQuery query, DocumentOrder order, int skip, int take)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        var real = Order(DocumentStore.Filter(snapshot.Stored.Where(d => !d.IsSample), query), order).ToList();
        var simulated = DocumentStore.Filter(snapshot.Stored.Where(d => d.IsSample), query)
            .Select(d => (Key: history.SortKey(d, order, snapshot.Today), Document: d))
            .OrderBy(d => Rank(d.Key, order))
            .ToList();

        // Every match is counted; only those on the page are built (a closure per match would cost a million allocations).
        var items = new List<DocumentRecord>(Math.Max(0, take));
        var position = 0;
        bool OnPage() => position >= skip && position < skip + take;

        foreach (var document in real)
        {
            if (OnPage())
                items.Add(document);
            position++;
        }

        var next = 0;
        foreach (var index in history.Match(query, order, snapshot.Today, snapshot.ChangedHistory))
        {
            var rank = Rank(history.SortKey(index, order), order);
            for (; next < simulated.Count && Rank(simulated[next].Key, order) <= rank; next++)
            {
                if (OnPage())
                    items.Add(simulated[next].Document);
                position++;
            }

            if (OnPage())
                items.Add(history.Materialize(index, snapshot.Today));
            position++;
        }

        for (; next < simulated.Count; next++)
        {
            if (OnPage())
                items.Add(simulated[next].Document);
            position++;
        }

        return new DocumentPage(items, position);
    }

    public async Task<Result<DocumentRecord>> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (store.FindSample(id) is { } sample)
            return sample;

        var result = await getJob.HandleAsync(new GetOcrJobQuery(id), cancellationToken);
        if (result.IsFailure)
            return result.Error!;
        return store.FromJob(result.Value);
    }

    /// <summary>Removes an OCR job (cancelling it if it is still queued) and what was stored for it.</summary>
    public async Task<Result> DeleteAsync(string id, string? fileName, string author, CancellationToken cancellationToken)
    {
        var result = await deleteJob.HandleAsync(new DeleteOcrJobCommand(id), cancellationToken);
        if (result.IsSuccess)
        {
            store.Forget(id);
            audit.Record(author, AuditAction.Deleted, id, fileName);
        }

        return result;
    }

    /// <summary>Ascending in list order: the newest orders count down.</summary>
    private static long Rank(long key, DocumentOrder order) => order == DocumentOrder.OldestUpload ? key : -key;

    private static IEnumerable<DocumentRecord> Order(IEnumerable<DocumentRecord> documents, DocumentOrder order) => order switch
    {
        DocumentOrder.OldestUpload => documents.OrderBy(d => d.UploadedAtUtc ?? DateTime.MaxValue),
        DocumentOrder.NewestApproval => documents.OrderByDescending(d => d.Approval?.AtUtc ?? DateTime.MinValue),
        _ => documents,
    };
}
