using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.History;

/// <summary>A capture operator of the simulated line: the shift they scan in, and the last history day they worked.</summary>
/// <param name="LastDay">Index of the last history day they worked (an account disabled since then); null while active.</param>
internal sealed record HistoryOperator(string UserName, bool EveningShift, int? LastDay = null);

/// <summary>A reviewer of the simulated line and what they may open (as in <see cref="Access.UserDirectory"/>).</summary>
/// <param name="Departments">Empty for every department.</param>
/// <param name="Pace">How long they take over a document, against the average reviewer (1.2 is 20 % slower).</param>
internal sealed record HistoryReviewer(string UserName, IReadOnlyList<DocumentDepartment> Departments, DocumentConfidentiality Clearance, double Pace)
{
    public bool CanSee(DocumentDepartment department, DocumentConfidentiality confidentiality) =>
        (Departments.Count == 0 || Departments.Contains(department)) && confidentiality <= Clearance;
}

/// <param name="Share">Share of the documents.</param>
/// <param name="Confidence">Typical OCR confidence of the department's documents, in percent.</param>
/// <param name="Kinds">The kinds of document it sends, the first one with <paramref name="MainKindShare"/>.</param>
/// <param name="Internal">Share of internal documents; <paramref name="Confidential"/> next, the rest is secret.</param>
internal sealed record HistoryDepartment(
    DocumentDepartment Department,
    double Share,
    double Confidence,
    DocumentKind[] Kinds,
    double MainKindShare,
    double Internal,
    double Confidential);

/// <summary>
/// The shape of the simulated project: how many documents, over how many days, who scanned and reviewed them, and the
/// rates of the automatic check. The figures are chosen to hold together: about 25,000 one-page documents a day once
/// the line is up to speed (the tender's 20,000 pages a day target, met by 11 or 12 operators), 3–4 % of them flagged
/// by the automatic check, and six reviewers who clear the queue in their day shift, so a few hundred documents scanned
/// in the evening are still waiting at the end of the day.
/// </summary>
internal static class HistoryProfile
{
    /// <summary>Documents the project has processed before today; the files uploaded in this app come on top.</summary>
    public const int Documents = 1_093_911;

    /// <summary>The history covers the days before today, from the project start (<see cref="Kpi.KpiSettings.Default"/>).</summary>
    public const int Days = 45;

    public const int FirstHour = 7;

    /// <summary>The morning shift scans until this hour, the evening shift from it.</summary>
    public const int EveningShiftHour = 15;

    public const int SecondsPerDay = 86_400;

    /// <summary>Reviewers work 07:30–16:30.</summary>
    public const int ReviewStartSecond = (7 * 3600) + 1800;

    public const int ReviewEndSecond = (16 * 3600) + 1800;

    public const double FailedShare = 0.002;

    public const double WarningShare = 0.004;

    /// <summary>A page with no text, among documents of two pages or more.</summary>
    public const double EmptyPageShare = 0.01;

    /// <summary>Reviewed documents whose text the reviewer corrected (a version 1.1) before approving.</summary>
    public const double EditedShare = 0.6;

    /// <summary>Pages scanned per hour of the line: busy mid-morning, a dip at the change of shift.</summary>
    public static readonly int[] HourWeights =
        [1280, 1610, 1720, 1690, 1540, 1650, 1700, 1580, 1490, 1620, 1680, 1710, 1550, 1600, 1660, 1430];

    /// <summary>The first days, while the line gets up to speed.</summary>
    public static readonly double[] RampUp = [0.55, 0.65, 0.75, 0.85, 0.92, 0.97];

    public static readonly HistoryDepartment[] Departments =
    [
        new(DocumentDepartment.Finance, 0.386, 95.1, [DocumentKind.PaymentVoucher, DocumentKind.PurchaseOrder], 0.75, 0.7, 0.25),
        new(DocumentDepartment.HumanResources, 0.193, 93.8, [DocumentKind.Decision, DocumentKind.Correspondence], 0.65, 0.4, 0.5),
        new(DocumentDepartment.Legal, 0.164, 92.7, [DocumentKind.Contract, DocumentKind.Correspondence], 0.7, 0.3, 0.5),
        new(DocumentDepartment.Procurement, 0.173, 94.9, [DocumentKind.PurchaseOrder, DocumentKind.Contract], 0.7, 0.75, 0.22),
        new(DocumentDepartment.Administration, 0.084, 96.2, [DocumentKind.Correspondence, DocumentKind.Decision], 0.7, 0.85, 0.13),
    ];

    /// <summary>
    /// Twelve operators on two shifts. capture03 was disabled 40 days ago (<see cref="Access.UserDirectory"/>), so the
    /// morning shift has one operator fewer after the fifth day.
    /// </summary>
    public static readonly HistoryOperator[] Operators =
    [
        new("capture01", EveningShift: false),
        new("capture02", EveningShift: false),
        new("capture03", EveningShift: false, LastDay: 4),
        new("capture04", EveningShift: false),
        new("capture05", EveningShift: false),
        new("capture06", EveningShift: false),
        new("capture07", EveningShift: true),
        new("capture08", EveningShift: true),
        new("capture09", EveningShift: true),
        new("capture10", EveningShift: true),
        new("capture11", EveningShift: true),
        new("capture12", EveningShift: true),
    ];

    /// <summary>The reviewers, with the departments and clearance of their accounts; review03 handles secret documents.</summary>
    public static readonly HistoryReviewer[] Reviewers =
    [
        new("review01", [], DocumentConfidentiality.Confidential, 0.95),
        new("review02", [DocumentDepartment.Finance, DocumentDepartment.Procurement], DocumentConfidentiality.Confidential, 1.1),
        new("review03", [], DocumentConfidentiality.Secret, 1.2),
        new("review04", [], DocumentConfidentiality.Confidential, 0.85),
        new("review05", [DocumentDepartment.HumanResources, DocumentDepartment.Legal, DocumentDepartment.Administration],
            DocumentConfidentiality.Confidential, 1.05),
        new("review06", [], DocumentConfidentiality.Confidential, 0.9),
    ];

    public static HistoryDepartment Profile(DocumentDepartment department) => Departments[(int)department];
}
