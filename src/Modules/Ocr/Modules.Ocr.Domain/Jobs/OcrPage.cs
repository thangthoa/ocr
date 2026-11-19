namespace Modules.Ocr.Domain.Jobs;

/// <summary>One recognised page (API doc §4, <c>result.pages[]</c>).</summary>
/// <param name="Warnings">English notes from the service (API doc §4.1); the UI translates known ones.</param>
public sealed record OcrPage(
    int PageNumber,
    OcrDocumentType Type,
    string Text,
    double Seconds,
    string Engine,
    IReadOnlyList<string> Warnings,
    bool Cached);
