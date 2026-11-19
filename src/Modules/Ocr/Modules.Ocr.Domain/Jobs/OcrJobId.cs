using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Modules.Ocr.Domain.Jobs;

/// <summary>
/// The service's job id (<c>job_</c> + 16 characters). Validated because it arrives from URLs
/// and is placed into request paths sent to the service.
/// </summary>
public readonly partial record struct OcrJobId
{
    private OcrJobId(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse([NotNullWhen(true)] string? value, out OcrJobId id)
    {
        id = default;
        if (value is null || !Pattern().IsMatch(value))
            return false;
        id = new OcrJobId(value);
        return true;
    }

    public static OcrJobId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"'{value}' is not a valid OCR job id.");

    public override string ToString() => Value;

    [GeneratedRegex("^job_[A-Za-z0-9]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
