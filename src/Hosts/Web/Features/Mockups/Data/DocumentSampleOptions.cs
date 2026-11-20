namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>
/// The 14 sample documents (<see cref="SampleDocumentSeed"/>). Off by default, so the lists show only real
/// OCR jobs; a demo without an OCR service can turn them on (<c>Documents__Samples__Enabled=true</c>).
/// The component tests turn them on and use them as fixtures.
/// </summary>
public sealed class DocumentSampleOptions
{
    public const string SectionName = "Documents:Samples";

    public bool Enabled { get; set; }
}
