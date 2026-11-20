namespace OppOcr.Web.Features.Mockups.Kpi;

// KPI targets of the digitization project. Like the rest of the mockup these rules live in the Web host;
// they move to the Domain/Application of a reporting module once the figures come from real data.

/// <summary>The KPIs the dashboard can track; the list is fixed, only the targets are set by people.</summary>
public enum KpiKind
{
    PagesPerDay,
    OcrAccuracy,
    AutoArchiveRate,
    ReviewBacklog,
    ReviewTime,
    FailureRate,
    PagesPerOperator,
}

/// <summary>Whether a KPI is met by reaching its target or by staying under it.</summary>
public enum KpiDirection
{
    AtLeast,
    AtMost,
}

public enum KpiUnit
{
    Pages,
    Documents,
    Percent,
    Hours,
}

public enum KpiStatus
{
    OnTrack,
    AtRisk,
    OffTrack,
}

/// <summary>Why a target or alert level was refused.</summary>
public enum KpiTargetError
{
    Missing,
    OutOfRange,
    AlertAboveTarget,
    AlertBelowTarget,
}

public enum ProjectPlanError
{
    PagesOutOfRange,
    DatesMissing,
    EndBeforeStart,
}

/// <param name="Alert">
/// The level at which the KPI turns from "at risk" to "off track": below the target for an
/// <see cref="KpiDirection.AtLeast"/> KPI, above it for an <see cref="KpiDirection.AtMost"/> one.
/// </param>
public sealed record KpiTarget(KpiKind Kind, double Target, double Alert, bool ShowOnDashboard = true);

/// <summary>The whole digitization project: how many pages, from when to when.</summary>
public sealed record ProjectPlan(int TotalPages, DateOnly Start, DateOnly End)
{
    public int TotalDays => End.DayNumber - Start.DayNumber + 1;

    /// <summary>Pages the plan expects by the end of <paramref name="day"/>, at an even pace.</summary>
    public int PlannedBy(DateOnly day)
    {
        var elapsed = Math.Clamp(day.DayNumber - Start.DayNumber + 1, 0, TotalDays);
        return (int)Math.Round((double)TotalPages * elapsed / TotalDays);
    }
}

public sealed record KpiSettings(IReadOnlyList<KpiTarget> Targets, ProjectPlan Project)
{
    public KpiTarget For(KpiKind kind) => Targets.First(t => t.Kind == kind);

    /// <summary>
    /// The targets of the tender: 2,000,000 pages at the 20,000 pages a day operational target (100 days),
    /// and at least 90 % OCR accuracy. The project starts 45 days before <paramref name="today"/> so the
    /// sample figures show a project under way whatever the date of the demo.
    /// </summary>
    public static KpiSettings Default(DateOnly today)
    {
        var start = today.AddDays(-45);
        return new KpiSettings(
        [
            new(KpiKind.PagesPerDay, 20_000, 18_000),
            new(KpiKind.OcrAccuracy, 90, 88),
            new(KpiKind.AutoArchiveRate, 80, 70),
            new(KpiKind.ReviewBacklog, 300, 500),
            new(KpiKind.ReviewTime, 24, 48),
            new(KpiKind.FailureRate, 1, 2),
            new(KpiKind.PagesPerOperator, 1_800, 1_500),
        ],
        new ProjectPlan(2_000_000, start, start.AddDays(99)));
    }
}

public static class KpiRules
{
    public const int MaxProjectPages = 100_000_000;

    public static KpiDirection Direction(KpiKind kind) => kind switch
    {
        KpiKind.ReviewBacklog or KpiKind.ReviewTime or KpiKind.FailureRate => KpiDirection.AtMost,
        _ => KpiDirection.AtLeast,
    };

    public static KpiUnit Unit(KpiKind kind) => kind switch
    {
        KpiKind.PagesPerDay or KpiKind.PagesPerOperator => KpiUnit.Pages,
        KpiKind.ReviewBacklog => KpiUnit.Documents,
        KpiKind.ReviewTime => KpiUnit.Hours,
        _ => KpiUnit.Percent,
    };

    public static double MaxValue(KpiUnit unit) => unit switch
    {
        KpiUnit.Percent => 100,
        KpiUnit.Hours => 720,
        _ => 1_000_000,
    };

    /// <summary>Met at or past the target; at risk up to the alert level; off track beyond it.</summary>
    public static KpiStatus Evaluate(double actual, KpiTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Direction(target.Kind) == KpiDirection.AtLeast
            ? actual >= target.Target ? KpiStatus.OnTrack : actual >= target.Alert ? KpiStatus.AtRisk : KpiStatus.OffTrack
            : actual <= target.Target ? KpiStatus.OnTrack : actual <= target.Alert ? KpiStatus.AtRisk : KpiStatus.OffTrack;
    }

    /// <summary>On or ahead of plan; at risk within 5 % behind it; off track further behind.</summary>
    public static KpiStatus EvaluateProject(int actualPages, int plannedPages) =>
        actualPages >= plannedPages ? KpiStatus.OnTrack
        : actualPages >= plannedPages * 0.95 ? KpiStatus.AtRisk
        : KpiStatus.OffTrack;

    /// <summary>The first problem with a target and its alert level, or null when both are valid.</summary>
    public static (KpiTargetError? Target, KpiTargetError? Alert) Check(KpiKind kind, double? target, double? alert)
    {
        var max = MaxValue(Unit(kind));
        var targetError = CheckValue(target, max);
        var alertError = CheckValue(alert, max);
        if (targetError is null && alertError is null)
        {
            if (Direction(kind) == KpiDirection.AtLeast && alert > target)
                alertError = KpiTargetError.AlertAboveTarget;
            else if (Direction(kind) == KpiDirection.AtMost && alert < target)
                alertError = KpiTargetError.AlertBelowTarget;
        }

        return (targetError, alertError);
    }

    public static ProjectPlanError? Check(int? totalPages, DateOnly? start, DateOnly? end) =>
        totalPages is not (>= 1 and <= MaxProjectPages) ? ProjectPlanError.PagesOutOfRange
        : start is null || end is null ? ProjectPlanError.DatesMissing
        : end < start ? ProjectPlanError.EndBeforeStart
        : null;

    private static KpiTargetError? CheckValue(double? value, double max) =>
        value is not { } number ? KpiTargetError.Missing
        : !double.IsFinite(number) || number < 0 || number > max ? KpiTargetError.OutOfRange
        : null;
}
