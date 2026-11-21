using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.History;

/// <summary>Orders of the document lists; each list shows the real documents of this app first.</summary>
public enum DocumentOrder
{
    /// <summary>All Documents: newest first.</summary>
    Newest,

    /// <summary>Review Queue: oldest upload first.</summary>
    OldestUpload,

    /// <summary>Document Archive: newest approval first.</summary>
    NewestApproval,
}

/// <summary>
/// The simulated history of the digitization project: <see cref="HistoryProfile.Documents"/> documents scanned in
/// the <see cref="HistoryProfile.Days"/> days before today, run through the automatic check and a simulated review
/// queue, so every figure (statuses, approvals, review backlog, per operator and per reviewer) follows from the
/// documents themselves. Built once, the first time it is needed, and the same on every run; its dates follow
/// the current day. The documents behave like the samples: they can be opened, reviewed and approved
/// (<see cref="DocumentStore"/> keeps a copy of a document once someone changes it).
/// </summary>
public sealed class ProjectHistory
{
    private const int ReviewSalt = 21;
    private const int SecondsPerHour = 3600;

    /// <summary>The history depends only on its size, so it is built once per process and shared.</summary>
    private static readonly ConcurrentDictionary<int, Lazy<Built>> Cache = new();

    private readonly TimeProvider _time;

    public ProjectHistory(IOptions<HistoryOptions> options, IOptions<DocumentSampleOptions> samples, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(samples);
        _time = time;
        Enabled = options.Value.Enabled;
        // The sample documents belong to the project too: with them on, the history has as many documents fewer.
        SimulatedCount = HistoryProfile.Documents - (samples.Value.Enabled ? SampleDocumentSeed.Create().Count : 0);
        Count = Enabled ? SimulatedCount : 0;
    }

    public bool Enabled { get; }

    /// <summary>How many history documents the lists and figures have (none when the history is off).</summary>
    public int Count { get; }

    /// <summary>
    /// The size of the simulation, even when the lists leave it out: the sample activity log is made from it either
    /// way (with the history off its documents exist only in the log).
    /// </summary>
    internal int SimulatedCount { get; }

    private Built? _simulation;

    private Built Simulation => _simulation ??=
        Cache.GetOrAdd(SimulatedCount, count => new Lazy<Built>(() => Build(count), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>The first day of the history (the project start) when today is <paramref name="today"/>.</summary>
    public static DateOnly FirstDay(DateOnly today) => today.AddDays(-HistoryProfile.Days);

    /// <summary>The upload batch of a history document: one per operator and hour (<c>B-20261007-0900-04</c>).</summary>
    internal static string BatchId(DateOnly date, int hour, int uploader) =>
        string.Create(CultureInfo.InvariantCulture, $"B-{date:yyyyMMdd}-{hour:00}00-{uploader + 1:00}");

    /// <summary>How long a reviewer worked on a flagged document, in seconds: 1.5 to 3.5 minutes at an average pace.</summary>
    internal static int ReviewSeconds(int index, int reviewer) =>
        (int)Math.Round((90 + HistoryRandom.Below(index, ReviewSalt, 121)) * HistoryProfile.Reviewers[reviewer].Pace);

    public bool TryIndex(string? id, out int index) => HistoryText.TryIndex(id, out index) && index < Count;

    public bool Contains(string? id) => TryIndex(id, out _);

    internal HistoryEntry Entry(int index) => Simulation.Entries[index];

    /// <summary>The documents uploaded on a history day (0 is the project start), as an index range.</summary>
    internal (int Start, int End) DayRange(int day)
    {
        var entries = Simulation.Entries;
        int First(int second)
        {
            int low = 0, high = entries.Length;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (entries[middle].Uploaded < second)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        return (First(day * HistoryProfile.SecondsPerDay), First((day + 1) * HistoryProfile.SecondsPerDay));
    }

    /// <summary>Builds the history now rather than on the first page that needs it.</summary>
    public void Warm() => _ = Simulation;

    /// <summary>Adds every history document to <paramref name="builder"/>, on the dates of <paramref name="today"/>.</summary>
    internal void AddTo(FactsBuilder builder, DateOnly today)
    {
        if (Enabled)
            builder.Add(Simulation.Facts, FirstDay(today).DayNumber);
    }

    /// <summary>The full document, as the pages show it.</summary>
    public DocumentRecord Materialize(int index) => Materialize(index, Today);

    public DocumentRecord Materialize(int index, DateOnly today)
    {
        var entry = Entry(index);
        var firstDay = FirstDay(today);
        var metadata = HistoryText.Metadata(index, entry);
        var operatorName = HistoryProfile.Operators[entry.Uploader].UserName;
        var uploaded = ToUtc(firstDay, entry.Uploaded);
        var size = entry.Pages * (350_000L + HistoryRandom.Below(index, 43, 250_000));
        var batch = BatchId(firstDay.AddDays(entry.Uploaded / HistoryProfile.SecondsPerDay), entry.Uploaded % HistoryProfile.SecondsPerDay / SecondsPerHour,
            entry.Uploader);

        if (entry.Status == DocumentStatus.Failed)
        {
            return new DocumentRecord(HistoryText.Id(index), IsSample: true, HistoryText.FileName(index, entry.Department, entry.Kind),
                entry.Pages, size, uploaded, operatorName, null, DocumentStatus.Failed, metadata, [], [], BatchId: batch);
        }

        var texts = HistoryText.PageTexts(index, entry, metadata);
        var checkedAt = ToUtc(firstDay, entry.Checked);
        List<DocumentVersion> versions =
        [
            new("1.0", checkedAt, DocumentStore.OcrAuthor, DocumentVersionChange.OcrResult, null, null,
                entry.Edited ? HistoryText.OcrTexts(texts) : texts, metadata),
        ];
        DocumentApproval? approval = null;
        if (entry.Approved >= 0)
        {
            var approvedAt = ToUtc(firstDay, entry.Approved);
            var approver = entry.ApprovedByCheck ? DocumentStore.QualityCheckAuthor : HistoryProfile.Reviewers[entry.Approver].UserName;
            approval = new DocumentApproval(approver, approvedAt);
            if (entry.Edited)
            {
                // The reviewer corrected the text in Word (the activity log has the same edit) and approved it a minute later.
                versions.Add(new DocumentVersion("1.1", approvedAt.AddMinutes(-1), approver, DocumentVersionChange.EditedInWord, null,
                    null, texts, metadata, WordVersion: 2));
            }
        }

        var pageConfidences = PageConfidences(index, entry.Pages, entry.Confidence!.Value, entry.EmptyPage);
        var pages = pageConfidences
            .Select((confidence, i) => new PageQuality(i + 1, confidence, confidence is not null, i + 1 == entry.WarningPage ? 1 : 0))
            .ToList();
        var quality = QualityCheck.Run(pages, entry.Confidence, metadata, QualitySettings.Default, checkedAt);

        return new DocumentRecord(HistoryText.Id(index), IsSample: true, HistoryText.FileName(index, entry.Department, entry.Kind),
            entry.Pages, size, uploaded, operatorName, entry.Confidence, entry.Status, metadata, texts, versions, BatchId: batch,
            Approval: approval, PageConfidences: pageConfidences, Quality: quality);
    }

    /// <summary>
    /// The history documents matching <paramref name="query"/>, in <paramref name="order"/>, leaving out
    /// <paramref name="excluded"/> (documents stored with changes, which are listed from the store instead).
    /// </summary>
    internal IEnumerable<int> Match(DocumentQuery query, DocumentOrder order, DateOnly today, IReadOnlySet<int> excluded)
    {
        if (!Enabled)
            yield break;

        var built = Simulation;
        var search = HistorySearch.For(query.Text, query.Field);
        if (search is { CanMatch: false })
            yield break;

        var firstDay = FirstDay(today);
        int? approvedFrom = query.ApprovedFrom is { } approvedStart ? (approvedStart.DayNumber - firstDay.DayNumber) * HistoryProfile.SecondsPerDay : null;
        int? approvedTo = query.ApprovedTo is { } approvedEnd ? ((approvedEnd.DayNumber - firstDay.DayNumber) + 1) * HistoryProfile.SecondsPerDay : null;

        bool Matches(int index)
        {
            ref readonly var entry = ref built.Entries[index];
            if (query.Status is { } status && entry.Status != status)
                return false;
            if (query.Department is { } department && entry.Department != department)
                return false;
            if (query.Kind is { } kind && entry.Kind != kind)
                return false;
            if (query.Confidentiality is { } confidentiality && entry.Confidentiality != confidentiality)
                return false;
            if (approvedFrom is not null || approvedTo is not null)
            {
                if (entry.Approved < 0 || entry.Approved < approvedFrom || entry.Approved >= approvedTo)
                    return false;
            }

            if (query.From is not null || query.To is not null)
            {
                var date = HistoryText.DocumentDate(index);
                if (date < query.From || date > query.To)
                    return false;
            }

            return (search is null || search.Matches(index, entry)) && !excluded.Contains(index);
        }

        switch (order)
        {
            case DocumentOrder.NewestApproval:
                foreach (var index in built.ByApproval)
                {
                    if (Matches(index))
                        yield return index;
                }

                break;
            case DocumentOrder.OldestUpload when query.Status == DocumentStatus.NeedsReview:
                foreach (var index in built.ReviewQueue)
                {
                    if (Matches(index))
                        yield return index;
                }

                break;
            case DocumentOrder.OldestUpload:
                for (var index = 0; index < Count; index++)
                {
                    if (Matches(index))
                        yield return index;
                }

                break;
            default:
                for (var index = Count - 1; index >= 0; index--)
                {
                    if (Matches(index))
                        yield return index;
                }

                break;
        }
    }

    /// <summary>The key a list is ordered by, in seconds from the first history day (bigger first for the newest orders).</summary>
    internal long SortKey(int index, DocumentOrder order)
    {
        var entry = Entry(index);
        return order == DocumentOrder.NewestApproval ? entry.Approved : entry.Uploaded;
    }

    /// <summary>The same key for a stored document, so it can be merged into the history at the right place.</summary>
    internal long SortKey(DocumentRecord document, DocumentOrder order, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        var utc = order == DocumentOrder.NewestApproval ? document.Approval?.AtUtc : document.UploadedAtUtc;
        // Without a time a document goes last; half the range keeps the sign flip of the newest orders from overflowing.
        if (utc is not { } value)
            return order == DocumentOrder.OldestUpload ? long.MaxValue / 2 : long.MinValue / 2;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), _time.LocalTimeZone);
        return (long)Math.Floor((local - FirstDay(today).ToDateTime(TimeOnly.MinValue)).TotalSeconds);
    }

    /// <summary>
    /// Page confidences around the document's: one-page documents keep it, longer ones spread a little around it
    /// (their average stays the document's). The page without text has none.
    /// </summary>
    internal static double?[] PageConfidences(int index, int pages, double confidence, int emptyPage)
    {
        var values = new double?[pages];
        var textPages = pages - (emptyPage >= 1 && emptyPage <= pages ? 1 : 0);
        var amplitude = Math.Min(5, (99.9 - confidence) / 2);
        double sum = 0;
        for (var page = 1; page <= pages; page++)
        {
            if (page == emptyPage)
                continue;
            var offset = ((HistoryRandom.Unit(index, 200 + page) * 2) - 1) * amplitude;
            values[page - 1] = offset;
            sum += offset;
        }

        var mean = textPages == 0 ? 0 : sum / textPages;
        for (var page = 0; page < pages; page++)
        {
            if (values[page] is { } offset)
                values[page] = Math.Round(confidence + offset - mean, 1);
        }

        return values;
    }

    private DateTime ToUtc(DateOnly firstDay, int seconds)
    {
        var local = firstDay.ToDateTime(TimeOnly.MinValue).AddSeconds(seconds);
        var zone = _time.LocalTimeZone;
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static Built Build(int count)
    {
        var entries = new HistoryEntry[count];
        var flagged = new List<int>(count / 20);
        var perDay = Allocate(count, DayWeights());
        var index = 0;
        for (var day = 0; day < HistoryProfile.Days; day++)
        {
            var perHour = Allocate(perDay[day], HistoryProfile.HourWeights.Select(w => (double)w).ToArray());
            var morning = Shift(day, evening: false);
            var evening = Shift(day, evening: true);
            for (var hour = 0; hour < perHour.Length; hour++)
            {
                var clock = HistoryProfile.FirstHour + hour;
                var shift = clock < HistoryProfile.EveningShiftHour ? morning : evening;
                var documents = perHour[hour];
                for (var i = 0; i < documents; i++)
                {
                    // Spread over the hour in order, so a later index is always a later upload.
                    var second = (int)(SecondsPerHour * (i + HistoryRandom.Unit(index, 1)) / documents);
                    var uploaded = (day * HistoryProfile.SecondsPerDay) + (clock * SecondsPerHour) + second;
                    entries[index] = Scan(index, uploaded, shift[i % shift.Length]);
                    if (entries[index].Status == DocumentStatus.NeedsReview)
                        flagged.Add(index);
                    index++;
                }
            }
        }

        Review(entries, flagged);
        return new Built(entries);
    }

    /// <summary>One document as scanned, recognised and checked: archived by the check, flagged for review, or failed.</summary>
    private static HistoryEntry Scan(int index, int uploaded, byte uploader)
    {
        var profile = PickDepartment(HistoryRandom.Unit(index, 2));
        var kind = HistoryRandom.Unit(index, 3) < profile.MainKindShare ? profile.Kinds[0] : profile.Kinds[1];
        var share = HistoryRandom.Unit(index, 4);
        var confidentiality = share < profile.Internal ? DocumentConfidentiality.Internal
            : share < profile.Internal + profile.Confidential ? DocumentConfidentiality.Confidential
            : DocumentConfidentiality.Secret;
        var size = HistoryRandom.Unit(index, 5);
        var pages = (byte)(size < 0.93 ? 1 : size < 0.975 ? 2 : size < 0.99 ? 3 : 4 + HistoryRandom.Below(index, 6, 3));
        var checkedAt = uploaded + 60 + HistoryRandom.Below(index, 7, 181);

        if (HistoryRandom.Unit(index, 8) < HistoryProfile.FailedShare)
        {
            return new HistoryEntry(uploaded, checkedAt, -1, -1, profile.Department, kind, confidentiality, pages, DocumentStatus.Failed,
                uploader, HistoryEntry.NoPerson, null, 0, HistoryEntry.NoPage, HistoryEntry.NoPage, edited: false);
        }

        // Most documents read well; a few read badly (a steep curve, as in real OCR output).
        var spread = HistoryRandom.Unit(index, 9);
        var confidence = Math.Round(Math.Clamp(profile.Confidence + 2.4 - (16 * Math.Pow(spread, 9)), 40, 99.6), 1);
        var emptyPage = pages > 1 && HistoryRandom.Unit(index, 10) < HistoryProfile.EmptyPageShare
            ? (byte)(1 + HistoryRandom.Below(index, 11, pages))
            : HistoryEntry.NoPage;
        var warningPage = HistoryRandom.Unit(index, 12) < HistoryProfile.WarningShare
            ? (byte)(1 + HistoryRandom.Below(index, 13, pages))
            : HistoryEntry.NoPage;
        if (warningPage == emptyPage)
            warningPage = HistoryEntry.NoPage;

        var pageConfidences = PageConfidences(index, pages, confidence, emptyPage);
        var settings = QualitySettings.Default;
        var lowPages = (byte)pageConfidences.Count(c => c < settings.MinPageConfidence);
        var reason = FirstIssue(confidence, pageConfidences, warningPage, settings);
        return reason is null
            ? new HistoryEntry(uploaded, checkedAt, checkedAt, (short)Math.Round(confidence * 10), profile.Department, kind, confidentiality,
                pages, DocumentStatus.Archived, uploader, HistoryEntry.QualityCheck, null, lowPages, emptyPage, warningPage, edited: false)
            : new HistoryEntry(uploaded, checkedAt, -1, (short)Math.Round(confidence * 10), profile.Department, kind, confidentiality,
                pages, DocumentStatus.NeedsReview, uploader, HistoryEntry.NoPerson, reason, lowPages, emptyPage, warningPage, edited: false);
    }

    /// <summary>The first issue <see cref="QualityCheck.Run"/> reports for these pages, in the same order.</summary>
    private static QualityIssueKind? FirstIssue(double confidence, double?[] pages, byte warningPage, QualitySettings settings)
    {
        if (confidence < settings.MinDocumentConfidence)
            return QualityIssueKind.LowDocumentConfidence;
        for (var page = 1; page <= pages.Length; page++)
        {
            if (pages[page - 1] is not { } value)
            {
                if (settings.FlagEmptyPages)
                    return QualityIssueKind.EmptyPage;
                continue;
            }

            if (value < settings.MinPageConfidence)
                return QualityIssueKind.LowPageConfidence;
            if (settings.FlagWarnings && page == warningPage)
                return QualityIssueKind.PageWarnings;
        }

        return null;
    }

    /// <summary>
    /// The review queue: reviewers work their day shift and take, each time they are free, the oldest flagged
    /// document they may open. What they have not reached by the end of the last day is still waiting.
    /// </summary>
    private static void Review(HistoryEntry[] entries, List<int> flagged)
    {
        var queue = flagged.OrderBy(i => entries[i].Checked).ThenBy(i => i).ToArray();
        var taken = new bool[queue.Length];
        var reviewers = HistoryProfile.Reviewers;
        var free = new int[reviewers.Length];
        var head = 0;
        for (var day = 0; day < HistoryProfile.Days; day++)
        {
            var end = (day * HistoryProfile.SecondsPerDay) + HistoryProfile.ReviewEndSecond;
            Array.Fill(free, (day * HistoryProfile.SecondsPerDay) + HistoryProfile.ReviewStartSecond);
            while (true)
            {
                var reviewer = Array.IndexOf(free, free.Min());
                var now = free[reviewer];
                while (head < queue.Length && taken[head])
                    head++;
                if (now >= end || head >= queue.Length)
                    break;

                var pick = -1;
                var next = int.MaxValue;
                for (var k = head; k < queue.Length; k++)
                {
                    if (taken[k])
                        continue;
                    ref readonly var entry = ref entries[queue[k]];
                    if (!reviewers[reviewer].CanSee(entry.Department, entry.Confidentiality))
                        continue;
                    if (entry.Checked <= now)
                        pick = k;
                    else
                        next = entry.Checked;
                    break;
                }

                if (pick < 0)
                {
                    free[reviewer] = Math.Min(next, end);
                    continue;
                }

                var index = queue[pick];
                taken[pick] = true;
                var approved = now + ReviewSeconds(index, reviewer);
                entries[index] = entries[index].Reviewed(approved, (byte)reviewer, HistoryRandom.Unit(index, 22) < HistoryProfile.EditedShare);
                free[reviewer] = approved;
            }
        }
    }

    /// <summary>The operators on a shift that day (an account disabled since then no longer scans).</summary>
    private static byte[] Shift(int day, bool evening) =>
        HistoryProfile.Operators
            .Select((op, i) => (Operator: op, Index: (byte)i))
            .Where(o => o.Operator.EveningShift == evening && (o.Operator.LastDay is null || day <= o.Operator.LastDay))
            .Select(o => o.Index)
            .ToArray();

    private static HistoryDepartment PickDepartment(double share)
    {
        foreach (var department in HistoryProfile.Departments)
        {
            if (share < department.Share)
                return department;
            share -= department.Share;
        }

        return HistoryProfile.Departments[^1];
    }

    /// <summary>A slow start, then a steady line with small day-to-day changes.</summary>
    private static double[] DayWeights() =>
        Enumerable.Range(0, HistoryProfile.Days)
            .Select(day => (day < HistoryProfile.RampUp.Length ? HistoryProfile.RampUp[day] : 1.0) * (1 + (0.06 * ((((day * 37) + 11) % 21) - 10) / 10)))
            .ToArray();

    /// <summary>Splits <paramref name="total"/> in proportion to the weights, giving the rounding leftovers to the largest remainders.</summary>
    private static int[] Allocate(int total, double[] weights)
    {
        var sum = weights.Sum();
        var exact = weights.Select(w => total * w / sum).ToArray();
        var counts = exact.Select(e => (int)Math.Floor(e)).ToArray();
        var left = total - counts.Sum();
        foreach (var i in Enumerable.Range(0, weights.Length).OrderByDescending(i => exact[i] - counts[i]).ThenBy(i => i).Take(left))
            counts[i]++;
        return counts;
    }

    private sealed class Built
    {
        public Built(HistoryEntry[] entries)
        {
            Entries = entries;
            Facts = new FactsBuilder();
            var queue = new List<int>();
            var archived = new List<int>(entries.Length);
            for (var index = 0; index < entries.Length; index++)
            {
                ref readonly var entry = ref entries[index];
                Facts.Add(entry, index, firstDay: 0, sign: 1);
                if (entry.Status == DocumentStatus.NeedsReview)
                    queue.Add(index);
                else if (entry.Status == DocumentStatus.Archived)
                    archived.Add(index);
            }

            ReviewQueue = [.. queue];
            var byApproval = archived.ToArray();
            var keys = byApproval.Select(i => -(((long)entries[i].Approved << 32) | (uint)i)).ToArray();
            Array.Sort(keys, byApproval);
            ByApproval = byApproval;
        }

        public HistoryEntry[] Entries { get; }

        /// <summary>The figures of every document, by history day index.</summary>
        public FactsBuilder Facts { get; }

        /// <summary>Documents still waiting for review, oldest first.</summary>
        public int[] ReviewQueue { get; }

        /// <summary>Archived documents, newest approval first.</summary>
        public int[] ByApproval { get; }
    }
}
