using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Activity;

/// <summary>
/// The activity log as the Activity logs page shows it: the sample month (<see cref="SampleActivity"/>) followed by
/// what was done in this app since it started (<see cref="AuditLog"/>), chained with SHA-256 the way an append-only
/// log is protected against tampering. The real log (append-only table, forwarded to the SIEM) needs persistence.
/// </summary>
public sealed class ActivityTrail(SampleActivity samples, AuditLog audit, IStringLocalizer<MockupText> text, TimeProvider time)
{
    /// <summary>Every entry, newest first, each with the hash of the one before it.</summary>
    public IReadOnlyList<ActivityRow> Load()
    {
        var live = audit.Latest(AuditLog.Capacity);
        var ordered = samples.Entries.Select(e => (Entry: e, IsSample: true))
            .Concat(live.Reverse().Select(e => (Entry: e, IsSample: false)))
            .OrderBy(e => e.Entry.AtUtc)
            .ToList();

        var rows = new List<ActivityRow>(ordered.Count);
        var previous = GenesisHash;
        for (var i = 0; i < ordered.Count; i++)
        {
            var hash = Chain(previous, ordered[i].Entry);
            rows.Add(new ActivityRow(i + 1, ordered[i].Entry, previous, hash, ordered[i].IsSample));
            previous = hash;
        }

        rows.Reverse();
        return rows;
    }

    /// <summary>Hashes every entry again from the oldest; the first row whose hash differs is where the log was changed.</summary>
    public static long? FirstBrokenLink(IReadOnlyList<ActivityRow> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        var previous = GenesisHash;
        for (var i = newestFirst.Count - 1; i >= 0; i--)
        {
            var row = newestFirst[i];
            if (row.PreviousHash != previous || Chain(previous, row.Entry) != row.Hash)
                return row.Sequence;
            previous = row.Hash;
        }

        return null;
    }

    public IReadOnlyList<ActivityRow> Filter(IEnumerable<ActivityRow> rows, ActivityQuery query)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(query);
        var term = query.Text?.Trim();
        return rows
            .Where(r => query.User is null || string.Equals(r.Entry.User, query.User, StringComparison.OrdinalIgnoreCase))
            .Where(r => query.Category is null || r.Category == query.Category)
            .Where(r => query.Result is null || r.Result == query.Result)
            .Where(r => query.From is null || LocalDay(r.Entry.AtUtc) >= query.From)
            .Where(r => query.To is null || LocalDay(r.Entry.AtUtc) <= query.To)
            .Where(r => string.IsNullOrEmpty(term) || Matches(r, term))
            .ToList();
    }

    /// <summary>The filtered log as CSV (UTF-8 with a BOM, times in UTC); the export itself is logged.</summary>
    internal DownloadFile Export(ActivityQuery query, string user)
    {
        var rows = Filter(Load(), query);
        var csv = new StringBuilder();
        AppendRow(csv,
        [
            "#", text["Audit.Column.Time"], text["Audit.Column.User"], text["Activity.Column.Category"], text["Audit.Column.Action"],
            text["Audit.Column.Document"], text["Activity.Column.Ip"], text["Activity.Column.Client"], text["Activity.Column.Result"],
            text["Activity.Siem"], text["Activity.Hash"],
        ]);
        foreach (var row in rows)
        {
            var entry = row.Entry;
            AppendRow(csv,
            [
                row.Sequence.ToString(CultureInfo.InvariantCulture),
                entry.AtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                entry.User,
                text[$"ActivityCategory.{row.Category}"],
                text.DescribeActivity(entry),
                entry.DocumentName is null ? null : $"{entry.DocumentName} ({entry.DocumentId})",
                entry.IpAddress,
                entry.Client,
                text[$"ActivityResult.{row.Result}"],
                row.SiemEventId,
                row.Hash,
            ]);
        }

        audit.Record(user, AuditAction.ActivityExported, detail: rows.Count.ToString(CultureInfo.InvariantCulture));
        var fileName = "activity-log-" + time.GetLocalNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".csv";
        return new DownloadFile([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())], "text/csv; charset=utf-8", fileName);
    }

    public DateOnly LocalDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), time.LocalTimeZone));

    private bool Matches(ActivityRow row, string term)
    {
        var entry = row.Entry;
        bool Has(string? value) => value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;
        return Has(entry.User) || Has(entry.DocumentName) || Has(entry.DocumentId) || Has(entry.Detail) || Has(entry.IpAddress)
            || Has(text.DescribeActivity(entry)) || Has(row.SiemEventId);
    }

    private static readonly string GenesisHash = new('0', 64);

    private static string Chain(string previous, AuditEntry entry)
    {
        var line = string.Join('|', previous, entry.AtUtc.ToString("O", CultureInfo.InvariantCulture), entry.User, entry.Action,
            entry.DocumentId, entry.Version, entry.Status, entry.Detail, entry.IpAddress);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)));
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> fields) =>
        csv.Append(string.Join(',', fields.Select(ApprovedDocumentFiles.CsvField))).Append("\r\n");
}
