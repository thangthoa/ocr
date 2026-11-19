using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Modules.Ocr.Domain.Jobs;

namespace OppOcr.Web.Features.Mockups.Quality;

/// <summary>
/// Made-up OCR confidence: the OCR service does not report one yet. Each page gets a stable value from its
/// job id and page number (the same after a restart), so the automatic check can be demonstrated on real
/// jobs. Replace it with the service's value once the API returns one (docs/development/ui-mockups.md).
/// </summary>
public static class SimulatedConfidence
{
    /// <summary>Points taken off for each warning the service reported on a page.</summary>
    public const double WarningPenalty = 15;

    /// <returns>One value per page, from page 1; null for a page with no text or missing from the result.</returns>
    public static IReadOnlyList<double?> ForJob(OcrJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var count = Math.Max(job.PagesTotal, job.Pages.Count == 0 ? 0 : job.Pages.Max(p => p.PageNumber));
        return Enumerable.Range(1, count)
            .Select(number => job.Pages.FirstOrDefault(p => p.PageNumber == number) is { } page ? ForPage(job.Id.Value, page) : null)
            .ToList();
    }

    /// <summary>
    /// Spreads a sample document's confidence over its pages, keeping the given value as their average and
    /// every page under 100 %.
    /// </summary>
    public static IReadOnlyList<double?> SpreadOverPages(string documentId, int pages, double confidence)
    {
        var amplitude = Math.Min(8, (99.9 - confidence) / 2);
        var offsets = Enumerable.Range(1, pages).Select(page => ((Unit($"{documentId}:{page}") * 2) - 1) * amplitude).ToList();
        var mean = offsets.Count == 0 ? 0 : offsets.Average();
        return offsets.Select(offset => (double?)Math.Round(confidence + offset - mean, 1)).ToList();
    }

    private static double? ForPage(string jobId, OcrPage page)
    {
        if (string.IsNullOrWhiteSpace(page.Text))
            return null;

        // Squaring keeps most pages near the best value and a few far below it, as in real OCR output.
        var spread = Unit($"{jobId}:{page.PageNumber}");
        var (best, worst) = page.Type == OcrDocumentType.Handwritten ? (95.0, 60.0) : (99.0, 72.0);
        var value = best - ((best - worst) * spread * spread) - (WarningPenalty * page.Warnings.Count);
        return Math.Round(Math.Clamp(value, 1, 99.9), 1);
    }

    /// <summary>A stable number in [0, 1) for a key; <c>string.GetHashCode</c> changes on every run.</summary>
    private static double Unit(string key)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return BinaryPrimitives.ReadUInt32LittleEndian(hash) / 4_294_967_296.0;
    }
}
