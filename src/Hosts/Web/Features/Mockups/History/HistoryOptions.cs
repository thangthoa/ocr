namespace OppOcr.Web.Features.Mockups.History;

/// <summary>
/// The simulated history of the digitization project (<see cref="ProjectHistory"/>): on by default, so every page
/// shows a project that has already processed <see cref="HistoryProfile.Documents"/> documents. The component tests
/// turn it off (<c>Documents__History__Enabled=false</c>) except where they test the history itself.
/// </summary>
public sealed class HistoryOptions
{
    public const string SectionName = "Documents:History";

    public bool Enabled { get; set; } = true;
}
