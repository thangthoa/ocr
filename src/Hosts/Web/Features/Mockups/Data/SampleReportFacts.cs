namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>A department's sample figures for one day; the reports add them up over a period.</summary>
/// <param name="LowPagesPercent">Pages under the page confidence threshold of the automatic check.</param>
public sealed record DepartmentDay(
    DateOnly Day,
    DocumentDepartment Department,
    int Pages,
    int Documents,
    double Confidence,
    double AutoArchivePercent,
    double FailedPercent,
    double LowPagesPercent);

/// <summary>
/// Daily sample figures behind the Reports page. A day's pages are those of the dashboard
/// (<see cref="SampleReports.PagesOn"/>), split between departments in the shares of the dashboard's department
/// table; the rates move a little from day to day, the same way on every run.
/// </summary>
public static class SampleReportFacts
{
    private static readonly DepartmentProfile[] Profiles =
    [
        new(DocumentDepartment.Finance, 0.386, 12.0, 95.1, 84, 4.1),
        new(DocumentDepartment.HumanResources, 0.193, 12.0, 93.8, 80, 6.0),
        new(DocumentDepartment.Legal, 0.164, 18.2, 92.7, 71, 8.3),
        new(DocumentDepartment.Procurement, 0.173, 9.8, 94.9, 83, 4.6),
        new(DocumentDepartment.Administration, 0.084, 9.4, 96.2, 87, 2.9),
    ];

    public static IReadOnlyList<DepartmentDay> For(DateOnly day, DateOnly today)
    {
        var pages = SampleReports.PagesOn(day, today);
        var left = pages;
        var rows = new List<DepartmentDay>(Profiles.Length);
        for (var i = 0; i < Profiles.Length; i++)
        {
            var profile = Profiles[i];
            // The last department takes what rounding left over, so departments add up to the day.
            var share = i == Profiles.Length - 1 ? left : (int)Math.Round(pages * profile.Share);
            left -= share;
            var wobble = Wobble(day, i);
            rows.Add(new DepartmentDay(
                day,
                profile.Department,
                share,
                (int)Math.Round(share / profile.PagesPerDocument),
                profile.Confidence + wobble * 0.6,
                profile.AutoArchivePercent + wobble * 3,
                Math.Max(0, 0.2 + wobble * 0.1),
                Math.Max(0, profile.LowPagesPercent + wobble * 0.8)));
        }

        return rows;
    }

    public static IReadOnlyList<DepartmentDay> For(DateOnly from, DateOnly to, DateOnly today)
    {
        var rows = new List<DepartmentDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
            rows.AddRange(For(day, today));
        return rows;
    }

    /// <summary>A stable number in [-1, 1] for a day and a salt.</summary>
    public static double Wobble(DateOnly day, int salt) => ((day.DayNumber * 37 + salt * 101) % 21 - 10) / 10.0;

    private sealed record DepartmentProfile(
        DocumentDepartment Department,
        double Share,
        double PagesPerDocument,
        double Confidence,
        double AutoArchivePercent,
        double LowPagesPercent);
}
