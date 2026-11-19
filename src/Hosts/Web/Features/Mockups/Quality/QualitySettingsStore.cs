using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Quality;

/// <summary>
/// The settings of the automatic check: in memory like the rest of the mockup (back to the defaults after a
/// restart) and shared by everyone. Every change is written to the activity log.
/// </summary>
public sealed class QualitySettingsStore(AuditLog audit)
{
    private QualitySettings _current = QualitySettings.Default;

    public QualitySettings Current => Volatile.Read(ref _current);

    public void Save(QualitySettings settings, string author)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MinDocumentConfidence, QualitySettings.MinThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.MinDocumentConfidence, QualitySettings.MaxThreshold);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MinPageConfidence, QualitySettings.MinThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.MinPageConfidence, QualitySettings.MaxThreshold);

        Volatile.Write(ref _current, settings with { RequiredMetadata = [.. settings.RequiredMetadata.Distinct().Order()] });
        audit.Record(author, AuditAction.QualitySettingsChanged);
    }
}
