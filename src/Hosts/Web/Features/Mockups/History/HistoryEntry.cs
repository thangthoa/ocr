using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.History;

/// <summary>
/// One simulated document, kept small (a million of them stay in memory): times are seconds from midnight of the
/// first history day, local time, so the history follows the current date without being rebuilt. Everything else
/// about the document (names, metadata, text) is derived from its index when it is shown.
/// </summary>
internal readonly struct HistoryEntry
{
    public const byte NoPerson = byte.MaxValue;

    /// <summary>The automatic check approved the document.</summary>
    public const byte QualityCheck = byte.MaxValue - 1;

    public const byte NoReason = byte.MaxValue;

    public const byte NoPage = byte.MaxValue;

    private readonly byte _department;
    private readonly byte _kind;
    private readonly byte _confidentiality;
    private readonly byte _status;
    private readonly byte _reason;

    public HistoryEntry(int uploaded, int checkedAt, int approved, short confidence, DocumentDepartment department, DocumentKind kind,
        DocumentConfidentiality confidentiality, byte pages, DocumentStatus status, byte uploader, byte approver,
        QualityIssueKind? reason, byte lowPages, byte emptyPage, byte warningPage, bool edited)
    {
        Uploaded = uploaded;
        Checked = checkedAt;
        Approved = approved;
        ConfidenceTenths = confidence;
        _department = (byte)department;
        _kind = (byte)kind;
        _confidentiality = (byte)confidentiality;
        Pages = pages;
        _status = (byte)status;
        Uploader = uploader;
        Approver = approver;
        _reason = reason is { } value ? (byte)value : NoReason;
        LowPages = lowPages;
        EmptyPage = emptyPage;
        WarningPage = warningPage;
        Edited = edited;
    }

    public int Uploaded { get; }

    /// <summary>When OCR finished and the automatic check ran.</summary>
    public int Checked { get; }

    /// <summary>When the document was approved (and so archived); -1 when it was not.</summary>
    public int Approved { get; }

    /// <summary>OCR confidence of the document in tenths of a percent; -1 when OCR failed.</summary>
    public short ConfidenceTenths { get; }

    public byte Pages { get; }

    /// <summary>Index in <see cref="HistoryProfile.Operators"/>.</summary>
    public byte Uploader { get; }

    /// <summary>Index in <see cref="HistoryProfile.Reviewers"/>, <see cref="QualityCheck"/> or <see cref="NoPerson"/>.</summary>
    public byte Approver { get; }

    /// <summary>Pages under the page confidence threshold of the check.</summary>
    public byte LowPages { get; }

    /// <summary>Page number (from 1) without text, or <see cref="NoPage"/>.</summary>
    public byte EmptyPage { get; }

    /// <summary>Page number (from 1) the OCR service warned about, or <see cref="NoPage"/>.</summary>
    public byte WarningPage { get; }

    /// <summary>The reviewer corrected the text before approving.</summary>
    public bool Edited { get; }

    public DocumentDepartment Department => (DocumentDepartment)_department;

    public DocumentKind Kind => (DocumentKind)_kind;

    public DocumentConfidentiality Confidentiality => (DocumentConfidentiality)_confidentiality;

    public DocumentStatus Status => (DocumentStatus)_status;

    /// <summary>The first issue the automatic check found; null when it passed or OCR failed.</summary>
    public QualityIssueKind? Reason => _reason == NoReason ? null : (QualityIssueKind)_reason;

    public double? Confidence => ConfidenceTenths < 0 ? null : ConfidenceTenths / 10.0;

    public int TextPages => ConfidenceTenths < 0 ? 0 : Pages - (EmptyPage == NoPage ? 0 : 1);

    public bool ApprovedByCheck => Approver == QualityCheck;

    public bool ApprovedByReviewer => Approver < HistoryProfile.Reviewers.Length;

    public HistoryEntry Reviewed(int approved, byte reviewer, bool edited) =>
        new(Uploaded, Checked, approved, ConfidenceTenths, Department, Kind, Confidentiality, Pages, DocumentStatus.Archived, Uploader,
            reviewer, Reason, LowPages, EmptyPage, WarningPage, edited);
}

/// <summary>Stable pseudo-random numbers from an index and a salt (SplitMix64), the same on every run.</summary>
internal static class HistoryRandom
{
    public static ulong Mix(ulong value)
    {
        var z = value + 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>A number in [0, 1).</summary>
    public static double Unit(int index, int salt) =>
        (Mix(((ulong)(uint)index * 0x100000001B3UL) ^ ((ulong)(uint)salt * 0x9E3779B1UL)) >> 11) * (1.0 / (1UL << 53));

    /// <summary>A whole number in [0, <paramref name="max"/>).</summary>
    public static int Below(int index, int salt, int max) => (int)(Unit(index, salt) * max);
}
