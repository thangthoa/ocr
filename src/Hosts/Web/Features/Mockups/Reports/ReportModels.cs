using System.Globalization;

namespace OppOcr.Web.Features.Mockups.Reports;

/// <summary>The reports people can run from the Reports page, in the order the page lists them.</summary>
public enum ReportKind
{
    Progress,
    Quality,
    Productivity,
    Exceptions,
    Archive,
    Activity,
}

public enum ReportCellKind
{
    Empty,
    Text,
    Count,
    Percent,
    Amount,
    Date,
}

/// <summary>One value of a report; the page and the CSV file format it each their own way.</summary>
public sealed record ReportCell(ReportCellKind Kind, string? Text = null, double Value = 0, DateOnly? Date = null)
{
    public static readonly ReportCell Empty = new(ReportCellKind.Empty);

    public static ReportCell Of(string text) => new(ReportCellKind.Text, Text: text);

    public static ReportCell Count(double value) => new(ReportCellKind.Count, Value: value);

    /// <param name="value">A percentage between 0 and 100.</param>
    public static ReportCell Percent(double value) => new(ReportCellKind.Percent, Value: value);

    /// <summary>A measure shown with one decimal (hours, gigabytes, pages per hour).</summary>
    public static ReportCell Amount(double value) => new(ReportCellKind.Amount, Value: value);

    public static ReportCell Day(DateOnly date) => new(ReportCellKind.Date, Date: date);

    public bool IsNumeric => Kind is ReportCellKind.Count or ReportCellKind.Percent or ReportCellKind.Amount;

    /// <summary>For people, in the UI language.</summary>
    public string Display() => Kind switch
    {
        ReportCellKind.Text => Text ?? string.Empty,
        ReportCellKind.Count => MockupTextExtensions.FormatNumber((int)Math.Round(Value)),
        ReportCellKind.Percent => MockupTextExtensions.FormatPercent(Value),
        ReportCellKind.Amount => Value.ToString("N1", CultureInfo.CurrentCulture),
        ReportCellKind.Date => MockupTextExtensions.FormatDate(Date),
        _ => MockupTextExtensions.Missing,
    };

    /// <summary>For a spreadsheet: invariant numbers and ISO dates, so Excel reads them as values.</summary>
    public string Csv() => Kind switch
    {
        ReportCellKind.Text => Text ?? string.Empty,
        ReportCellKind.Count => Math.Round(Value).ToString("0", CultureInfo.InvariantCulture),
        ReportCellKind.Percent or ReportCellKind.Amount => Value.ToString("0.0", CultureInfo.InvariantCulture),
        ReportCellKind.Date => Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        _ => string.Empty,
    };
}

public sealed record ReportColumn(string Header, bool Numeric = true);

/// <param name="IsSample">False when the report reads what people did in this app (the activity report).</param>
/// <param name="Total">The totals row; its first cell is the label.</param>
public sealed record ReportTable(
    ReportKind Kind,
    DateOnly From,
    DateOnly To,
    bool IsSample,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<ReportCell>> Rows,
    IReadOnlyList<ReportCell>? Total);

public enum ReportRangeError
{
    Missing,
    FromAfterTo,
    InFuture,
    TooLong,
}

public static class ReportRange
{
    /// <summary>A year a time keeps the daily reports a readable length.</summary>
    public const int MaxDays = 366;

    public static ReportRangeError? Check(DateOnly? from, DateOnly? to, DateOnly today) =>
        from is null || to is null ? ReportRangeError.Missing
        : from > to ? ReportRangeError.FromAfterTo
        : to > today ? ReportRangeError.InFuture
        : to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays ? ReportRangeError.TooLong
        : null;
}
