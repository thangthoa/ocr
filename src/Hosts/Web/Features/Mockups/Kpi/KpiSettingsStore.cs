using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Kpi;

/// <summary>
/// KPI targets and the project plan: in memory like the rest of the mockup (back to the defaults after a
/// restart) and shared by everyone. Every change is written to the activity log.
/// </summary>
public sealed class KpiSettingsStore(AuditLog audit, TimeProvider time)
{
    private KpiSettings? _current;

    public KpiSettings Current => Volatile.Read(ref _current) ?? Defaults;

    public KpiSettings Defaults => KpiSettings.Default(DateOnly.FromDateTime(time.GetLocalNow().DateTime));

    public void Save(KpiSettings settings, string author)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var kind in Enum.GetValues<KpiKind>())
        {
            var target = settings.Targets.SingleOrDefault(t => t.Kind == kind)
                ?? throw new ArgumentException($"The KPI {kind} has no target.", nameof(settings));
            if (KpiRules.Check(kind, target.Target, target.Alert) is not (null, null))
                throw new ArgumentException($"The target of the KPI {kind} is not valid.", nameof(settings));
        }

        var project = settings.Project;
        if (KpiRules.Check(project.TotalPages, project.Start, project.End) is not null)
            throw new ArgumentException("The project plan is not valid.", nameof(settings));

        // Kept in the order of the KPI list, whatever the order of the form.
        Volatile.Write(ref _current, settings with { Targets = [.. settings.Targets.OrderBy(t => t.Kind)] });
        audit.Record(author, AuditAction.KpiSettingsChanged);
    }
}
